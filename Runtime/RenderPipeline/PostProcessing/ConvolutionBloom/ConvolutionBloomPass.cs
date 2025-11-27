// Modified from https://github.com/StellarWarp/High-Performance-Convolution-Bloom-On-Unity/
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.PostProcessing
{
    public class ConvolutionBloomPass : ScriptableRenderPass, IDisposable
    {
        private readonly FFTKernel _fftKernel;

        private FFTKernel.FFTSize _convolutionSizeX = FFTKernel.FFTSize.Size512;

        private FFTKernel.FFTSize _convolutionSizeY = FFTKernel.FFTSize.Size256;

        private RenderTexture _fftTarget;

        private RenderTexture _psf;

        private RenderTexture _otf;

        private Material _brightMaskMaterial;

        private Material _bloomBlendMaterial;

        private Material _psfRemapMaterial;

        private Material _psfGeneratorMaterial;

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
            
            int width = (int)sizeX;
            int height = (int)sizeY;
            
            const RenderTextureFormat format = RenderTextureFormat.ARGBHalf;
            int verticalPadding = Mathf.FloorToInt(height * bloomParam.fftExtend.value.y);
            int targetTexHeight = bloomParam.disableReadWriteOptimization.value ? height : height - 2 * verticalPadding;
            if (!_otf || !_fftTarget || !_psf || _convolutionSizeX != sizeX
                || _convolutionSizeY != sizeY || _fftTarget.height != targetTexHeight
                || _fftTarget.format != format)
            {
                _convolutionSizeX = sizeX;
                _convolutionSizeY = sizeY;

                if (!_otf || _otf.width != width || _otf.height != height || _otf.format != format)
                {
                    if (_otf) _otf.Release();
                    _otf = new RenderTexture(width, height, 0,
                        format, RenderTextureReadWrite.Linear)
                    {
                        depthStencilFormat = GraphicsFormat.None,
                        enableRandomWrite = true
                    };
                    _otf.Create();
                }

                if (!_fftTarget || _fftTarget.width != width || _fftTarget.height != targetTexHeight ||
                    _fftTarget.format != format)
                {
                    if (_fftTarget) _fftTarget.Release();
                    _fftTarget = new RenderTexture(width, targetTexHeight, 0,
                        format, RenderTextureReadWrite.Linear)
                    {
                        depthStencilFormat = GraphicsFormat.None,
                        wrapMode = TextureWrapMode.Clamp,
                        enableRandomWrite = true
                    };
                    _fftTarget.Create();
                }

                if (!_psf || _psf.width != width || _psf.height != height || _psf.format != format)
                {
                    if (_psf) _psf.Release();
                    _psf = new RenderTexture(width, height, 0,
                        format, RenderTextureReadWrite.Linear)
                    {
                        depthStencilFormat = GraphicsFormat.None,
                        enableRandomWrite = true
                    };
                    _psf.Create();
                }
            }
        }

        public void Dispose()
        {
            if (_psf) _psf.Release();
            if (_otf) _otf.Release();
            if (_fftTarget) _fftTarget.Release();
            if (_brightMaskMaterial) UObject.DestroyImmediate(_brightMaskMaterial);
            if (_bloomBlendMaterial) UObject.DestroyImmediate(_bloomBlendMaterial);
            if (_psfRemapMaterial) UObject.DestroyImmediate(_psfRemapMaterial);
            if (_psfGeneratorMaterial) UObject.DestroyImmediate(_psfGeneratorMaterial);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!_brightMaskMaterial) _brightMaskMaterial = CoreUtils.CreateEngineMaterial(IllusionShaders.ConvolutionBloomBrightMask);
            if (!_bloomBlendMaterial) _bloomBlendMaterial = CoreUtils.CreateEngineMaterial(IllusionShaders.ConvolutionBloomBlend);
            if (!_psfRemapMaterial) _psfRemapMaterial = CoreUtils.CreateEngineMaterial(IllusionShaders.ConvolutionBloomPsfRemap);
            if (!_psfGeneratorMaterial) _psfGeneratorMaterial = CoreUtils.CreateEngineMaterial(IllusionShaders.ConvolutionBloomPsfGenerator);
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
                if (colorTargetHandle.rt == null) return;

                if (!bloomParams.disableReadWriteOptimization.value) fftExtend.y = 0;
                _brightMaskMaterial.SetVector(ShaderProperties.FFTExtend, fftExtend);
                _brightMaskMaterial.SetFloat(ShaderProperties.Threshold, threshold);
                _brightMaskMaterial.SetFloat(ShaderProperties.ThresholdKnee, thresholdKnee);
                _brightMaskMaterial.SetFloat(ShaderProperties.MaxClamp, clampMax);
                _brightMaskMaterial.SetVector(ShaderProperties.TexelSize, new Vector4(1f / targetX, 1f / targetY, 0, 0));
                cmd.Blit(colorTargetHandle, _fftTarget, _brightMaskMaterial);

                Vector2Int size = new Vector2Int((int)_convolutionSizeX, (int)_convolutionSizeY);
                Vector2Int horizontalRange = Vector2Int.zero;
                Vector2Int verticalRange = Vector2Int.zero;
                Vector2Int offset = Vector2Int.zero;

                if (!bloomParams.disableReadWriteOptimization.value)
                {
                    int paddingY = (size.y - _fftTarget.height) / 2;
                    verticalRange = new Vector2Int(0, _fftTarget.height);
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

                _bloomBlendMaterial.SetVector(ShaderProperties.FFTExtend, fftExtend);
                _bloomBlendMaterial.SetFloat(ShaderProperties.Intensity, intensity);
                cmd.Blit(_fftTarget, colorTargetHandle, _bloomBlendMaterial);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void OpticalTransferFunctionUpdate(CommandBuffer cmd, ConvolutionBloom param, Vector2Int size, bool highQuality)
        {
            _psfRemapMaterial.SetFloat(ShaderProperties.MaxClamp, param.imagePSFMaxClamp.value);
            _psfRemapMaterial.SetFloat(ShaderProperties.MinClamp, param.imagePSFMinClamp.value);
            _psfRemapMaterial.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
            _psfRemapMaterial.SetFloat(ShaderProperties.KernelPow, param.imagePSFPow.value);
            _psfRemapMaterial.SetFloat(ShaderProperties.KernelScaler, param.imagePSFScale.value);
            _psfRemapMaterial.SetInt(ShaderProperties.ScreenX, size.x);
            _psfRemapMaterial.SetInt(ShaderProperties.ScreenY, size.y);
            if (param.generatePSF.value)
            {
                _psfGeneratorMaterial.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
                _psfGeneratorMaterial.SetInt(ShaderProperties.ScreenX, size.x);
                _psfGeneratorMaterial.SetInt(ShaderProperties.ScreenY, size.y);
                _psfGeneratorMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                cmd.Blit(_otf, _otf, _psfGeneratorMaterial);
            }
            else
            {
                _psfRemapMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                cmd.Blit(param.imagePSF.value, _otf, _psfRemapMaterial);
            }

            _fftKernel.FFT(_otf, cmd, highQuality);
        }
    }
}