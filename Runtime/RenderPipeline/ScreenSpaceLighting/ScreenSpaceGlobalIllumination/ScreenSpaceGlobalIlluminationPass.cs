using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class ScreenSpaceGlobalIlluminationPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;

        private readonly ComputeShader _ssgiComputeShader;

        private readonly ComputeShader _diffuseDenoiserCS;

        private readonly ComputeShader _bilateralUpsampleCS;

        private readonly ComputeShader _temporalFilterCS;

        private readonly int _traceKernel;

        private readonly int _traceHalfKernel;

        private readonly int _reprojectKernel;

        private readonly int _reprojectHalfKernel;

        private readonly int _validateHistoryKernel;

        private readonly int _temporalAccumulationColorKernel;

        private readonly int _temporalFilterCopyHistoryKernel;

        private readonly int _generatePointDistributionKernel;

        private readonly int _bilateralFilterColorKernel;

        private readonly int _gatherColorKernel;

        private readonly int _bilateralUpsampleKernel;

        private RTHandle _hitPointRT;

        private RTHandle _outputRT;

        private RTHandle _denoisedRT;

        private RTHandle _temporalRT;

        private RTHandle _temporalRT2;

        private RTHandle _denoisedRT2;

        private RTHandle _upsampledRT;

        private RTHandle _intermediateRT;

        private RTHandle _validationBufferRT;

        private ScreenSpaceGlobalIlluminationVariables _giVariables;

        private ShaderVariablesBilateralUpsample _upsampleVariables;

        private RenderTextureDescriptor _targetDescriptor;

        private float _rtWidth;

        private float _rtHeight;

        private float _screenWidth;

        private float _screenHeight;

        private bool _halfResolution;

        private float _historyResolutionScale;

        private readonly GraphicsBuffer _pointDistribution;

        private bool _denoiserInitialized;

        private static readonly ProfilingSampler TracingSampler = new("Trace");

        private static readonly ProfilingSampler ReprojectSampler = new("Reproject");

        private static readonly ProfilingSampler DenoiseSampler = new("Denoise");

        private static readonly ProfilingSampler UpsampleSampler = new("Upsample");

        private bool _needDenoise;

        // Constant buffer structure matching the compute shader
        private struct ScreenSpaceGlobalIlluminationVariables
        {
            public int RayMarchingSteps;
            public float RayMarchingThicknessScale;
            public float RayMarchingThicknessBias;
            public int RayMarchingReflectsSky;

            public int RayMarchingFallbackHierarchy;
            public int IndirectDiffuseFrameIndex;
        }

        public ScreenSpaceGlobalIlluminationPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceGlobalIlluminationPass;
            profilingSampler = new ProfilingSampler("Screen Space Global Illumination");

            _ssgiComputeShader = rendererData.RuntimeResources.screenSpaceGlobalIlluminationCS;
            _traceKernel = _ssgiComputeShader.FindKernel("TraceGlobalIllumination");
            _traceHalfKernel = _ssgiComputeShader.FindKernel("TraceGlobalIlluminationHalf");
            _reprojectKernel = _ssgiComputeShader.FindKernel("ReprojectGlobalIllumination");
            _reprojectHalfKernel = _ssgiComputeShader.FindKernel("ReprojectGlobalIlluminationHalf");

            _diffuseDenoiserCS = rendererData.RuntimeResources.diffuseDenoiserCS;
            _generatePointDistributionKernel = _diffuseDenoiserCS.FindKernel("GeneratePointDistribution");
            _bilateralFilterColorKernel = _diffuseDenoiserCS.FindKernel("BilateralFilterColor");
            _gatherColorKernel = _diffuseDenoiserCS.FindKernel("GatherColor");

            _bilateralUpsampleCS = rendererData.RuntimeResources.bilateralUpsampleCS;
            _bilateralUpsampleKernel = _bilateralUpsampleCS.FindKernel("BilateralUpSampleColor");

            _temporalFilterCS = rendererData.RuntimeResources.temporalFilterCS;
            _validateHistoryKernel = _temporalFilterCS.FindKernel("ValidateHistory");
            _temporalAccumulationColorKernel = _temporalFilterCS.FindKernel("TemporalAccumulationColor");
            _temporalFilterCopyHistoryKernel = _temporalFilterCS.FindKernel("CopyHistory");

            // Initialize point distribution buffer for denoiser (16 samples * 4 frame periods)
            _pointDistribution = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16 * 4, 2 * sizeof(float));
            _denoiserInitialized = false;
