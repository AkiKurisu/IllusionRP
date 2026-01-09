// Modified from https://github.com/StellarWarp/High-Performance-Convolution-Bloom-On-Unity/
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UObject = UnityEngine.Object;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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

        private static class ShaderProperties
        {
            public static readonly int FFTExtend = Shader.PropertyToID("_FFT_EXTEND");

            public static readonly int Threshold = Shader.PropertyToID("_Threshlod");

            public static readonly int ThresholdKnee = Shader.PropertyToID("_ThresholdKnee");

            public static readonly int TexelSize = Shader.PropertyToID("_TexelSize");

            public static readonly int MaxClamp = Shader.PropertyToID("_MaxClamp");

            public static readonly int MinClamp = Shader.PropertyToID("_MinClamp");

            public static readonly int KernelPow = Shader.PropertyToID("_Power");

            public static readonly int KernelScaler = Shader.PropertyToID("_Scaler");

            public static readonly int ScreenX = Shader.PropertyToID("_ScreenX");

            public static readonly int ScreenY = Shader.PropertyToID("_ScreenY");

            public static readonly int EnableRemap = Shader.PropertyToID("_EnableRemap");

            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
        }

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
#if UNITY_2023_1_OR_NEWER
            ConfigureInput(ScriptableRenderPassInput.Color);
