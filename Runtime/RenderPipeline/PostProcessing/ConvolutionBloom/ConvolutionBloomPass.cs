// Modified from https://github.com/StellarWarp/High-Performance-Convolution-Bloom-On-Unity/
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    public class ConvolutionBloomPass : ScriptableRenderPass, IDisposable
    {
        private readonly FFTKernel _fftKernel;

        private FFTKernel.FFTSize _convolutionSizeX = FFTKernel.FFTSize.Size512;

        private FFTKernel.FFTSize _convolutionSizeY = FFTKernel.FFTSize.Size256;

        private RTHandle _fftTarget;

        private RTHandle _psf;

        private RTHandle _otf;
        
        private RTHandle _imagePsfRTHandle;

        private readonly LazyMaterial _brightMaskMaterial;

        private readonly LazyMaterial _bloomBlendMaterial;

        private readonly LazyMaterial _psfRemapMaterial;

        private readonly LazyMaterial _psfGeneratorMaterial;

        private readonly ProfilingSampler _brightMaskSampler = new("Convolution Bloom Bright Mask");
        
        private readonly ProfilingSampler _fftConvolutionSampler = new("Convolution Bloom FFT Convolution");
        
        private readonly ProfilingSampler _bloomBlendSampler = new("Convolution Bloom Blend");
        
        private readonly ProfilingSampler _otfPsfSampler = new("Convolution Bloom OTF PSF");
        
        private readonly ProfilingSampler _otfFftSampler = new("Convolution Bloom OTF FFT");

        private bool _psfInitialized;

        public ConvolutionBloomPass(IllusionRendererData rendererData)
        {
            _fftKernel = new FFTKernel(rendererData.RuntimeResources.fastFourierTransformCS,
                rendererData.RuntimeResources.fastFourierConvolveCS);
            renderPassEvent = IllusionRenderPassEvent.CustomPostProcessPass;
            profilingSampler = new ProfilingSampler("Convolution Bloom");
            _brightMaskMaterial = new LazyMaterial(IllusionShaders.ConvolutionBloomBrightMask);
            _bloomBlendMaterial = new LazyMaterial(IllusionShaders.ConvolutionBloomBlend);
            _psfRemapMaterial = new LazyMaterial(IllusionShaders.ConvolutionBloomPsfRemap);
            _psfGeneratorMaterial = new LazyMaterial(IllusionShaders.ConvolutionBloomPsfGenerator);
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        private void UpdateRenderTextureSize(ConvolutionBloom bloomParam)
        {
            FFTKernel.FFTSize sizeX;
            FFTKernel.FFTSize sizeY;
            if (bloomParam.quality.value == ConvolutionBloomQuality.High)
            {
                sizeX = FFTKernel.FFTSize.Size1024;
                sizeY = FFTKernel.FFTSize.Size512;
            }
            else
            {
                sizeX = FFTKernel.FFTSize.Size512;
                sizeY = FFTKernel.FFTSize.Size256;
            }
            
            _convolutionSizeX = sizeX;
            _convolutionSizeY = sizeY;
            
            int width = (int)sizeX;
            int height = (int)sizeY;
            
            const GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat;
            int verticalPadding = Mathf.FloorToInt(height * bloomParam.fftExtend.value.y);
            int targetTexHeight = bloomParam.disableReadWriteOptimization.value ? height : height - 2 * verticalPadding;

            // Allocate OTF texture
            var otfDesc = new RenderTextureDescriptor(width, height, format, 0)
            {
                enableRandomWrite = true,
                useMipMap = false
            };
            RenderingUtils.ReAllocateHandleIfNeeded(ref _otf, otfDesc, name: "ConvolutionBloom_OTF");

            // Allocate FFT target texture
            var fftTargetDesc = new RenderTextureDescriptor(width, targetTexHeight, format, 0)
            {
                enableRandomWrite = true,
                useMipMap = false
            };
            RenderingUtils.ReAllocateHandleIfNeeded(ref _fftTarget, fftTargetDesc, wrapMode: TextureWrapMode.Clamp, name: "ConvolutionBloom_FFTTarget");

            // Allocate PSF texture
            var psfDesc = new RenderTextureDescriptor(width, height, format, 0)
            {
                enableRandomWrite = true,
                useMipMap = false
            };
            RenderingUtils.ReAllocateHandleIfNeeded(ref _psf, psfDesc, name: "ConvolutionBloom_PSF");
        }

        public void Dispose()
        {
            _psf?.Release();
            _psf = null;
            _otf?.Release();
            _otf = null;
            _fftTarget?.Release();
            _fftTarget = null;
            _imagePsfRTHandle?.Release();
            _imagePsfRTHandle = null;
            
            _brightMaskMaterial.DestroyCache();
            _bloomBlendMaterial.DestroyCache();
            _psfRemapMaterial.DestroyCache();
            _psfGeneratorMaterial.DestroyCache();
        }

        // RenderGraph PassData classes
        private class BrightMaskPassData
        {
            internal Material BrightMaskMaterial;
            internal Vector2 FFTExtend;
            internal float Threshold;
            internal float ThresholdKnee;
            internal float MaxClamp;
            internal Vector4 TexelSize;
            internal TextureHandle Source;
        }

        private class ConvolutionPassData
        {
            internal FFTKernel FFTKernel;
            internal TextureHandle Target;
            internal TextureHandle Filter;
            internal bool HighQuality;
            internal bool DisableDispatchMergeOptimization;
            internal bool DisableReadWriteOptimization;
            internal Vector2Int Size;
        }

        private class BloomBlendPassData
        {
            internal Material BloomBlendMaterial;
            internal Vector2 FFTExtend;
            internal float Intensity;
            internal TextureHandle Source;
        }

        private class OTFUpdatePassData
        {
            internal Material PsfRemapMaterial;
            internal Material PsfGeneratorMaterial;
            internal bool HighQuality;
            internal FFTKernel FFTKernel;
            internal TextureHandle OtfTextureHandle;
            internal TextureHandle ImagePsfTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var bloomParams = VolumeManager.instance.stack.GetComponent<ConvolutionBloom>();
            if (!bloomParams || !bloomParams.IsActive()) return;
            
            float threshold = bloomParams.threshold.value;
            float thresholdKnee = bloomParams.scatter.value;
            float clampMax = bloomParams.clamp.value;
            float intensity = bloomParams.intensity.value;
            var fftExtend = bloomParams.fftExtend.value;
            bool highQuality = bloomParams.quality.value == ConvolutionBloomQuality.High;
            if (!bloomParams.disableReadWriteOptimization.value) fftExtend.y = 0;
            
            UpdateRenderTextureSize(bloomParams);

            var resource = frameData.Get<UniversalResourceData>();
            TextureHandle colorTexture = resource.activeColorTexture;
            
            // Update OTF if parameters changed
            if (bloomParams.IsParamUpdated() || !_psfInitialized)
            {
                _psfInitialized = true;
                RenderOTFUpdatePass(renderGraph, bloomParams, highQuality);
            }
            
            // Import RTHandles as TextureHandles
            TextureHandle fftTargetHandle = renderGraph.ImportTexture(_fftTarget);
            TextureHandle otfHandle = renderGraph.ImportTexture(_otf);

            // Pass 1: Bright mask extraction
            fftTargetHandle = RenderBrightMaskPass(renderGraph, colorTexture, fftTargetHandle, threshold, thresholdKnee, clampMax, fftExtend);

            // Pass 2: FFT Convolution
            fftTargetHandle = RenderConvolutionPass(renderGraph, fftTargetHandle, otfHandle,
                bloomParams, highQuality);

            // Pass 3: Bloom blend
            RenderBloomBlendPass(renderGraph, fftTargetHandle, colorTexture, intensity, fftExtend);
        }

        private TextureHandle RenderBrightMaskPass(RenderGraph renderGraph, TextureHandle source,
            TextureHandle destination, float threshold, float thresholdKnee,
            float maxClamp, Vector2 fftExtend)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BrightMaskPassData>("Convolution Bloom Bright Mask", out var passData, _brightMaskSampler))
            {
                passData.BrightMaskMaterial = _brightMaskMaterial.Value;
                passData.FFTExtend = fftExtend;
                passData.Threshold = threshold;
                passData.ThresholdKnee = thresholdKnee;
                passData.MaxClamp = maxClamp;
                
                builder.UseTexture(source);
                passData.Source = source;
                builder.SetRenderAttachment(destination, 0);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (BrightMaskPassData data, RasterGraphContext context) =>
                {
                    data.BrightMaskMaterial.SetVector(ShaderProperties.FFTExtend, data.FFTExtend);
                    data.BrightMaskMaterial.SetFloat(ShaderProperties.Threshold, data.Threshold);
                    data.BrightMaskMaterial.SetFloat(ShaderProperties.ThresholdKnee, data.ThresholdKnee);
                    data.BrightMaskMaterial.SetFloat(ShaderProperties.MaxClamp, data.MaxClamp);
                    
                    Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.BrightMaskMaterial, 0);
                });

                return destination;
            }
        }

        private TextureHandle RenderConvolutionPass(RenderGraph renderGraph, TextureHandle fftTarget,
            TextureHandle ortFilter, ConvolutionBloom bloomParams, bool highQuality)
        {
            using (var builder = renderGraph.AddComputePass<ConvolutionPassData>("Convolution Bloom FFT", out var passData, _fftConvolutionSampler))
            {
                passData.FFTKernel = _fftKernel;
                passData.Target = fftTarget;
                passData.Filter = ortFilter;
                passData.HighQuality = highQuality;
                passData.DisableDispatchMergeOptimization = bloomParams.disableDispatchMergeOptimization.value;
                passData.DisableReadWriteOptimization = bloomParams.disableReadWriteOptimization.value;
                passData.Size = new Vector2Int((int)_convolutionSizeX, (int)_convolutionSizeY);

                // Mark textures as used in the pass
                builder.UseTexture(fftTarget, AccessFlags.ReadWrite);
                builder.UseTexture(ortFilter, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (ConvolutionPassData data, ComputeGraphContext context) =>
                {
                    if (data.DisableDispatchMergeOptimization)
                    {
                        data.FFTKernel.Convolve(context.cmd, data.Target, data.Filter, data.HighQuality);
                    }
                    else
                    {
                        Vector2Int offset = Vector2Int.zero;
                        Vector2Int verticalRange = Vector2Int.zero;
                        if (!data.DisableReadWriteOptimization)
                        {
                            int paddingY = (data.Size.y - ((RTHandle)data.Target).rt.height) / 2;
                            verticalRange = new Vector2Int(0, ((RTHandle)data.Target).rt.height);
                            offset = new Vector2Int(0, -paddingY);
                        }
                        data.FFTKernel.ConvolveOpt(context.cmd, data.Target, data.Filter, data.Size, Vector2Int.zero, verticalRange, offset);
                    }
                });

                return fftTarget;
            }
        }

        private void RenderBloomBlendPass(RenderGraph renderGraph, TextureHandle source,
            TextureHandle destination, float intensity, Vector2 fftExtend)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BloomBlendPassData>("Convolution Bloom Blend", out var passData, _bloomBlendSampler))
            {
                passData.BloomBlendMaterial = _bloomBlendMaterial.Value;
                passData.FFTExtend = fftExtend;
                passData.Intensity = intensity;
                
                builder.UseTexture(source);
                passData.Source = source;
                builder.SetRenderAttachment(destination, 0);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (BloomBlendPassData data, RasterGraphContext context) =>
                {
                    data.BloomBlendMaterial.SetVector(ShaderProperties.FFTExtend, data.FFTExtend);
                    data.BloomBlendMaterial.SetFloat(ShaderProperties.Intensity, data.Intensity);
                    
                    Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.BloomBlendMaterial, 0);
                });
            }
        }

        private void RenderOTFUpdatePass(RenderGraph renderGraph, ConvolutionBloom param, bool highQuality)
        {
            TextureHandle otfHandle = renderGraph.ImportTexture(_otf);
            
            // PSF Remap/Generation Pass (Raster)
            using (var builder = renderGraph.AddRasterRenderPass<OTFUpdatePassData>("Convolution Bloom OTF PSF", out var passData, _otfPsfSampler))
            {
                passData.PsfRemapMaterial = _psfRemapMaterial.Value;
                passData.PsfGeneratorMaterial = _psfGeneratorMaterial.Value;
                builder.SetRenderAttachment(otfHandle, 0);
                passData.OtfTextureHandle = otfHandle;
                
                builder.AllowPassCulling(false);
                
                if (!param.generatePSF.value && param.imagePSF.value)
                {
                    // Cache RTHandle for imagePSF texture
                    if (_imagePsfRTHandle == null || _imagePsfRTHandle.rt != param.imagePSF.value)
                    {
                        _imagePsfRTHandle?.Release();
                        _imagePsfRTHandle = RTHandles.Alloc(param.imagePSF.value);
                    }

                    var psf = renderGraph.ImportTexture(_imagePsfRTHandle);
                    builder.UseTexture(psf);
                    passData.ImagePsfTexture = psf;
                    passData.PsfRemapMaterial.SetFloat(ShaderProperties.MaxClamp, param.imagePSFMaxClamp.value);
                    passData.PsfRemapMaterial.SetFloat(ShaderProperties.MinClamp, param.imagePSFMinClamp.value);
                    passData.PsfRemapMaterial.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
                    passData.PsfRemapMaterial.SetFloat(ShaderProperties.KernelPow, param.imagePSFPow.value);
                    passData.PsfRemapMaterial.SetFloat(ShaderProperties.KernelScaler, param.imagePSFScale.value);
                    passData.PsfRemapMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                    builder.SetRenderFunc(static (OTFUpdatePassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.ImagePsfTexture, Vector2.one, data.PsfRemapMaterial, 0);
                    });
                }
                else
                {
                    passData.PsfGeneratorMaterial.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
                    passData.PsfGeneratorMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                    builder.SetRenderFunc(static (OTFUpdatePassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, Vector2.one, data.PsfGeneratorMaterial, 0);
                    });
                }
            }

            // FFT Pass (Compute)
            using (var builder = renderGraph.AddComputePass<OTFUpdatePassData>("Convolution Bloom OTF FFT", out var passData, _otfFftSampler))
            {
                passData.FFTKernel = _fftKernel;
                passData.HighQuality = highQuality;
                builder.UseTexture(otfHandle, AccessFlags.ReadWrite);
                passData.OtfTextureHandle = otfHandle;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (OTFUpdatePassData data, ComputeGraphContext context) =>
                {
                    data.FFTKernel.FFT(context.cmd, data.OtfTextureHandle, data.HighQuality);
                });
            }
        }
        
        private static class ShaderProperties
        {
            public static readonly int FFTExtend = Shader.PropertyToID("_FFT_EXTEND");

            public static readonly int Threshold = Shader.PropertyToID("_Threshlod");

            public static readonly int ThresholdKnee = Shader.PropertyToID("_ThresholdKnee");

            public static readonly int MaxClamp = Shader.PropertyToID("_MaxClamp");

            public static readonly int MinClamp = Shader.PropertyToID("_MinClamp");

            public static readonly int KernelPow = Shader.PropertyToID("_Power");

            public static readonly int KernelScaler = Shader.PropertyToID("_Scaler");

            public static readonly int EnableRemap = Shader.PropertyToID("_EnableRemap");

            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
        }
    }
}