#if UNITY_2023_1_OR_NEWER
            ConfigureInput(ScriptableRenderPassInput.Depth
                           | ScriptableRenderPassInput.Normal
                           | ScriptableRenderPassInput.Motion);
#endif
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // Get SSGI volume settings
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();
            if (!volume || !volume.enable.value)
                return;

            _needDenoise = volume.denoise.value;
            _screenWidth = renderingData.cameraData.cameraTargetDescriptor.width;
            _screenHeight = renderingData.cameraData.cameraTargetDescriptor.height;
            _halfResolution = volume.halfResolution.value;

            int resolutionDivider = _halfResolution ? 2 : 1;
            _rtWidth = _screenWidth / resolutionDivider;
            _rtHeight = _screenHeight / resolutionDivider;

            // Allocate hit point texture
            _targetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            _targetDescriptor.msaaSamples = 1;
            _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            _targetDescriptor.depthBufferBits = 0;
            _targetDescriptor.width = Mathf.CeilToInt(_rtWidth);
            _targetDescriptor.height = Mathf.CeilToInt(_rtHeight);
            _targetDescriptor.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref _hitPointRT, _targetDescriptor,
                name: "_IndirectDiffuseHitPointTexture", filterMode: FilterMode.Point);

            // Allocate output texture (low res if half resolution, full res otherwise)
            _targetDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            RenderingUtils.ReAllocateIfNeeded(ref _outputRT, _targetDescriptor,
                name: "_IndirectDiffuseTexture", filterMode: FilterMode.Point);

            // Allocate full resolution upsampled texture if half resolution mode
            if (_halfResolution)
            {
                var fullResDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                fullResDescriptor.msaaSamples = 1;
                fullResDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                fullResDescriptor.depthBufferBits = 0;
                fullResDescriptor.enableRandomWrite = true;
                fullResDescriptor.width = Mathf.CeilToInt(_screenWidth);
                fullResDescriptor.height = Mathf.CeilToInt(_screenHeight);
                RenderingUtils.ReAllocateIfNeeded(ref _upsampledRT, fullResDescriptor,
                    name: "_IndirectDiffuseUpsampled", filterMode: FilterMode.Point);
            }

            // Allocate denoising buffers if enabled
            if (volume.denoise.value)
            {
                // Allocate validation buffer for temporal filter
                var validationDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                validationDescriptor.msaaSamples = 1;
                validationDescriptor.graphicsFormat = GraphicsFormat.R8_UInt;
                validationDescriptor.depthBufferBits = 0;
                validationDescriptor.enableRandomWrite = true;
                validationDescriptor.width = Mathf.CeilToInt(_screenWidth);
                validationDescriptor.height = Mathf.CeilToInt(_screenHeight);
                RenderingUtils.ReAllocateIfNeeded(ref _validationBufferRT, validationDescriptor,
                    name: "_SSGIValidationBuffer", filterMode: FilterMode.Point);

                _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateIfNeeded(ref _temporalRT, _targetDescriptor,
                    name: "_SSGITemporalOutput", filterMode: FilterMode.Point);
                _targetDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                RenderingUtils.ReAllocateIfNeeded(ref _denoisedRT, _targetDescriptor,
                    name: "_SSGIDenoisedOutput", filterMode: FilterMode.Point);

                // Allocate second pass denoising buffers if enabled
                if (volume.secondDenoiserPass.value)
                {
                    _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    RenderingUtils.ReAllocateIfNeeded(ref _temporalRT2, _targetDescriptor,
                        name: "_SSGITemporalOutput2", filterMode: FilterMode.Point);
                    _targetDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                    RenderingUtils.ReAllocateIfNeeded(ref _denoisedRT2, _targetDescriptor,
                        name: "_SSGIDenoisedOutput2", filterMode: FilterMode.Point);
                }

                // Allocate intermediate buffer for half resolution bilateral filter
                if (volume.halfResolutionDenoiser.value)
                {
                    RenderingUtils.ReAllocateIfNeeded(ref _intermediateRT, _targetDescriptor,
                        name: "_DiffuseDenoiserIntermediate", filterMode: FilterMode.Point);
                }

                // Allocate first history buffer
                float scaleFactor = _halfResolution ? 0.5f : 1.0f;
                if (scaleFactor != _historyResolutionScale ||
                    _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination) == null)
                {
                    _rendererData.ReleaseHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination);
                    var historyAllocator = new IllusionRendererData.CustomHistoryAllocator(
                        new Vector2(scaleFactor, scaleFactor),
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "IndirectDiffuseHistoryBuffer");
                    _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination,
                        historyAllocator.Allocator, 1);
                }

                // Allocate second history buffer for second denoiser pass
                if (volume.secondDenoiserPass.value)
                {
                    if (scaleFactor != _historyResolutionScale ||
                        _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2) == null)
                    {
                        _rendererData.ReleaseHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2);
                        var historyAllocator2 = new IllusionRendererData.CustomHistoryAllocator(
                            new Vector2(scaleFactor, scaleFactor),
                            GraphicsFormat.R16G16B16A16_SFloat,
                            "IndirectDiffuseHistoryBuffer2");
                        _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2,
                            historyAllocator2.Allocator, 1);
                    }
                }

                _historyResolutionScale = scaleFactor;
            }

            if (volume.enableProbeVolumes.value && _rendererData.SampleProbeVolumes)
            {
                _ssgiComputeShader.EnableKeyword("_PROBE_VOLUME_ENABLE");
            }
            else
            {
                _ssgiComputeShader.DisableKeyword("_PROBE_VOLUME_ENABLE");
            }
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth
                           | ScriptableRenderPassInput.Normal
                           | ScriptableRenderPassInput.Motion);
        }

        private void PrepareVariables(ref CameraData cameraData)
        {
            var camera = cameraData.camera;
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();

            // Calculate thickness parameters
            float thickness = volume.depthBufferThickness.value;
            float n = camera.nearClipPlane;
            float f = camera.farClipPlane;
            float thicknessScale = 1.0f / (1.0f + thickness);
            float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

            // Ray marching parameters
            _giVariables.RayMarchingSteps = volume.maxRaySteps.value;
            _giVariables.RayMarchingThicknessScale = thicknessScale;
            _giVariables.RayMarchingThicknessBias = thicknessBias;
            _giVariables.RayMarchingReflectsSky = 1;

            // Fallback parameters
            _giVariables.RayMarchingFallbackHierarchy = (int)volume.rayMiss.value;

            // Frame index for temporal sampling
            _giVariables.IndirectDiffuseFrameIndex = (int)(_rendererData.FrameCount % 16);
        }

        private void ExecuteTrace(CommandBuffer cmd, ref CameraData cameraData)
        {
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);

            if (!normalTexture.IsValid())
                return;

            var depthTexture = _rendererData.DepthPyramidRT;
            int kernel = _halfResolution ? _traceHalfKernel : _traceKernel;
            var offsetBuffer = _rendererData.DepthMipChainInfo.GetOffsetBufferData(
                _rendererData.DepthPyramidMipLevelOffsetsBuffer);

            _rendererData.BindDitheredRNGData8SPP(cmd);
            
            // Set constant buffer
            ConstantBuffer.Push(cmd, _giVariables, _ssgiComputeShader, Properties.ShaderVariablesSSGI);

            // Bind input textures
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._DepthPyramid, depthTexture);
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._CameraNormalsTexture, normalTexture);
            cmd.SetComputeBufferParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._DepthPyramidMipLevelOffsets, offsetBuffer);

            // Bind output texture
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                Properties.IndirectDiffuseHitPointTextureRW, _hitPointRT);

            // Dispatch compute shader
            int tileSize = 8;
            int tilesX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, tileSize);
            int tilesY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, tileSize);
            cmd.DispatchCompute(_ssgiComputeShader, kernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);
        }

        private void ExecuteReproject(CommandBuffer cmd, ref CameraData cameraData)
        {
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);
            if (!normalTexture.IsValid())
                return;

            var motionVectorTexture = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);
            var depthTexture = _rendererData.DepthPyramidRT;

            // Get previous frame color pyramid
            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(cameraData, out bool isNewFrame);
            if (preFrameColorRT == null)
                return;

            // Get history depth texture (use current depth as fallback)
            var historyDepthRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Depth);
            if (historyDepthRT == null || !historyDepthRT.IsValid())
            {
                historyDepthRT = depthTexture;
            }

            int kernel = _halfResolution ? _reprojectHalfKernel : _reprojectKernel;
            var offsetBuffer = _rendererData.DepthMipChainInfo.GetOffsetBufferData(
                _rendererData.DepthPyramidMipLevelOffsetsBuffer);

            // Set constant buffer
            ConstantBuffer.Push(cmd, _giVariables, _ssgiComputeShader, Properties.ShaderVariablesSSGI);

            // Bind input textures
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._DepthPyramid, depthTexture);
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._CameraNormalsTexture, normalTexture);
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._MotionVectorTexture,
                isNewFrame && motionVectorTexture.IsValid() ? motionVectorTexture : Texture2D.blackTexture);
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._ColorPyramidTexture, preFrameColorRT);
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                Properties.HistoryDepthTexture, historyDepthRT);
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                Properties.IndirectDiffuseHitPointTexture, _hitPointRT);
            cmd.SetComputeBufferParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._DepthPyramidMipLevelOffsets, offsetBuffer);

            // Exposure texture may not be initialized in the first frame
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._ExposureTexture, _rendererData.GetExposureTexture());
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                IllusionShaderProperties._PrevExposureTexture, _rendererData.GetPreviousExposureTexture());

            // Bind output texture
            cmd.SetComputeTextureParam(_ssgiComputeShader, kernel,
                Properties.IndirectDiffuseTextureRW, _outputRT);

            // Dispatch compute shader
            int tileSize = 8;
            int tilesX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, tileSize);
            int tilesY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, tileSize);
            cmd.DispatchCompute(_ssgiComputeShader, kernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);
        }

        private void InitializeDiffuseDenoiser(CommandBuffer cmd)
        {
            // Generate point distribution (only needs to be done once)
            if (!_denoiserInitialized)
            {
                cmd.SetComputeBufferParam(_diffuseDenoiserCS, _generatePointDistributionKernel,
                    Properties.PointDistributionRW, _pointDistribution);
                cmd.DispatchCompute(_diffuseDenoiserCS, _generatePointDistributionKernel, 1, 1, 1);
                _denoiserInitialized = true;
            }
        }

        private static float GetPixelSpreadTangent(float fov, int width, int height)
        {
            // Calculate the pixel spread angle tangent for the current FOV and resolution
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) / (height * 0.5f);
        }

        // TODO: Move out of SSGI render pass
        private void ExecuteValidateHistory(CommandBuffer cmd, ref CameraData cameraData, float historyValidity)
        {
            var depthTexture = _rendererData.DepthPyramidRT;
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);
            var motionVectorTexture = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);

            if (!depthTexture.IsValid() || !normalTexture.IsValid())
                return;

            // Get history depth and normal textures
            var historyDepthRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Depth);
            var historyNormalRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Normal);

            // If history is not available, clear validation buffer and return
            if (historyDepthRT == null || !historyDepthRT.IsValid() || historyNormalRT == null || !historyNormalRT.IsValid())
            {
                CoreUtils.SetRenderTarget(cmd, _validationBufferRT, clearFlag: ClearFlag.Color, Color.black);
                return;
            }

            // Calculate pixel spread tangent
            float pixelSpreadTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, (int)_screenWidth, (int)_screenHeight);

            // Bind input textures
            cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
                Properties.DepthTexture, depthTexture);
            cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
                Properties.HistoryDepthTexture, historyDepthRT);
            cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
                Properties.NormalBufferTexture, normalTexture);
            cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
                Properties.HistoryNormalTexture, historyNormalRT);
            cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
                IllusionShaderProperties._MotionVectorTexture,
                motionVectorTexture.IsValid() ? motionVectorTexture : Texture2D.blackTexture);

            // cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
            //     Properties.StencilTexture, depthTexture, 0, RenderTextureSubElement.Stencil);
            // cmd.SetComputeIntParam(_temporalFilterCS, Properties.ObjectMotionStencilBit, 0); // Default to 0 if no stencil

            // Bind constants
            cmd.SetComputeFloatParam(_temporalFilterCS, Properties.HistoryValidity, historyValidity);
            cmd.SetComputeFloatParam(_temporalFilterCS, Properties.PixelSpreadAngleTangent, pixelSpreadTangent);
            cmd.SetComputeVectorParam(_temporalFilterCS, Properties.HistorySizeAndScale, _rendererData.EvaluateRayTracingHistorySizeAndScale(historyNormalRT));

            // Bind output buffer
            cmd.SetComputeTextureParam(_temporalFilterCS, _validateHistoryKernel,
                Properties.ValidationBufferRW, _validationBufferRT);

            // Dispatch
            int tileSize = 8;
            int tilesX = IllusionRenderingUtils.DivRoundUp((int)_screenWidth, tileSize);
            int tilesY = IllusionRenderingUtils.DivRoundUp((int)_screenHeight, tileSize);
            cmd.DispatchCompute(_temporalFilterCS, _validateHistoryKernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);
        }

        private void ExecuteTemporalDenoise(CommandBuffer cmd, ref CameraData cameraData, ref ScreenSpaceGlobalIllumination volume,
            RTHandle inputRT, RTHandle outputRT, int historyType)
        {
            var depthTexture = _rendererData.DepthPyramidRT;
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);
            var motionVectorTexture = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);

            if (!depthTexture.IsValid() || !normalTexture.IsValid())
                return;

            // Get history buffer
            var historyRT = _rendererData.GetCurrentFrameRT(historyType);

            // Calculate pixel spread tangent for the denoiser resolution
            float pixelSpreadTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, (int)_rtWidth, (int)_rtHeight);

            // Determine resolution multiplier (1.0 for full res, 0.5 for half res)
            float resolutionMultiplier = _halfResolution ? 0.5f : 1.0f;

            // Bind input textures for temporal accumulation
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                Properties.DenoiseInputTexture, inputRT);
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                Properties.HistoryBuffer, historyRT);
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                Properties.DepthTexture, depthTexture);
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                Properties.ValidationBuffer, _validationBufferRT);
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                Properties.VelocityBuffer, Texture2D.blackTexture); // Not used for SSGI
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                IllusionShaderProperties._MotionVectorTexture,
                motionVectorTexture.IsValid() ? motionVectorTexture : Texture2D.blackTexture);
            
            // Bind exposure textures
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                IllusionShaderProperties._ExposureTexture, _rendererData.GetExposureTexture());
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                IllusionShaderProperties._PrevExposureTexture, _rendererData.GetPreviousExposureTexture());

            // Bind constants
            cmd.SetComputeFloatParam(_temporalFilterCS, Properties.HistoryValidity, 1.0f);
            cmd.SetComputeIntParam(_temporalFilterCS, Properties.ReceiverMotionRejection, 0);
            cmd.SetComputeIntParam(_temporalFilterCS, Properties.OccluderMotionRejection, 0);
            cmd.SetComputeFloatParam(_temporalFilterCS, Properties.PixelSpreadAngleTangent, pixelSpreadTangent);
            cmd.SetComputeVectorParam(_temporalFilterCS, Properties.DenoiserResolutionMultiplierVals,
                new Vector4(resolutionMultiplier, 1.0f / resolutionMultiplier, 1, 1));
            cmd.SetComputeIntParam(_temporalFilterCS, Properties.EnableExposureControl, 1);

            // Bind output buffer
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalAccumulationColorKernel,
                Properties.AccumulationOutputTextureRW, outputRT);

            // Dispatch temporal accumulation
            int tileSize = 8;
            int tilesX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, tileSize);
            int tilesY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, tileSize);
            cmd.DispatchCompute(_temporalFilterCS, _temporalAccumulationColorKernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);

            // Copy the accumulated result to history buffer for next frame
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalFilterCopyHistoryKernel,
                Properties.DenoiseInputTexture, outputRT);
            cmd.SetComputeTextureParam(_temporalFilterCS, _temporalFilterCopyHistoryKernel,
                Properties.DenoiseOutputTextureRW, historyRT);
            cmd.SetComputeVectorParam(_temporalFilterCS, Properties.DenoiserResolutionMultiplierVals,
                new Vector4(resolutionMultiplier, 1.0f / resolutionMultiplier, 1, 1));
            cmd.DispatchCompute(_temporalFilterCS, _temporalFilterCopyHistoryKernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);
        }

        private void ExecuteSpatialDenoise(CommandBuffer cmd, ref CameraData cameraData, RTHandle inputRT, RTHandle outputRT,
            float kernelSize, bool halfResolutionFilter, bool jitterFilter)
        {
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);

            if (!normalTexture.IsValid())
                return;

            // Initialize point distribution buffer if needed
            InitializeDiffuseDenoiser(cmd);
            
            // Determine resolution multiplier (1.0 for full res, 0.5 for half res)
            float resolutionMultiplier = _halfResolution ? 0.5f : 1.0f;

            // Calculate pixel spread tangent for the current resolution
            float pixelSpreadTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, (int)_rtWidth, (int)_rtHeight);

            // Setup bilateral filter parameters
            cmd.SetComputeFloatParam(_diffuseDenoiserCS, Properties.DenoiserFilterRadius, kernelSize);
            cmd.SetComputeFloatParam(_diffuseDenoiserCS, Properties.PixelSpreadAngleTangent, pixelSpreadTangent);
            cmd.SetComputeIntParam(_diffuseDenoiserCS, Properties.HalfResolutionFilter, halfResolutionFilter ? 1 : 0);
            cmd.SetComputeVectorParam(_diffuseDenoiserCS, Properties.DenoiserResolutionMultiplierVals,
                new Vector4(resolutionMultiplier, 1.0f / resolutionMultiplier, 0.0f, 0.0f));
            // Set jitter frame period for temporal variation
            int frameIndex = (int)(_rendererData.FrameCount % 16);
            if (jitterFilter)
                cmd.SetComputeIntParam(_diffuseDenoiserCS, Properties.JitterFramePeriod, frameIndex % 4);
            else
                cmd.SetComputeIntParam(_diffuseDenoiserCS, Properties.JitterFramePeriod, -1);
            

            // Bind resources for bilateral filter
            cmd.SetComputeBufferParam(_diffuseDenoiserCS, _bilateralFilterColorKernel,
                Properties.PointDistribution, _pointDistribution);
            cmd.SetComputeTextureParam(_diffuseDenoiserCS, _bilateralFilterColorKernel,
                Properties.DenoiseInputTexture, inputRT);
            cmd.SetComputeTextureParam(_diffuseDenoiserCS, _bilateralFilterColorKernel,
                Properties.DepthTexture, _rendererData.DepthPyramidRT);
            cmd.SetComputeTextureParam(_diffuseDenoiserCS, _bilateralFilterColorKernel,
                Properties.NormalBufferTexture, normalTexture);

            // Bind output texture
            if (halfResolutionFilter)
            {
                cmd.SetComputeTextureParam(_diffuseDenoiserCS, _bilateralFilterColorKernel,
                    Properties.DenoiseOutputTextureRW, _intermediateRT);
            }
            else
            {
                cmd.SetComputeTextureParam(_diffuseDenoiserCS, _bilateralFilterColorKernel,
                    Properties.DenoiseOutputTextureRW, outputRT);
            }

            // Dispatch bilateral filter
            int tileSize = 8;
            int tilesX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, tileSize);
            int tilesY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, tileSize);
            cmd.DispatchCompute(_diffuseDenoiserCS, _bilateralFilterColorKernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);

            // If using half resolution filter, perform gather pass to upsample
            if (halfResolutionFilter)
            {
                cmd.SetComputeTextureParam(_diffuseDenoiserCS, _gatherColorKernel,
                    Properties.DenoiseInputTexture, _intermediateRT);
                cmd.SetComputeTextureParam(_diffuseDenoiserCS, _gatherColorKernel,
                    Properties.DepthTexture, _rendererData.DepthPyramidRT);
                cmd.SetComputeTextureParam(_diffuseDenoiserCS, _gatherColorKernel,
                    Properties.DenoiseOutputTextureRW, outputRT);
                cmd.DispatchCompute(_diffuseDenoiserCS, _gatherColorKernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);
            }
        }

        private void ExecuteUpsample(CommandBuffer cmd, ref CameraData cameraData, RTHandle lowResInput)
        {
            // Setup upsample constant buffer
            unsafe
            {
                _upsampleVariables._HalfScreenSize = new Vector4(
                    _rtWidth,
                    _rtHeight,
                    1.0f / _rtWidth,
                    1.0f / _rtHeight);

                // Fill distance-based weights (2x2 pattern for half resolution)
                for (int i = 0; i < 16; ++i)
                    _upsampleVariables._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];

                // Fill tap offsets (2x2 pattern for half resolution)
                for (int i = 0; i < 32; ++i)
                    _upsampleVariables._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
            }

            // Set constant buffer
            ConstantBuffer.Push(cmd, _upsampleVariables, _bilateralUpsampleCS, Properties.ShaderVariablesBilateralUpsample);

            // Setup half screen size vector
            Vector4 halfScreenSize = new Vector4(_rtWidth, _rtHeight, 1.0f / _rtWidth, 1.0f / _rtHeight);

            // Bind textures
            cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpsampleKernel,
                Properties.LowResolutionTexture, lowResInput);
            cmd.SetComputeVectorParam(_bilateralUpsampleCS, Properties.HalfScreenSize, halfScreenSize);
            cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpsampleKernel,
                Properties.OutputUpscaledTexture, _upsampledRT);

            // Dispatch (full resolution tiles)
            int tileSize = 8;
            int tilesX = IllusionRenderingUtils.DivRoundUp((int)_screenWidth, tileSize);
            int tilesY = IllusionRenderingUtils.DivRoundUp((int)_screenHeight, tileSize);
            cmd.DispatchCompute(_bilateralUpsampleCS, _bilateralUpsampleKernel, tilesX, tilesY, IllusionRendererData.MaxViewCount);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!_rendererData.SampleScreenSpaceIndirectDiffuse)
            {
                return;
            }

            ref var cameraData = ref renderingData.cameraData;
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();
            if (!volume || !volume.enable.value)
                return;

            // Need previous frame color pyramid
            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(cameraData, out _);
            if (preFrameColorRT == null)
                return;

            // Prepare shader variables
            PrepareVariables(ref cameraData);

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                using (new ProfilingScope(cmd, TracingSampler))
                {
                    ExecuteTrace(cmd, ref cameraData);
                }

                using (new ProfilingScope(cmd, ReprojectSampler))
                {
                    ExecuteReproject(cmd, ref cameraData);
                }

                RTHandle finalRT = _outputRT;
                if (_needDenoise)
                {
                    using (new ProfilingScope(cmd, DenoiseSampler))
                    {
                        // Validate history before temporal denoising
                        ExecuteValidateHistory(cmd, ref cameraData, 1.0f);

                        ExecuteTemporalDenoise(cmd, ref cameraData, ref volume,
                            finalRT, _temporalRT,
                            (int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination);

                        bool halfResFilter = volume.halfResolutionDenoiser.value;
                        ExecuteSpatialDenoise(cmd, ref cameraData, _temporalRT, _denoisedRT,
                            volume.denoiserRadius.value, halfResFilter, volume.secondDenoiserPass.value);

                        finalRT = _denoisedRT;

                        // Second pass: Apply second denoiser pass if enabled
                        if (volume.secondDenoiserPass.value)
                        {
                            ExecuteTemporalDenoise(cmd, ref cameraData, ref volume,
                                finalRT, _temporalRT2,
                                (int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2);

                            // Spatial denoising with smaller kernel (0.5x)
                            ExecuteSpatialDenoise(cmd, ref cameraData, _temporalRT2, _denoisedRT2,
                                volume.denoiserRadius.value * 0.5f, halfResFilter, false);

                            finalRT = _denoisedRT2;
                        }
                    }
                }

                // Bilateral upsampling from half resolution to full resolution
                if (_halfResolution)
                {
                    using (new ProfilingScope(cmd, UpsampleSampler))
                    {
                        ExecuteUpsample(cmd, ref cameraData, finalRT);
                        finalRT = _upsampledRT;
                    }
                }

                cmd.SetGlobalTexture(Properties.IndirectDiffuseTexture, finalRT);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _hitPointRT?.Release();
            _outputRT?.Release();
            _denoisedRT?.Release();
            _temporalRT?.Release();
            _temporalRT2?.Release();
            _denoisedRT2?.Release();
            _upsampledRT?.Release();
            _intermediateRT?.Release();
            _validationBufferRT?.Release();
            _pointDistribution?.Release();
        }

        private static class Properties
        {
            public static readonly int ShaderVariablesSSGI = Shader.PropertyToID("UnityScreenSpaceGlobalIllumination");

            public static readonly int IndirectDiffuseHitPointTextureRW = Shader.PropertyToID("_IndirectDiffuseHitPointTextureRW");

            public static readonly int IndirectDiffuseHitPointTexture = Shader.PropertyToID("_IndirectDiffuseHitPointTexture");

            public static readonly int IndirectDiffuseTextureRW = Shader.PropertyToID("_IndirectDiffuseTextureRW");

            public static readonly int IndirectDiffuseTexture = Shader.PropertyToID("_IndirectDiffuseTexture");

            public static readonly int HistoryDepthTexture = Shader.PropertyToID("_HistoryDepthTexture");

            // Upsample shader properties
            public static readonly int ShaderVariablesBilateralUpsample = Shader.PropertyToID("ShaderVariablesBilateralUpsample");

            public static readonly int LowResolutionTexture = Shader.PropertyToID("_LowResolutionTexture");

            public static readonly int HalfScreenSize = Shader.PropertyToID("_HalfScreenSize");

            public static readonly int OutputUpscaledTexture = Shader.PropertyToID("_OutputUpscaledTexture");

            // Bilateral denoiser shader properties
            public static readonly int PointDistributionRW = Shader.PropertyToID("_PointDistributionRW");

            public static readonly int PointDistribution = Shader.PropertyToID("_PointDistribution");

            public static readonly int DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");

            public static readonly int DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");

            public static readonly int DenoiserFilterRadius = Shader.PropertyToID("_DenoiserFilterRadius");

            public static readonly int PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");

            public static readonly int HalfResolutionFilter = Shader.PropertyToID("_HalfResolutionFilter");

            public static readonly int JitterFramePeriod = Shader.PropertyToID("_JitterFramePeriod");

            public static readonly int DepthTexture = Shader.PropertyToID("_DepthTexture");

            public static readonly int NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");

            // Temporal filter shader properties
            public static readonly int ValidationBufferRW = Shader.PropertyToID("_ValidationBufferRW");

            public static readonly int ValidationBuffer = Shader.PropertyToID("_ValidationBuffer");

            public static readonly int HistoryBuffer = Shader.PropertyToID("_HistoryBuffer");

            public static readonly int VelocityBuffer = Shader.PropertyToID("_VelocityBuffer");

            public static readonly int HistoryValidity = Shader.PropertyToID("_HistoryValidity");

            public static readonly int ReceiverMotionRejection = Shader.PropertyToID("_ReceiverMotionRejection");

            public static readonly int OccluderMotionRejection = Shader.PropertyToID("_OccluderMotionRejection");

            public static readonly int DenoiserResolutionMultiplierVals = Shader.PropertyToID("_DenoiserResolutionMultiplierVals");

            public static readonly int EnableExposureControl = Shader.PropertyToID("_EnableExposureControl");

            public static readonly int AccumulationOutputTextureRW = Shader.PropertyToID("_AccumulationOutputTextureRW");

            public static readonly int HistoryNormalTexture = Shader.PropertyToID("_HistoryNormalTexture");

            // public static readonly int ObjectMotionStencilBit = Shader.PropertyToID("_ObjectMotionStencilBit");

            public static readonly int HistorySizeAndScale = Shader.PropertyToID("_HistorySizeAndScale");

            // public static readonly int StencilTexture = Shader.PropertyToID("_StencilTexture");
        }
    }
}