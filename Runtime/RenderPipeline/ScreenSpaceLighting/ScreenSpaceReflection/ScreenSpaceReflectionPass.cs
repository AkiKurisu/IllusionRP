using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class ScreenSpaceReflectionPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _material = new(IllusionShaders.ScreenSpaceReflection);

        private RTHandle _ssrHitPointRT;

        private RTHandle _ssrLightingRT;

        private float _rtHeight;

        private float _rtWidth;

        private float _screenHeight;

        private float _screenWidth;

        private static readonly MaterialPropertyBlock SharedPropertyBlock = new();

        private readonly IllusionRendererData _rendererData;

        private readonly ComputeShader _computeShader;

        private readonly int _tracingKernel;

        private readonly int _reprojectionKernel;

        private readonly ComputeShader _clearBuffer2DCS;

        private readonly int _clearBuffer2DKernel;

        private readonly int _accumulateNoWorldSpeedRejectionBothKernel;

        private readonly int _accumulateSmoothSpeedRejectionBothKernel;
        
        private readonly int _accumulateNoWorldSpeedRejectionBothDebugKernel;

        private readonly int _accumulateSmoothSpeedRejectionBothDebugKernel;

        private readonly ProfilingSampler _tracingSampler = new("Tracing");

        private readonly ProfilingSampler _reprojectionSampler = new("Reprojection");
        
        private readonly ProfilingSampler _accumulationSampler = new("Accumulation");

        private ScreenSpaceReflectionVariables _variables;

        private RenderTextureDescriptor _targetDescriptor;

        private const int ReprojectPassIndex = 3;

        private bool _isDownsampling;

        private bool _tracingInCS;

        private bool _reprojectInCS;

        private bool _needAccumulate; // usePBRAlgo

        private float _screenSpaceAccumulationResolutionScale;
        
        private bool _previousAccumNeedClear;

        private ScreenSpaceReflectionAlgorithm _currentSSRAlgorithm;

        // PARAMETERS DECLARATION GUIDELINES:
        // All data is aligned on Vector4 size, arrays elements included.
        // - Shader side structure will be padded for anything not aligned to Vector4. Add padding accordingly.
        // - Base element size for array should be 4 components of 4 bytes (Vector4 or Vector4Int basically) otherwise the array will be interlaced with padding on shader side.
        // - In Metal the float3 and float4 are both actually sized and aligned to 16 bytes, whereas for Vulkan/SPIR-V, the alignment is the same. Do not use Vector3!
        // Try to keep data grouped by access and rendering system as much as possible (fog params or light params together for example).
        // => Don't move a float parameter away from where it belongs for filling a hole. Add padding in this case.
        private struct ScreenSpaceReflectionVariables
        {
            public Matrix4x4 ProjectionMatrix;
            
            public float Intensity;
            public float Thickness;
            public float ThicknessScale;
            public float ThicknessBias;
            
            public float Steps;
            public float StepSize;
            public float RoughnessFadeEnd;
            public float RoughnessFadeRcpLength;
            
            public float RoughnessFadeEndTimesRcpLength;
            public float EdgeFadeRcpLength;
            public int DepthPyramidMaxMip;
            public float DownsamplingDivider;
            
            public float AccumulationAmount;
            public float PBRSpeedRejection;
            public float PBRSpeedRejectionScalerFactor;
            public float PBRBias;

            public int ColorPyramidMaxMip;
        }

        public ScreenSpaceReflectionPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceReflectionPass;
            _computeShader = rendererData.RuntimeResources.screenSpaceReflectionCS;
            _tracingKernel = _computeShader.FindKernel("ScreenSpaceReflectionCS");
            _reprojectionKernel = _computeShader.FindKernel("ScreenSpaceReflectionReprojectionCS");
            _clearBuffer2DCS = rendererData.RuntimeResources.clearBuffer2D;
            _clearBuffer2DKernel = _clearBuffer2DCS.FindKernel("ClearBuffer2DMain");
            _accumulateNoWorldSpeedRejectionBothKernel = _computeShader.FindKernel("ScreenSpaceReflectionsAccumulateNoWorldSpeedRejectionBoth");
            _accumulateSmoothSpeedRejectionBothKernel = _computeShader.FindKernel("ScreenSpaceReflectionsAccumulateSmoothSpeedRejectionBoth");
            _accumulateNoWorldSpeedRejectionBothDebugKernel = _computeShader.FindKernel("ScreenSpaceReflectionsAccumulateNoWorldSpeedRejectionBothDebug");
            _accumulateSmoothSpeedRejectionBothDebugKernel = _computeShader.FindKernel("ScreenSpaceReflectionsAccumulateSmoothSpeedRejectionBothDebug");
            profilingSampler = new ProfilingSampler("Screen Space Reflection");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            _screenWidth = renderingData.cameraData.cameraTargetDescriptor.width;
            _screenHeight = renderingData.cameraData.cameraTargetDescriptor.height;
            _isDownsampling = volume.DownSample.value;
            int downsampleDivider = _isDownsampling ? 2 : 1;
            _rtWidth = _screenWidth / downsampleDivider;
            _rtHeight = _screenHeight / downsampleDivider;
            
            // @IllusionRP: Have not handled downsampling in compute shader yet.
            _tracingInCS = volume.mode == ScreenSpaceReflectionMode.HizSS 
                           && _rendererData.PreferComputeShader;
            _reprojectInCS = _rendererData.PreferComputeShader;
            // Skip accumulation in scene view
            _needAccumulate = _rendererData.PreferComputeShader && renderingData.cameraData.cameraType == CameraType.Game
                                                                && volume.usedAlgorithm.value == ScreenSpaceReflectionAlgorithm.PBRAccumulation;
            
            _previousAccumNeedClear = _needAccumulate && (_currentSSRAlgorithm == ScreenSpaceReflectionAlgorithm.Approximation || _rendererData.IsFirstFrame || _rendererData.ResetPostProcessingHistory);
            _currentSSRAlgorithm = volume.usedAlgorithm.value; // Store for next frame comparison

            // ================================ Allocation ================================ //
            _targetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            _targetDescriptor.msaaSamples = 1;
            _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16_UNorm; // Only need xy position
            _targetDescriptor.depthBufferBits = (int)DepthBits.None;
            _targetDescriptor.width = Mathf.CeilToInt(_rtWidth);
            _targetDescriptor.height = Mathf.CeilToInt(_rtHeight);
            _targetDescriptor.enableRandomWrite = _tracingInCS;
            RenderingUtils.ReAllocateIfNeeded(ref _ssrHitPointRT, _targetDescriptor, name: "_SsrHitPointTexture", filterMode: FilterMode.Point);
            cmd.SetGlobalTexture(_ssrHitPointRT.name, _ssrHitPointRT);

            _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            _targetDescriptor.enableRandomWrite = _reprojectInCS || _needAccumulate;
            RenderingUtils.ReAllocateIfNeeded(ref _ssrLightingRT, _targetDescriptor, name: "_SsrLightingTexture", filterMode: FilterMode.Point);
            cmd.SetGlobalTexture(_ssrLightingRT.name, _ssrLightingRT);

            if (_needAccumulate)
            {
                AllocateScreenSpaceAccumulationHistoryBuffer(_isDownsampling ? 0.5f : 1.0f);
                _computeShader.DisableKeyword("SSR_APPROX");
            }
            else
            {
                _computeShader.EnableKeyword("SSR_APPROX");
            }
            // ================================ Allocation ================================ //
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
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            var thickness = volume.thickness.value;
            var minSmoothness = volume.minSmoothness.value;
            var screenFadeDistance = volume.screenFadeDistance.value;
            var smoothnessFadeStart = volume.smoothnessFadeStart.value;
            float n = camera.nearClipPlane;
            float f = camera.farClipPlane;
            var scale = 1.0f / (1.0f + thickness);
            var bias = -n / (f - n) * (thickness * scale);
            float roughnessFadeStart = 1 - smoothnessFadeStart;
            float roughnessFadeEnd = 1 - minSmoothness;
            float roughnessFadeLength = roughnessFadeEnd - roughnessFadeStart;
            float roughnessFadeEndTimesRcpLength = (roughnessFadeLength != 0) ? roughnessFadeEnd * (1.0f / roughnessFadeLength) : 1;
            float roughnessFadeRcpLength = (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0;
            float edgeFadeRcpLength = Mathf.Min(1.0f / screenFadeDistance, float.MaxValue);
            var SSR_ProjectionMatrix = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false);
            var HalfCameraSize = new Vector2(
                (int)(cameraData.camera.pixelWidth * 0.5f),
                (int)(cameraData.camera.pixelHeight * 0.5f));
            int downsampleDivider = volume.DownSample.value ? 2 : 1;

            Matrix4x4 warpToScreenSpaceMatrix = Matrix4x4.identity;
            warpToScreenSpaceMatrix.m00 = HalfCameraSize.x;
            warpToScreenSpaceMatrix.m03 = HalfCameraSize.x;
            warpToScreenSpaceMatrix.m11 = HalfCameraSize.y;
            warpToScreenSpaceMatrix.m13 = HalfCameraSize.y;
            Matrix4x4 SSR_ProjectToPixelMatrix = warpToScreenSpaceMatrix * SSR_ProjectionMatrix;

            _variables.Intensity = volume.intensity.value;
            _variables.Thickness = thickness;
            _variables.ThicknessScale = scale;
            _variables.ThicknessBias = bias;
            _variables.Steps = volume.steps.value;
            _variables.StepSize = volume.stepSize.value;
            _variables.RoughnessFadeEnd = roughnessFadeEnd;
            _variables.RoughnessFadeRcpLength = roughnessFadeRcpLength;
            _variables.RoughnessFadeEndTimesRcpLength = roughnessFadeEndTimesRcpLength;
            _variables.EdgeFadeRcpLength = edgeFadeRcpLength;
            _variables.DepthPyramidMaxMip = _rendererData.DepthMipChainInfo.mipLevelCount - 1;
            _variables.ColorPyramidMaxMip = _rendererData.ColorPyramidHistoryMipCount - 1;
            _variables.DownsamplingDivider = 1.0f / downsampleDivider;
            _variables.ProjectionMatrix = SSR_ProjectToPixelMatrix;
            
            // PBR properties only be used in compute shader mode
            _variables.PBRBias = volume.biasFactor.value;
            _variables.PBRSpeedRejection = Mathf.Clamp01(volume.speedRejectionParam.value);
            _variables.PBRSpeedRejectionScalerFactor = Mathf.Pow(volume.speedRejectionScalerFactor.value * 0.1f, 2.0f);
            if (_rendererData.FrameCount <= 3)
            {
                _variables.AccumulationAmount = 1.0f;
            }
            else
            {
                _variables.AccumulationAmount = Mathf.Pow(2, Mathf.Lerp(0.0f, -7.0f, volume.accumulationFactor.value));
            }
        }

        /// <summary>
        /// Use properties instead of constant buffer in pixel shader
        /// </summary>
        /// <param name="propertyBlock"></param>
        /// <param name="variables"></param>
        private static void SetPixelShaderProperties(MaterialPropertyBlock propertyBlock, ScreenSpaceReflectionVariables variables)
        {
            propertyBlock.SetFloat(Properties.SsrIntensity, variables.Intensity);
            propertyBlock.SetFloat(Properties.Thickness, variables.Thickness);
            propertyBlock.SetFloat(Properties.SsrThicknessScale, variables.ThicknessScale);
            propertyBlock.SetFloat(Properties.SsrThicknessBias, variables.ThicknessBias);
            propertyBlock.SetFloat(Properties.Steps, variables.Steps);
            propertyBlock.SetFloat(Properties.StepSize, variables.StepSize);
            propertyBlock.SetFloat(Properties.SsrRoughnessFadeEnd, variables.RoughnessFadeEnd);
            propertyBlock.SetFloat(Properties.SsrRoughnessFadeEndTimesRcpLength, variables.RoughnessFadeEndTimesRcpLength);
            propertyBlock.SetFloat(Properties.SsrRoughnessFadeRcpLength, variables.RoughnessFadeRcpLength);
            propertyBlock.SetFloat(Properties.SsrEdgeFadeRcpLength, variables.EdgeFadeRcpLength);
            propertyBlock.SetInteger(Properties.SsrDepthPyramidMaxMip, variables.DepthPyramidMaxMip);
            propertyBlock.SetInteger(Properties.SsrColorPyramidMaxMip, variables.ColorPyramidMaxMip);
            propertyBlock.SetFloat(Properties.SsrDownsamplingDivider, variables.DownsamplingDivider);
            propertyBlock.SetMatrix(Properties.SsrProjectionMatrix, variables.ProjectionMatrix);
        }

        private void ExecuteTracing(CommandBuffer cmd, ref CameraData cameraData, bool asyncCompute)
        {
            // Stencil has been written to depth texture in gbuffer
            var depthStencilTexture = UniversalRenderingUtility.GetDepthWriteTexture(ref cameraData);
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);
            if (!depthStencilTexture.IsValid() || !normalTexture.IsValid())
            {
                return;
            }

            // ============================ Property Block ======================== //
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            var passIndex = (int)volume.mode.value;
            var offsetBuffer = _rendererData.DepthMipChainInfo.GetOffsetBufferData(_rendererData.DepthPyramidMipLevelOffsetsBuffer);
            // ============================ Property Block ======================== //

            if (_tracingInCS)
            {
                cmd.SetComputeBufferParam(_computeShader, _tracingKernel, IllusionShaderProperties._DepthPyramidMipLevelOffsets, offsetBuffer);
                cmd.SetComputeTextureParam(_computeShader, _tracingKernel, IllusionShaderProperties._StencilTexture, 
                    depthStencilTexture, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(_computeShader, _tracingKernel, IllusionShaderProperties._CameraNormalsTexture, normalTexture);
                cmd.SetComputeTextureParam(_computeShader, _tracingKernel, Properties.SsrHitPointTexture, _ssrHitPointRT);

                ConstantBuffer.Push(cmd, _variables, _computeShader, Properties.ShaderVariablesScreenSpaceReflection);
                
                int groupsX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, 8);
                int groupsY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, 8);
                cmd.DispatchCompute(_computeShader, _tracingKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
            }
            else
            {
                var material = _material.Value;
                cmd.SetRenderTarget(_ssrHitPointRT);
                SharedPropertyBlock.Clear();
                SetPixelShaderProperties(SharedPropertyBlock, _variables);

                SharedPropertyBlock.SetBuffer(IllusionShaderProperties._DepthPyramidMipLevelOffsets, offsetBuffer);
                SharedPropertyBlock.SetTexture(IllusionShaderProperties._StencilTexture, depthStencilTexture, RenderTextureSubElement.Stencil);
                // We need to set the "_BlitScaleBias" uniform for user materials with shaders relying on core Blit.hlsl to work
                SharedPropertyBlock.SetVector(IllusionShaderProperties._BlitScaleBias, new Vector4(1, 1, 0, 0));

                cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1,
                    SharedPropertyBlock);
            }
        }

        private void ExecuteReprojection(CommandBuffer cmd, ref CameraData cameraData)
        {
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(cameraData.renderer);
            if (!normalTexture.IsValid())
            {
                return;
            }

            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(cameraData, out bool isNewFrame);
            var motionVectorColorRT = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);

            if (_reprojectInCS)
            {
                cmd.SetComputeTextureParam(_computeShader, _reprojectionKernel,
                    IllusionShaderProperties._MotionVectorTexture,
                    isNewFrame ? motionVectorColorRT : Texture2D.blackTexture);
                cmd.SetComputeTextureParam(_computeShader, _reprojectionKernel,
                    IllusionShaderProperties._ColorPyramidTexture, preFrameColorRT);
                cmd.SetComputeTextureParam(_computeShader, _reprojectionKernel,
                    IllusionShaderProperties._CameraNormalsTexture, normalTexture);
                cmd.SetComputeTextureParam(_computeShader, _reprojectionKernel, Properties.SsrHitPointTexture,
                    _ssrHitPointRT);
                if (_needAccumulate)
                {
                    var ssrAccum = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                    cmd.SetComputeTextureParam(_computeShader, _reprojectionKernel, Properties.SsrAccumTexture, ssrAccum);
                }
                else
                {
                    cmd.SetComputeTextureParam(_computeShader, _reprojectionKernel, Properties.SsrAccumTexture, _ssrLightingRT);
                }
                ConstantBuffer.Push(cmd, _variables, _computeShader, Properties.ShaderVariablesScreenSpaceReflection);
                
                int groupsX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, 8);
                int groupsY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, 8);
                cmd.DispatchCompute(_computeShader, _reprojectionKernel, groupsX, groupsY,
                    IllusionRendererData.MaxViewCount);
            }
            else
            {
                var material = _material.Value;
                cmd.SetRenderTarget(_ssrLightingRT);
                SharedPropertyBlock.Clear();
                SetPixelShaderProperties(SharedPropertyBlock, _variables);
                
                SharedPropertyBlock.SetTexture(IllusionShaderProperties._MotionVectorTexture,
                    motionVectorColorRT.IsValid() ? motionVectorColorRT : Texture2D.blackTexture);
                SharedPropertyBlock.SetTexture(IllusionShaderProperties._ColorPyramidTexture, preFrameColorRT);
                SharedPropertyBlock.SetTexture(IllusionShaderProperties._CameraNormalsTexture, normalTexture);
                SharedPropertyBlock.SetTexture(Properties.SsrHitPointTexture, _ssrHitPointRT);
                // We need to set the "_BlitScaleBias" uniform for user materials with shaders relying on core Blit.hlsl to work
                SharedPropertyBlock.SetVector(IllusionShaderProperties._BlitScaleBias, new Vector4(1, 1, 0, 0));
                
                cmd.DrawProcedural(Matrix4x4.identity, material, ReprojectPassIndex, MeshTopology.Triangles, 3, 1,
                    SharedPropertyBlock);
            }
        }

        private void AllocateScreenSpaceAccumulationHistoryBuffer(float scaleFactor)
        {
            if (scaleFactor != _screenSpaceAccumulationResolutionScale || _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation) == null)
            {
                _rendererData.ReleaseHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);

                var ssrAlloc = new IllusionRendererData.CustomHistoryAllocator(new Vector2(scaleFactor, scaleFactor), GraphicsFormat.R16G16B16A16_SFloat, "SSR_Accum Packed history");
                _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation, ssrAlloc.Allocator, 2);

                _screenSpaceAccumulationResolutionScale = scaleFactor;
            }
        }

        private void ExecuteAccumulation(CommandBuffer cmd, ref CameraData cameraData, bool useAsyncCompute)
        {
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            int kernel;
#if UNITY_EDITOR
            if (volume.fullScreenDebugMode.value)
#else
            if (IllusionRuntimeRenderingConfig.Get().EnableScreenSpaceReflectionDebug)
#endif
            {
                if (volume.enableWorldSpeedRejection.value)
                {
                    kernel = _accumulateSmoothSpeedRejectionBothDebugKernel;
                }
                else
                {
                    kernel = _accumulateNoWorldSpeedRejectionBothDebugKernel;
                }
            }
            else
            {
                if (volume.enableWorldSpeedRejection.value)
                {
                    kernel = _accumulateSmoothSpeedRejectionBothKernel;
                }
                else
                {
                    kernel = _accumulateNoWorldSpeedRejectionBothKernel;
                }
            }
            var ssrAccum = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
            var ssrAccumPrev = _rendererData.GetPreviousFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(cameraData, out bool isNewFrame);
            var motionVectorColorRT = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);
            
            cmd.SetComputeTextureParam(_computeShader, kernel,
                IllusionShaderProperties._MotionVectorTexture,
                isNewFrame ? motionVectorColorRT : Texture2D.blackTexture);
            cmd.SetComputeTextureParam(_computeShader, kernel,
                IllusionShaderProperties._ColorPyramidTexture, preFrameColorRT);
            cmd.SetComputeTextureParam(_computeShader, kernel, Properties.SsrAccumTexture, ssrAccum);
            cmd.SetComputeTextureParam(_computeShader, kernel, Properties.SsrAccumPrev, ssrAccumPrev);
            cmd.SetComputeTextureParam(_computeShader, kernel, Properties.SsrLightingTextureRW, _ssrLightingRT);

            ConstantBuffer.Push(cmd, _variables, _computeShader, Properties.ShaderVariablesScreenSpaceReflection);
                
            int groupsX = IllusionRenderingUtils.DivRoundUp((int)_rtWidth, 8);
            int groupsY = IllusionRenderingUtils.DivRoundUp((int)_rtHeight, 8);
            cmd.DispatchCompute(_computeShader, kernel, groupsX, groupsY,
                IllusionRendererData.MaxViewCount);
        }

        private void ClearColorBuffer2D(CommandBuffer cmd, RTHandle rt, Color inClearColor, bool async)
        {
            if (async)
            {
                cmd.SetComputeTextureParam(_clearBuffer2DCS, _clearBuffer2DKernel, IllusionShaderProperties._Buffer2D,
                    rt);
                cmd.SetComputeVectorParam(_clearBuffer2DCS, IllusionShaderProperties._ClearValue, inClearColor);
                cmd.SetComputeVectorParam(_clearBuffer2DCS, IllusionShaderProperties._BufferSize,
                    new Vector4(_rtWidth, _rtHeight, 0.0f, 0.0f));
                cmd.DispatchCompute(_clearBuffer2DCS, _clearBuffer2DKernel,
                    IllusionRenderingUtils.DivRoundUp((int)_rtWidth, 8),
                    IllusionRenderingUtils.DivRoundUp((int)_rtHeight, 8),
                    IllusionRendererData.MaxViewCount);
            }
            else
            {
                CoreUtils.SetRenderTarget(cmd, rt, ClearFlag.Color, inClearColor);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderer.cameraColorTargetHandle == null)
                return;

            var material = _material.Value;
            if (!material)
                return;
            
            if (cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
                return;
            
            // The first color pyramid of the frame is generated after the SSR transparent, so we have no choice but to use the previous
            // frame color pyramid (that includes transparents from the previous frame).
            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(cameraData, out _);
            if (preFrameColorRT == null)
            {
                return;
            }

            PrepareVariables(ref cameraData);

            var cmd = CommandBufferPool.Get();
            bool useAsyncCompute = _reprojectInCS && _tracingInCS && _needAccumulate 
                                   && IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute;
            if (useAsyncCompute)
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
            }
            
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Always use APPROX mode in Fragment Shader
                if (_rendererData.PreferComputeShader && _needAccumulate)
                {
                    var ssrAccum = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                    ClearColorBuffer2D(cmd, ssrAccum, Color.clear, useAsyncCompute);
                    if (_previousAccumNeedClear)
                    {
                        var ssrAccumPrev = _rendererData.GetPreviousFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                        ClearColorBuffer2D(cmd, ssrAccumPrev, Color.clear, useAsyncCompute);
                    }
                }
                
                ClearColorBuffer2D(cmd, _ssrHitPointRT, Color.clear, useAsyncCompute);

                using (new ProfilingScope(cmd, _tracingSampler))
                {
                    ExecuteTracing(cmd, ref cameraData, useAsyncCompute);
                }
                
                using (new ProfilingScope(cmd, _reprojectionSampler))
                {
                    ExecuteReprojection(cmd, ref cameraData);
                }

                if (_needAccumulate)
                {
                    using (new ProfilingScope(cmd, _accumulationSampler))
                    {
                        ExecuteAccumulation(cmd, ref cameraData, useAsyncCompute);
                    }
                }
                
                if (useAsyncCompute)
                {
                    _rendererData.CreateAsyncGraphicsFence(cmd, IllusionGraphicsFenceEvent.ScreenSpaceReflection);
                }
            }
            
            if (useAsyncCompute)
            {
                context.ExecuteCommandBufferAsync(cmd, ComputeQueueType.Background);
            }
            else
            {
                context.ExecuteCommandBuffer(cmd);
            }
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _material.DestroyCache();
            _ssrHitPointRT?.Release();
            _ssrLightingRT?.Release();
        }

        private static class Properties
        {
            public static readonly int SsrProjectionMatrix = Shader.PropertyToID("_SSR_ProjectionMatrix");

            public static readonly int SsrIntensity = Shader.PropertyToID("_SSRIntensity");

            public static readonly int Thickness = Shader.PropertyToID("_Thickness");

            public static readonly int Steps = Shader.PropertyToID("_Steps");

            public static readonly int StepSize = Shader.PropertyToID("_StepSize");

            public static readonly int SsrThicknessScale = Shader.PropertyToID("_SsrThicknessScale");

            public static readonly int SsrThicknessBias = Shader.PropertyToID("_SsrThicknessBias");

            public static readonly int SsrDepthPyramidMaxMip = Shader.PropertyToID("_SsrDepthPyramidMaxMip");
            
            public static readonly int SsrColorPyramidMaxMip = Shader.PropertyToID("_SsrColorPyramidMaxMip");

            public static readonly int SsrRoughnessFadeEnd = Shader.PropertyToID("_SsrRoughnessFadeEnd");

            public static readonly int SsrRoughnessFadeEndTimesRcpLength = Shader.PropertyToID("_SsrRoughnessFadeEndTimesRcpLength");

            public static readonly int SsrRoughnessFadeRcpLength = Shader.PropertyToID("_SsrRoughnessFadeRcpLength");

            public static readonly int SsrEdgeFadeRcpLength = Shader.PropertyToID("_SsrEdgeFadeRcpLength");
            
            public static readonly int SsrDownsamplingDivider = Shader.PropertyToID("_SsrDownsamplingDivider");

            public static readonly int SsrAccumTexture = Shader.PropertyToID("_SsrAccumTexture");

            public static readonly int SsrHitPointTexture = Shader.PropertyToID("_SsrHitPointTexture");
            
            public static readonly int SsrAccumPrev = Shader.PropertyToID("_SsrAccumPrev");
            
            public static readonly int SsrLightingTextureRW = Shader.PropertyToID("_SsrLightingTextureRW");
            
            public static readonly int ShaderVariablesScreenSpaceReflection = MemberNameHelpers.ShaderPropertyID();
        }
    }
}