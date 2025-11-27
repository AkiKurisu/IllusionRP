using System;
using System.Collections.Generic;
using Illusion.Rendering.PostProcessing;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public enum ExposureDebugMode
    {
        /// <summary>
        /// No exposure debug.
        /// </summary>
        None,

        /// <summary>
        /// Display the EV100 values of the scene, color-coded.
        /// </summary>
        SceneEV100Values,

        /// <summary>
        /// Display the Histogram used for exposure.
        /// </summary>
        HistogramView,

        /// <summary>
        /// Display an RGB histogram of the final image (after post-processing).
        /// </summary>
        FinalImageHistogramView,

        /// <summary>
        /// Visualize the scene color weighted as the metering mode selected.
        /// </summary>
        MeteringWeighted,
    }

    public enum ScreenSpaceShadowDebugMode
    {
        /// <summary>
        /// Display by renderer settings
        /// </summary>
        None,
        
        /// <summary>
        /// Only display main light shadow
        /// </summary>
        MainLightShadow,
        
        /// <summary>
        /// Only display contact shadow
        /// </summary>
        ContactShadow
    }

    public enum IndirectDiffuseMode
    {
        Off,
        ScreenSpace,
        RayTraced,
        Mixed
    }
    
    /// <summary>
    /// IllusionRP renderer shared data
    /// </summary>
    public partial class IllusionRendererData : IDisposable
    {
        internal readonly struct CustomHistoryAllocator
        {
            private readonly Vector2 _scaleFactor;

            private readonly GraphicsFormat _format;

            private readonly string _name;

            public CustomHistoryAllocator(Vector2 scaleFactor, GraphicsFormat format, string name)
            {
                _scaleFactor = scaleFactor;
                _format = format;
                _name = name;
            }

            public RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                return rtHandleSystem.Alloc(Vector2.one * _scaleFactor,
                    // TextureXR.slices, 
                    filterMode: FilterMode.Point,
                    colorFormat: _format,
                    // dimension: TextureXR.dimension, 
                    // useDynamicScale: true, 
                    enableRandomWrite: true,
                    name: $"{id}_{_name}_{frameIndex}");
            }
        }

        public readonly IllusionRenderPipelineResources RuntimeResources;

        public readonly PreIntegratedFGD PreIntegratedFGD;

        public PackedMipChainInfo DepthMipChainInfo;

        public Vector2Int DepthMipChainSize => DepthMipChainInfo.textureSize;

        public int ColorPyramidHistoryMipCount { get; internal set; }

        public readonly GPUCopy GPUCopy;

        public readonly ComputeBuffer DepthPyramidMipLevelOffsetsBuffer;

        // Not support VR yet
        public const int MaxViewCount = 1;

        /// <summary>
        /// Depth texture after transparent depth normal but before transparent depth only.
        /// </summary>
        public RTHandle CameraPreDepthTextureRT;

        public RTHandle ContactShadowsRT;

        public RTHandle ContactShadowsDenoisedRT;

        public RTHandle ScreenSpaceShadowsRT;

        /// <summary>
        /// Color texture before post-processing of previous frame
        /// </summary>
        public RTHandle CameraPreviousColorTextureRT;

        /// <summary>
        /// Forward rendering path thin gbuffer
        /// </summary>
        public RTHandle ForwardGBufferRT;

        /// <summary>
        /// Depth pyramid of current frame
        /// </summary>
        public RTHandle DepthPyramidRT;

        /// <summary>
        /// Get renderer camera <see cref="UniversalAdditionalCameraData"/>.
        /// </summary>
        public UniversalAdditionalCameraData AdditionalCameraData => _additionalCameraData;

        /// <summary>
        /// Get per-renderer frame count.
        /// </summary>
        public uint FrameCount { get; private set; }

        /// <summary>
        /// Renderer requires a history color buffer.
        /// </summary>
        public bool RequireHistoryColor { get; internal set; }

        /// <summary>
        /// Prefer using Compute Shader in render passes.
        /// </summary>
        public bool PreferComputeShader { get; internal set; }

        /// <summary>
        /// Enable native render pass if possible.
        /// </summary>
        public bool NativeRenderPass { get; internal set; } = true;

        /// <summary>
        /// Get whether the renderer can sample probe volumes (PRTGI).
        /// </summary>
        public bool SampleProbeVolumes { get; internal set; } = true;
        
        /// <summary>
        /// Get whether the renderer can sample screen space indirect diffuse texture.
        /// </summary>
        public bool SampleScreenSpaceIndirectDiffuse { get; internal set; } = true;
        
        /// <summary>
        /// Whether the renderer should copy depth and normal texture for next frame usage.
        /// </summary>
        public bool RequireHistoryDepthNormal { get; internal set; }
        
        /// <summary>
        /// Whether the renderer use main light rendering layers to control indirect diffuse intensity.
        /// </summary>
        public bool EnableIndirectDiffuseRenderingLayers { get; internal set; }

        public bool IsFirstFrame { get; private set; } = true;

        public bool ResetPostProcessingHistory { get; internal set; } = true;

        public bool DidResetPostProcessingHistoryInLastFrame { get; internal set; }

        /// <summary>
        /// Returns true if lighting is active for current state of debug settings.
        /// </summary>
        public bool IsLightingActive { get; private set; }
        
        public bool ContactShadowsSampling { get; internal set; }
        
        public bool PCSSShadowSampling { get; internal set; }

        public uint PerObjectShadowRenderingLayer { get; internal set; }

        public MipGenerator MipGenerator { get; }

        public const int ShadowCascadeCount = 4;

        public readonly Matrix4x4[] MainLightShadowDeviceProjectionMatrixs = new Matrix4x4[ShadowCascadeCount];

        public readonly Vector4[] MainLightShadowDeviceProjectionVectors = new Vector4[ShadowCascadeCount];
        
        public readonly Vector4[] MainLightShadowCascadeBiases = new Vector4[ShadowCascadeCount];

        public ShadowSliceData[] MainLightShadowSliceData { get; private set; }
        
        public RTHandle DebugExposureTexture;

        public ComputeBuffer DebugImageHistogram;

        public ComputeBuffer HistogramBuffer;

        public static IllusionRendererData Active { get; private set; }

        private UniversalAdditionalCameraData _additionalCameraData;

        private readonly BufferedRTHandleSystem _historyRTSystem = new();

        private Camera _camera;

        private readonly Dictionary<IllusionGraphicsFenceEvent, GraphicsFence> _graphicsFences = new();

        private Exposure _exposure;

        private readonly RTHandle _emptyExposureTexture; // RGHalf

        private readonly RTHandle _debugExposureData;

        private int _taaFrameIndex;

        private UniversalAdditionalLightData _mainLightData;

        private const GraphicsFormat ExposureFormat = GraphicsFormat.R32G32_SFloat;

        private ComputeBuffer _ambientProbeBuffer;

        private struct ShaderVariablesGlobal
        {
            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ViewProjMatrix;
            public Matrix4x4 InvViewProjMatrix;
            public Matrix4x4 PrevInvViewProjMatrix;

            public Vector4 RTHandleScale;
            public Vector4 RTHandleScaleHistory;

            // TAA Frame Index ranges from 0 to 7.
            public Vector4 TaaFrameInfo;  // { unused, frameCount, taaFrameIndex, taaEnabled ? 1 : 0 }

            public Vector4 ColorPyramidUvScaleAndLimitPrevFrame;

            public int IndirectDiffuseMode;
            public float IndirectDiffuseLightingMultiplier;
            public uint IndirectDiffuseLightingLayers;
        }

        private ShaderVariablesGlobal _shaderVariablesGlobal;

        public IllusionRendererData(IllusionRenderPipelineResources renderPipelineResources)
        {
            RuntimeResources = renderPipelineResources;
            GPUCopy = new GPUCopy(renderPipelineResources.copyChannelCS);
            DepthMipChainInfo = new PackedMipChainInfo();
            DepthMipChainInfo.Allocate();
            PreIntegratedFGD = new PreIntegratedFGD(renderPipelineResources);
            DepthPyramidMipLevelOffsetsBuffer = new ComputeBuffer(15, sizeof(int) * 2);
            MipGenerator = new MipGenerator(this);
            // Setup a default exposure textures and clear it to neutral values so that the exposure
            // multiplier is 1 and thus has no effect
            // Beware that 0 in EV100 maps to a multiplier of 0.833 so the EV100 value in this
            // neutral exposure texture isn't 0
            _emptyExposureTexture = RTHandles.Alloc(1, 1, colorFormat: ExposureFormat,
                enableRandomWrite: true, name: "Empty EV100 Exposure");

            _debugExposureData = RTHandles.Alloc(1, 1, colorFormat: ExposureFormat,
                enableRandomWrite: true, name: "Debug Exposure Info");
        }

        public void Update(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            Active = this;
            // Cleanup previous graphics fence
            _graphicsFences.Clear();
            // Advance render frame
            FrameCount++;
            IsFirstFrame = false;
            UpdateCameraData(renderingData);
            UpdateLightData(renderingData);
            UpdateShadowData(renderingData);
            UpdateRenderTextures(renderingData);
            UpdateDebugSettings(renderingData);
            UpdateVolumeParameters();
        }

        public void Dispose()
        {
            if (Active == this)
            {
                Active = null;
            }

            _graphicsFences.Clear();
            MipGenerator.Release();
            _historyRTSystem.Dispose();
            CameraPreDepthTextureRT?.Release();
            CameraPreviousColorTextureRT?.Release();
            DepthPyramidMipLevelOffsetsBuffer?.Release();
            ScreenSpaceShadowsRT?.Release();
            ContactShadowsRT?.Release();
            ContactShadowsDenoisedRT?.Release();
            ForwardGBufferRT?.Release();
            DebugExposureTexture?.Release();
            DepthPyramidRT?.Release();
            CoreUtils.SafeRelease(HistogramBuffer);
            HistogramBuffer = null;
            CoreUtils.SafeRelease(_ambientProbeBuffer);
            _ambientProbeBuffer = null;
            CoreUtils.SafeRelease(DebugImageHistogram);
            DebugImageHistogram = null;
            RTHandles.Release(_emptyExposureTexture);
        }

        /// <summary>
        /// Push global constant buffers to gpu
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        internal void PushGlobalBuffers(CommandBuffer cmd, ref RenderingData renderingData)
        {
            PushShadowData(cmd);
            PushGlobalVariables(cmd, ref renderingData);
        }

        private void PushGlobalVariables(CommandBuffer cmd, ref RenderingData renderingData)
        {
            bool useTAA = renderingData.cameraData.IsTemporalAAEnabled(); // Disable in scene view

            // Match HDRP View Projection Matrix, pre-handle reverse z.
            _shaderVariablesGlobal.ViewMatrix = renderingData.cameraData.camera.worldToCameraMatrix;
            _shaderVariablesGlobal.ViewProjMatrix = IllusionRenderingUtils.CalculateViewProjMatrix(ref renderingData.cameraData);
            var lastInvViewProjMatrix = _shaderVariablesGlobal.InvViewProjMatrix;
            _shaderVariablesGlobal.InvViewProjMatrix = _shaderVariablesGlobal.ViewProjMatrix.inverse;
            _shaderVariablesGlobal.PrevInvViewProjMatrix = FrameCount > 1 ? _shaderVariablesGlobal.InvViewProjMatrix : lastInvViewProjMatrix;

            // No RTHandleScale in IllusionRP
            // _shaderVariablesGlobal.RTHandleScale = RTHandles.rtHandleProperties.rtHandleScale;
            // _shaderVariablesGlobal.RTHandleScaleHistory = _historyRTSystem.rtHandleProperties.rtHandleScale;
            _shaderVariablesGlobal.RTHandleScale = Vector4.one;
            _shaderVariablesGlobal.RTHandleScaleHistory = Vector4.one;

            const int kMaxSampleCount = 8;
            if (++_taaFrameIndex >= kMaxSampleCount)
                _taaFrameIndex = 0;
            _shaderVariablesGlobal.TaaFrameInfo = new Vector4(0, _taaFrameIndex, FrameCount, useTAA ? 1 : 0);
            _shaderVariablesGlobal.ColorPyramidUvScaleAndLimitPrevFrame
                = IllusionRenderingUtils.ComputeViewportScaleAndLimit(_historyRTSystem.rtHandleProperties.previousViewportSize,
                _historyRTSystem.rtHandleProperties.previousRenderTargetSize);
            
            GetMainLightIndirectIntensityAndRenderingLayers(ref renderingData, out float intensity, out uint layers);
            if (!EnableIndirectDiffuseRenderingLayers)
            {
                layers = ~(uint)0;
            }
            _shaderVariablesGlobal.IndirectDiffuseMode = (int)GetIndirectDiffuseMode();
            _shaderVariablesGlobal.IndirectDiffuseLightingMultiplier = intensity;
            _shaderVariablesGlobal.IndirectDiffuseLightingLayers = layers;
            ConstantBuffer.PushGlobal(cmd, _shaderVariablesGlobal, IllusionShaderProperties.ShaderVariablesGlobal);
        }
        
        private void PushShadowData(CommandBuffer cmd)
        {
            cmd.SetGlobalVectorArray(IllusionShaderProperties._MainLightShadowCascadeBiases, MainLightShadowCascadeBiases);
        }
        
        private void GetMainLightIndirectIntensityAndRenderingLayers(ref RenderingData renderingData,
            out float intensity, out uint renderingLayers)
        {
            intensity = 1.0f;
            renderingLayers = 0;
            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (mainLightIndex < 0) return; // No main light
            VisibleLight mainLight = renderingData.lightData.visibleLights[mainLightIndex];
            intensity = mainLight.light.bounceIntensity;
            renderingLayers = _mainLightData?.renderingLayers ?? 0;
        }

        private void UpdateDebugSettings(in RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            if (renderer.DebugHandler != null && !renderingData.cameraData.isPreviewCamera)
            {
                IsLightingActive = renderer.DebugHandler.IsLightingActive;
            }
            else
            {
                IsLightingActive = true;
            }
        }

        private void UpdateShadowData(RenderingData renderingData)
        {
            var mainLightShadowCasterPass = UniversalRenderingUtility.GetMainLightShadowCasterPass(renderingData.cameraData.renderer);
            MainLightShadowSliceData = UniversalRenderingUtility.GetMainLightShadowSliceData(mainLightShadowCasterPass);
            
            // deviceProjection will potentially inverse-Z
            for (int i = 0; i < MainLightShadowSliceData.Length && i < ShadowCascadeCount; ++i)
            {
                MainLightShadowDeviceProjectionMatrixs[i] = GL.GetGPUProjectionMatrix(MainLightShadowSliceData[i].projectionMatrix, false);
                MainLightShadowDeviceProjectionVectors[i] = new Vector4(MainLightShadowDeviceProjectionMatrixs[i].m00, MainLightShadowDeviceProjectionMatrixs[i].m11,
                    MainLightShadowDeviceProjectionMatrixs[i].m22, MainLightShadowDeviceProjectionMatrixs[i].m23);
            }
            
            int shadowLightIndex = renderingData.lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return;

            VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];
            for (int i = 0; i < MainLightShadowSliceData.Length && i < ShadowCascadeCount; ++i)
            {
                MainLightShadowCascadeBiases[i] = ShadowUtils.GetShadowBias(ref shadowLight, shadowLightIndex, ref renderingData.shadowData,
                    MainLightShadowSliceData[i].projectionMatrix, MainLightShadowSliceData[i].resolution);
            }
        }

        private void UpdateRenderTextures(RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            var viewportSize = new Vector2Int(descriptor.width, descriptor.height);
            _historyRTSystem.SwapAndSetReferenceSize(descriptor.width, descriptor.height);

            // Since we do not use RTHandleScale, ensure render texture size correct
            if (_historyRTSystem.rtHandleProperties.currentRenderTargetSize.x > descriptor.width
                || _historyRTSystem.rtHandleProperties.currentRenderTargetSize.y > descriptor.height)
            {
                _historyRTSystem.ResetReferenceSize(descriptor.width, descriptor.height);
                _exposureTextures.Clear();
            }

            DepthMipChainInfo.ComputePackedMipChainInfo(viewportSize, 0);
            
            SetupExposureTextures();
        }

        private void UpdateVolumeParameters()
        {
            _exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
            // Update info about current target mid gray
            TargetMidGray requestedMidGray = _exposure.targetMidGray.value;
            switch (requestedMidGray)
            {
                case TargetMidGray.Grey125:
                    ColorUtils.s_LightMeterCalibrationConstant = 12.5f;
                    break;
                case TargetMidGray.Grey14:
                    ColorUtils.s_LightMeterCalibrationConstant = 14.0f;
                    break;
                case TargetMidGray.Grey18:
                    ColorUtils.s_LightMeterCalibrationConstant = 18.0f;
                    break;
                default:
                    ColorUtils.s_LightMeterCalibrationConstant = 12.5f;
                    break;
            }
        }

        private void UpdateCameraData(in RenderingData renderingData)
        {
            _camera = renderingData.cameraData.camera;
            _camera.TryGetComponent(out _additionalCameraData);
        }

        private void UpdateLightData(in RenderingData renderingData)
        {
            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (mainLightIndex < 0) return; // No main light

            VisibleLight mainLight = renderingData.lightData.visibleLights[mainLightIndex];
            if (_mainLightData == null || _mainLightData.gameObject != mainLight.light.gameObject)
            {
                if (!mainLight.light) return;
                // Prevent main light overdraw shadow.
                if (!mainLight.light.TryGetComponent(out _mainLightData)) return;
                if (_mainLightData.customShadowLayers)
                {
                    _mainLightData.shadowRenderingLayers &= ~PerObjectShadowRenderingLayer;
                }
                else
                {
                    _mainLightData.customShadowLayers = true;
                    _mainLightData.shadowRenderingLayers = uint.MaxValue & ~PerObjectShadowRenderingLayer;
                }
            }
        }

        /// <summary>
        /// Copy history depth and normal buffers for next frame usage.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        public void CopyHistoryGraphicsBuffers(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;

            // Get current depth pyramid and normal texture
            var currentDepth = DepthPyramidRT;
            var currentNormal = UniversalRenderingUtility.GetNormalTexture(renderingData.cameraData.renderer);

            if (currentDepth == null || !currentDepth.IsValid())
                return;

            // Allocate and copy depth history buffer
            var depthHistoryRT = GetCurrentFrameRT((int)IllusionFrameHistoryType.Depth);
            if (depthHistoryRT == null)
            {
                RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                {
                    return rtHandleSystem.Alloc(Vector2.one, filterMode: FilterMode.Point,
                        colorFormat: currentDepth.rt.graphicsFormat,
                        enableRandomWrite: currentDepth.rt.enableRandomWrite,
                        name: $"{id}_Depth_History_Buffer_{frameIndex}");
                }

                depthHistoryRT = AllocHistoryFrameRT((int)IllusionFrameHistoryType.Depth, Allocator, 1);
            }

            // Copy current depth to history
            if (depthHistoryRT != null && depthHistoryRT.IsValid())
            {
                for (int i = 0; i < MaxViewCount; i++)
                {
                    cmd.CopyTexture(currentDepth, i, 0, 0, 0, descriptor.width, descriptor.height,
                        depthHistoryRT, i, 0, 0, 0);
                }
            }

            // Allocate and copy normal history buffer
            if (currentNormal.IsValid())
            {
                var normalHistoryRT = GetCurrentFrameRT((int)IllusionFrameHistoryType.Normal);
                if (normalHistoryRT == null)
                {
                    RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                    {
                        return rtHandleSystem.Alloc(Vector2.one, filterMode: FilterMode.Point,
                            colorFormat: currentNormal.rt.graphicsFormat,
                            enableRandomWrite: currentNormal.rt.enableRandomWrite,
                            name: $"{id}_Normal_History_Buffer_{frameIndex}");
                    }

                    normalHistoryRT = AllocHistoryFrameRT((int)IllusionFrameHistoryType.Normal, Allocator, 1);
                }

                // Copy current normal to history
                if (normalHistoryRT != null && normalHistoryRT.IsValid())
                {
                    for (int i = 0; i < MaxViewCount; i++)
                    {
                        cmd.CopyTexture(currentNormal, i, 0, 0, 0, descriptor.width, descriptor.height,
                            normalHistoryRT, i, 0, 0, 0);
                    }
                }
            }
        }

        /// <summary>
        /// Bind global textures
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        internal void BindGlobalTextures(CommandBuffer cmd, ref RenderingData renderingData)
        {
            BindDitheredRNGData1SPP(cmd);
            BindHistoryColor(cmd, renderingData);
            BindAmbientProbe(cmd);
        }

        private void BindAmbientProbe(CommandBuffer cmd)
        {
            SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;
            _ambientProbeBuffer ??= new ComputeBuffer(7, 16);
            var array = new NativeArray<Vector4>(7, Allocator.Temp);
            array[0] = new Vector4(ambientProbe[0, 3], ambientProbe[0, 1], ambientProbe[0, 2], ambientProbe[0, 0] - ambientProbe[0, 6]);
            array[1] = new Vector4(ambientProbe[1, 3], ambientProbe[1, 1], ambientProbe[1, 2], ambientProbe[1, 0] - ambientProbe[1, 6]);
            array[2] = new Vector4(ambientProbe[2, 3], ambientProbe[2, 1], ambientProbe[2, 2], ambientProbe[2, 0] - ambientProbe[2, 6]);
            array[3] = new Vector4(ambientProbe[0, 4], ambientProbe[0, 5], ambientProbe[0, 6] * 3, ambientProbe[0, 7]);
            array[4] = new Vector4(ambientProbe[1, 4], ambientProbe[1, 5], ambientProbe[1, 6] * 3, ambientProbe[1, 7]);
            array[5] = new Vector4(ambientProbe[2, 4], ambientProbe[2, 5], ambientProbe[2, 6] * 3, ambientProbe[2, 7]);
            array[6] = new Vector4(ambientProbe[0, 8], ambientProbe[1, 8], ambientProbe[2, 8], 1);
            _ambientProbeBuffer.SetData(array);
            array.Dispose();
            cmd.SetGlobalBuffer(IllusionShaderProperties._AmbientProbeData, _ambientProbeBuffer);
        }

        private void BindHistoryColor(CommandBuffer cmd, in RenderingData renderingData)
        {
            var historyColorRT = GetPreviousFrameColorRT(renderingData.cameraData, out var isNewFrame);
            if (historyColorRT.IsValid())
            {
                var motionVectorColorRT = UniversalRenderingUtility.GetMotionVectorColor(renderingData.cameraData.renderer);
                isNewFrame &= motionVectorColorRT.IsValid();
                cmd.SetGlobalTexture(IllusionShaderProperties._HistoryColorTexture, historyColorRT);
                cmd.SetGlobalTexture(IllusionShaderProperties._MotionVectorTexture, isNewFrame ? motionVectorColorRT : Texture2D.blackTexture);
            }
        }

        private void BindDitheredRNGData1SPP(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(IllusionShaderProperties._OwenScrambledTexture, RuntimeResources.owenScrambled256Tex);
            cmd.SetGlobalTexture(IllusionShaderProperties._ScramblingTileXSPP, RuntimeResources.scramblingTile1SPP);
            cmd.SetGlobalTexture(IllusionShaderProperties._RankingTileXSPP, RuntimeResources.rankingTile1SPP);
            cmd.SetGlobalTexture(IllusionShaderProperties._ScramblingTexture, RuntimeResources.scramblingTex);
        }

        public Vector4 EvaluateRayTracingHistorySizeAndScale(RTHandle buffer)
        {
            return new Vector4(_historyRTSystem.rtHandleProperties.previousViewportSize.x,
                _historyRTSystem.rtHandleProperties.previousViewportSize.y,
                (float)_historyRTSystem.rtHandleProperties.previousViewportSize.x / buffer.rt.width,
                (float)_historyRTSystem.rtHandleProperties.previousViewportSize.y / buffer.rt.height);
        }

        /// <summary>
        /// Allocates a history RTHandle with the unique identifier id.
        /// </summary>
        /// <param name="id">Unique id for this history buffer.</param>
        /// <param name="allocator">Allocator function for the history RTHandle.</param>
        /// <param name="bufferCount">Number of buffer that should be allocated.</param>
        /// <returns>A new RTHandle.</returns>
        public RTHandle AllocHistoryFrameRT(int id, Func<string, int, RTHandleSystem, RTHandle> allocator, int bufferCount)
        {
            _historyRTSystem.AllocBuffer(id, (rts, i) => allocator(_camera.name, i, rts), bufferCount);
            return _historyRTSystem.GetFrameRT(id, 0);
        }

        /// <summary>
        /// Returns the id RTHandle from the previous frame.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The RTHandle from previous frame.</returns>
        public RTHandle GetPreviousFrameRT(int id)
        {
            return _historyRTSystem.GetFrameRT(id, 1);
        }

        /// <summary>
        /// Returns the id RTHandle of the current frame.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The RTHandle of the current frame.</returns>
        public RTHandle GetCurrentFrameRT(int id)
        {
            return _historyRTSystem.GetFrameRT(id, 0);
        }

        /// <summary>
        /// Release a buffer.
        /// </summary>
        /// <param name="id"></param>
        internal void ReleaseHistoryFrameRT(int id)
        {
            _historyRTSystem.ReleaseBuffer(id);
        }

        /// <summary>
        /// Creates a GraphicsFence.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="syncFenceEvent"></param>
        /// <returns></returns>
        public GraphicsFence CreateAsyncGraphicsFence(CommandBuffer cmd, IllusionGraphicsFenceEvent syncFenceEvent)
        {
            var fence = cmd.CreateAsyncGraphicsFence();
            _graphicsFences[syncFenceEvent] = fence;
            return fence;
        }

        /// <summary>
        /// Instructs the GPU to pause processing of the queue until it passes through the GraphicsFence fence.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="syncFenceEvent"></param>
        public void WaitOnAsyncGraphicsFence(CommandBuffer cmd, IllusionGraphicsFenceEvent syncFenceEvent)
        {
            if (_graphicsFences.TryGetValue(syncFenceEvent, out var fence))
            {
                cmd.WaitOnAsyncGraphicsFence(fence);
            }
        }

        /// <summary>
        /// Get previous frame color buffer if possible
        /// </summary>
        /// <param name="cameraData"></param>
        /// <param name="isNewFrame"></param>
        /// <returns></returns>
        public RTHandle GetPreviousFrameColorRT(CameraData cameraData, out bool isNewFrame)
        {
            // Using color pyramid
            if (cameraData.cameraType == CameraType.Game)
            {
                var previewsColorRT = GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain);
                if (previewsColorRT != null)
                {
                    isNewFrame = true;
                    return previewsColorRT;
                }
            }

            // Using taa accumulation buffer
            if (cameraData.IsTemporalAAEnabled())
            {
                int multipassId = 0;
#if ENABLE_VR && ENABLE_XR_MODULE
                multipassId = cameraData.xr.multipassId;
#endif
                var taaPersistentData = AdditionalCameraData.taaPersistentData;
                isNewFrame = taaPersistentData.GetLastAccumFrameIndex(multipassId) != Time.frameCount;
                return taaPersistentData.accumulationTexture(multipassId);
            }

            // Using history color
            isNewFrame = true;
            if (RequireHistoryColor)
            {
                return CameraPreviousColorTextureRT;
            }

            // Fallback to opaque texture if exist.
            return UniversalRenderingUtility.GetOpaqueTexture(cameraData.renderer);
        }

        private IndirectDiffuseMode GetIndirectDiffuseMode()
        {
            if (SampleScreenSpaceIndirectDiffuse)
            {
                return IndirectDiffuseMode.ScreenSpace;
            }

            // Raytracing not implement yet.
            return IndirectDiffuseMode.Off;
        }
    }
}