#endif
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
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
            RenderingUtils.ReAllocateIfNeeded(ref _otf, otfDesc, name: "ConvolutionBloom_OTF");

            // Allocate FFT target texture
            var fftTargetDesc = new RenderTextureDescriptor(width, targetTexHeight, format, 0)
            {
                enableRandomWrite = true,
                useMipMap = false
            };
            RenderingUtils.ReAllocateIfNeeded(ref _fftTarget, fftTargetDesc, wrapMode: TextureWrapMode.Clamp, name: "ConvolutionBloom_FFTTarget");

            // Allocate PSF texture
            var psfDesc = new RenderTextureDescriptor(width, height, format, 0)
            {
                enableRandomWrite = true,
                useMipMap = false
            };
            RenderingUtils.ReAllocateIfNeeded(ref _psf, psfDesc, name: "ConvolutionBloom_PSF");
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
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var bloomParams = VolumeManager.instance.stack.GetComponent<ConvolutionBloom>();
            if (bloomParams == null) return;
            if (!bloomParams.IsActive()) return;
            float threshold = bloomParams.threshold.value;
            float thresholdKnee = bloomParams.scatter.value;
            float clampMax = bloomParams.clamp.value;
            float intensity = bloomParams.intensity.value;
            var fftExtend = bloomParams.fftExtend.value;
            bool highQuality = bloomParams.quality.value == ConvolutionBloomQuality.High;

            UpdateRenderTextureSize(bloomParams);

            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                var targetX = renderingData.cameraData.camera.pixelWidth;
                var targetY = renderingData.cameraData.camera.pixelHeight;
                if (bloomParams.IsParamUpdated())
                {
                    OpticalTransferFunctionUpdate(cmd, bloomParams, new Vector2Int(targetX, targetY), highQuality);
                }

                var colorTargetHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;

                // Bright mask extraction
                using (new ProfilingScope(cmd, _brightMaskSampler))
                {
                    if (!bloomParams.disableReadWriteOptimization.value) fftExtend.y = 0;
                    _brightMaskMaterial.Value.SetVector(ShaderProperties.FFTExtend, fftExtend);
                    _brightMaskMaterial.Value.SetFloat(ShaderProperties.Threshold, threshold);
                    _brightMaskMaterial.Value.SetFloat(ShaderProperties.ThresholdKnee, thresholdKnee);
                    _brightMaskMaterial.Value.SetFloat(ShaderProperties.MaxClamp, clampMax);
                    _brightMaskMaterial.Value.SetVector(ShaderProperties.TexelSize, new Vector4(1f / targetX, 1f / targetY, 0, 0));
                    CoreUtils.SetRenderTarget(cmd, _fftTarget);
                    Blitter.BlitTexture(cmd, colorTargetHandle, Vector2.one, _brightMaskMaterial.Value, 0);
                }

                // FFT Convolution
                using (new ProfilingScope(cmd, _fftConvolutionSampler))
                {
                    Vector2Int size = new Vector2Int((int)_convolutionSizeX, (int)_convolutionSizeY);
                    Vector2Int horizontalRange = Vector2Int.zero;
                    Vector2Int verticalRange = Vector2Int.zero;
                    Vector2Int offset = Vector2Int.zero;

                    if (!bloomParams.disableReadWriteOptimization.value)
                    {
                        int paddingY = (size.y - _fftTarget.rt.height) / 2;
                        verticalRange = new Vector2Int(0, _fftTarget.rt.height);
                        offset = new Vector2Int(0, -paddingY);
                    }

                    if (bloomParams.disableDispatchMergeOptimization.value)
                    {
                        _fftKernel.Convolve(cmd, _fftTarget, _otf, highQuality);
                    }
                    else
                    {
                        _fftKernel.ConvolveOpt(cmd, _fftTarget, _otf,
                            size,
                            horizontalRange,
                            verticalRange,
                            offset);
                    }
                }

                // Bloom blend
                using (new ProfilingScope(cmd, _bloomBlendSampler))
                {
                    _bloomBlendMaterial.Value.SetVector(ShaderProperties.FFTExtend, fftExtend);
                    _bloomBlendMaterial.Value.SetFloat(ShaderProperties.Intensity, intensity);
                    CoreUtils.SetRenderTarget(cmd, colorTargetHandle);
                    Blitter.BlitTexture(cmd, _fftTarget, Vector2.one, _bloomBlendMaterial.Value, 0);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void OpticalTransferFunctionUpdate(CommandBuffer cmd, ConvolutionBloom param, Vector2Int size, bool highQuality)
        {
            // PSF Remap/Generation
            using (new ProfilingScope(cmd, _otfPsfSampler))
            {
                _psfRemapMaterial.Value.SetFloat(ShaderProperties.MaxClamp, param.imagePSFMaxClamp.value);
                _psfRemapMaterial.Value.SetFloat(ShaderProperties.MinClamp, param.imagePSFMinClamp.value);
                _psfRemapMaterial.Value.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
                _psfRemapMaterial.Value.SetFloat(ShaderProperties.KernelPow, param.imagePSFPow.value);
                _psfRemapMaterial.Value.SetFloat(ShaderProperties.KernelScaler, param.imagePSFScale.value);
                _psfRemapMaterial.Value.SetInt(ShaderProperties.ScreenX, size.x);
                _psfRemapMaterial.Value.SetInt(ShaderProperties.ScreenY, size.y);
                if (param.generatePSF.value)
                {
                    _psfGeneratorMaterial.Value.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
                    _psfGeneratorMaterial.Value.SetInt(ShaderProperties.ScreenX, size.x);
                    _psfGeneratorMaterial.Value.SetInt(ShaderProperties.ScreenY, size.y);
                    _psfGeneratorMaterial.Value.SetInt(ShaderProperties.EnableRemap, 1);
                    CoreUtils.SetRenderTarget(cmd, _otf.rt);
                    Blitter.BlitTexture(cmd, Vector2.one, _psfGeneratorMaterial.Value, 0);
                }
                else
                {
                    _psfRemapMaterial.Value.SetInt(ShaderProperties.EnableRemap, 1);
                    CoreUtils.SetRenderTarget(cmd, _otf);
                    Blitter.BlitTexture(cmd, param.imagePSF.value, Vector2.one, _psfRemapMaterial.Value, 0);
                }
            }

            // FFT
            using (new ProfilingScope(cmd, _otfFftSampler))
            {
                _fftKernel.FFT(cmd, _otf, highQuality);
            }
        }

#if UNITY_2023_1_OR_NEWER
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
            internal ConvolutionBloom Param;
            internal Vector2Int Size;
            internal bool HighQuality;
            internal FFTKernel FFTKernel;
            internal TextureHandle OtfTextureHandle;
            internal TextureHandle ImagePsfTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            var bloomParams = VolumeManager.instance.stack.GetComponent<ConvolutionBloom>();
            if (bloomParams == null || !bloomParams.IsActive()) return;
            
            float threshold = bloomParams.threshold.value;
            float thresholdKnee = bloomParams.scatter.value;
            float clampMax = bloomParams.clamp.value;
            float intensity = bloomParams.intensity.value;
            var fftExtend = bloomParams.fftExtend.value;
            bool highQuality = bloomParams.quality.value == ConvolutionBloomQuality.High;
            if (!bloomParams.disableReadWriteOptimization.value) fftExtend.y = 0;
            
            UpdateRenderTextureSize(bloomParams);

            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle colorTexture = renderer.activeColorTexture;
            var targetX = renderingData.cameraData.camera.pixelWidth;
            var targetY = renderingData.cameraData.camera.pixelHeight;
            
            // Update OTF if parameters changed
            if (bloomParams.IsParamUpdated())
            {
                RenderOTFUpdatePass(renderGraph, bloomParams, new Vector2Int(targetX, targetY), highQuality);
            }
            
            // Import RTHandles as TextureHandles
            TextureHandle fftTargetHandle = renderGraph.ImportTexture(_fftTarget);
            TextureHandle otfHandle = renderGraph.ImportTexture(_otf);

            // Pass 1: Bright mask extraction
            fftTargetHandle = RenderBrightMaskPass(renderGraph, colorTexture, fftTargetHandle,
                bloomParams, threshold, thresholdKnee, clampMax, new Vector2Int(targetX, targetY), fftExtend);

            // Pass 2: FFT Convolution
            fftTargetHandle = RenderConvolutionPass(renderGraph, fftTargetHandle, otfHandle,
                bloomParams, highQuality);

            // Pass 3: Bloom blend
            RenderBloomBlendPass(renderGraph, fftTargetHandle, colorTexture, intensity, fftExtend);
        }

        private TextureHandle RenderBrightMaskPass(RenderGraph renderGraph, TextureHandle source,
            TextureHandle destination, ConvolutionBloom bloomParams, float threshold, float thresholdKnee,
            float maxClamp, Vector2Int screenSize, Vector2 fftExtend)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BrightMaskPassData>("Convolution Bloom Bright Mask", out var passData, _brightMaskSampler))
            {
                passData.BrightMaskMaterial = _brightMaskMaterial.Value;
                passData.FFTExtend = fftExtend;
                passData.Threshold = threshold;
                passData.ThresholdKnee = thresholdKnee;
                passData.MaxClamp = maxClamp;
                passData.TexelSize = new Vector4(1f / screenSize.x, 1f / screenSize.y, 0, 0);
                
                passData.Source = builder.UseTexture(source);
                builder.UseTextureFragment(destination, 0);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (BrightMaskPassData data, RasterGraphContext context) =>
                {
                    data.BrightMaskMaterial.SetVector(ShaderProperties.FFTExtend, data.FFTExtend);
                    data.BrightMaskMaterial.SetFloat(ShaderProperties.Threshold, data.Threshold);
                    data.BrightMaskMaterial.SetFloat(ShaderProperties.ThresholdKnee, data.ThresholdKnee);
                    data.BrightMaskMaterial.SetFloat(ShaderProperties.MaxClamp, data.MaxClamp);
                    data.BrightMaskMaterial.SetVector(ShaderProperties.TexelSize, data.TexelSize);
                    
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
                builder.UseTexture(fftTarget, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
                builder.UseTexture(ortFilter, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);

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
                
                passData.Source = builder.UseTexture(source);
                builder.UseTextureFragment(destination, 0);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (BloomBlendPassData data, RasterGraphContext context) =>
                {
                    data.BloomBlendMaterial.SetVector(ShaderProperties.FFTExtend, data.FFTExtend);
                    data.BloomBlendMaterial.SetFloat(ShaderProperties.Intensity, data.Intensity);
                    
                    Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.BloomBlendMaterial, 0);
                });
            }
        }

        private void RenderOTFUpdatePass(RenderGraph renderGraph, ConvolutionBloom param, Vector2Int size, bool highQuality)
        {
            TextureHandle otfHandle = renderGraph.ImportTexture(_otf);
            
            // PSF Remap/Generation Pass (Raster)
            using (var builder = renderGraph.AddRasterRenderPass<OTFUpdatePassData>("Convolution Bloom OTF PSF", out var passData, _otfPsfSampler))
            {
                passData.PsfRemapMaterial = _psfRemapMaterial.Value;
                passData.PsfGeneratorMaterial = _psfGeneratorMaterial.Value;
                passData.Param = param;
                passData.Size = size;
                passData.OtfTextureHandle = builder.UseTextureFragment(otfHandle, 0);
                
                if (!param.generatePSF.value && param.imagePSF.value != null)
                {
                    // Cache RTHandle for imagePSF texture
                    if (_imagePsfRTHandle == null || _imagePsfRTHandle.rt != param.imagePSF.value)
                    {
                        _imagePsfRTHandle?.Release();
                        _imagePsfRTHandle = RTHandles.Alloc(param.imagePSF.value);
                    }
                    passData.ImagePsfTexture = builder.UseTexture(renderGraph.ImportTexture(_imagePsfRTHandle));
                }

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((OTFUpdatePassData data, RasterGraphContext context) =>
                {
                    data.PsfRemapMaterial.SetFloat(ShaderProperties.MaxClamp, data.Param.imagePSFMaxClamp.value);
                    data.PsfRemapMaterial.SetFloat(ShaderProperties.MinClamp, data.Param.imagePSFMinClamp.value);
                    data.PsfRemapMaterial.SetVector(ShaderProperties.FFTExtend, data.Param.fftExtend.value);
                    data.PsfRemapMaterial.SetFloat(ShaderProperties.KernelPow, data.Param.imagePSFPow.value);
                    data.PsfRemapMaterial.SetFloat(ShaderProperties.KernelScaler, data.Param.imagePSFScale.value);
                    data.PsfRemapMaterial.SetInt(ShaderProperties.ScreenX, data.Size.x);
                    data.PsfRemapMaterial.SetInt(ShaderProperties.ScreenY, data.Size.y);
                    
                    if (data.Param.generatePSF.value)
                    {
                        data.PsfGeneratorMaterial.SetVector(ShaderProperties.FFTExtend, data.Param.fftExtend.value);
                        data.PsfGeneratorMaterial.SetInt(ShaderProperties.ScreenX, data.Size.x);
                        data.PsfGeneratorMaterial.SetInt(ShaderProperties.ScreenY, data.Size.y);
                        data.PsfGeneratorMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                        
                        Blitter.BlitTexture(context.cmd, Vector2.one, data.PsfGeneratorMaterial, 0);
                    }
                    else
                    {
                        data.PsfRemapMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                        Blitter.BlitTexture(context.cmd, data.ImagePsfTexture, Vector2.one, data.PsfRemapMaterial, 0);
                    }
                });
            }

            // FFT Pass (Compute)
            using (var builder = renderGraph.AddComputePass<OTFUpdatePassData>("Convolution Bloom OTF FFT", out var passData, _otfFftSampler))
            {
                passData.FFTKernel = _fftKernel;
                passData.HighQuality = highQuality;
                passData.OtfTextureHandle = builder.UseTexture(otfHandle, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (OTFUpdatePassData data, ComputeGraphContext context) =>
                {
                    data.FFTKernel.FFT(context.cmd, data.OtfTextureHandle, data.HighQuality);
                });
            }
        }
#endif
    }
}