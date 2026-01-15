using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class ScreenSpaceReflectionPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _material = new(IllusionShaders.ScreenSpaceReflection);

        private RTHandle _ssrHitPointRT;

        private RTHandle _ssrLightingRT;

        private int _rtHeight;

        private int _rtWidth;

        private int _screenHeight;

        private int _screenWidth;

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

        private static readonly ProfilingSampler _tracingSampler = new("Tracing");

        private static readonly ProfilingSampler _reprojectionSampler = new("Reprojection");
        
        private static readonly ProfilingSampler _accumulationSampler = new("Accumulation");

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
            ConfigureInput(ScriptableRenderPassInput.Depth
                           | ScriptableRenderPassInput.Normal
                           | ScriptableRenderPassInput.Motion);
        }

        private void PrepareSSRData(ref RenderingData renderingData, bool useRenderGraph)
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
            _reprojectInCS = _tracingInCS;
            // Skip accumulation in scene view
            _needAccumulate = _reprojectInCS && renderingData.cameraData.cameraType == CameraType.Game
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
            
            if (_needAccumulate || useRenderGraph)
            {
                AllocateScreenSpaceAccumulationHistoryBuffer(_isDownsampling ? 0.5f : 1.0f);
            }
            // ================================ Allocation ================================ //
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

        private bool IsSSREnabled(ContextContainer frameData)
        {
            if (!_rendererData.SampleScreenSpaceReflection)
            {
                return false;
            }

            var material = _material.Value;
            if (!material)
                return false;
            
            // The first color pyramid of the frame is generated after the SSR transparent, so we have no choice but to use the previous
            // frame color pyramid (that includes transparents from the previous frame).
            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(frameData, out _);
            if (preFrameColorRT == null)
            {
                return false;
            }

            return true;
        }

        private class TracingPassData
        {
            public ScreenSpaceReflectionVariables Variables;
            public ComputeShader ComputeShader;
            public int TracingKernel;
            public Material Material;
            public bool TracingInCS;
            public int Width;
            public int Height;
            public int ViewCount;
            public ComputeBuffer OffsetBuffer;
            
            public TextureHandle HitPointTexture;
            public TextureHandle DepthStencilTexture;
            public TextureHandle NormalTexture;
            public TextureHandle DepthPyramidTexture;
            public DitheredTextureHandleSet BlueNoiseResources;
        }
        
        private class ReprojectionPassData
        {
            public ScreenSpaceReflectionVariables Variables;
            public ComputeShader ComputeShader;
            public int ReprojectionKernel;
            public Material Material;
            public bool ReprojectInCS;
            public int Width;
            public int Height;
            public int ViewCount;
            
            public TextureHandle HitPointTexture;
            public TextureHandle ColorPyramidTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle NormalTexture;
            public TextureHandle SsrAccumTexture;
        }
        
        private class AccumulationPassData
        {
            public ScreenSpaceReflectionVariables Variables;
            public ComputeShader ComputeShader;
            public int AccumulationKernel;
            public int Width;
            public int Height;
            public int ViewCount;
            
            public TextureHandle HitPointTexture;
            public TextureHandle ColorPyramidTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle SsrAccum;
            public TextureHandle SsrAccumPrev;
            public TextureHandle SsrLightingTextureRW;
        }
        
        private class CombinedSSRPassData
        {
            public ScreenSpaceReflectionVariables Variables;
            public ComputeShader ComputeShader;
            public int TracingKernel;
            public int ReprojectionKernel;
            public int AccumulationKernel;
            public int Width;
            public int Height;
            public int ViewCount;
            public ComputeBuffer OffsetBuffer;
            public bool UsePBRAlgo;
            public bool UseAsync;
            public bool PreviousAccumNeedClear;
            public bool ValidColorPyramid;
            
            // Clear resources
            public ComputeShader ClearBuffer2DCS;
            public int ClearBuffer2DKernel;
            
            // Texture handles
            public TextureHandle HitPointTexture;
            public TextureHandle DepthStencilTexture;
            public TextureHandle NormalTexture;
            public TextureHandle DepthPyramidTexture;
            public TextureHandle ColorPyramidTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle SsrAccum;
            public TextureHandle SsrAccumPrev;
            public DitheredTextureHandleSet BlueNoiseResources;
        }
        
        private class ClearPassData
        {
            public ComputeShader ClearBuffer2DCS;
            public int ClearBuffer2DKernel;
            public Vector4 BufferSize;
            public Color ClearColor;
            
            public TextureHandle TargetTexture;
        }

        private TextureHandle RenderTracingRasterPass(RenderGraph renderGraph, TextureHandle hitPointTexture,
            TextureHandle depthStencilTexture, TextureHandle normalTexture, TextureHandle depthPyramidTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<TracingPassData>("SSR Tracing (Raster)", out var passData, _tracingSampler))
            {
                var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
                var passIndex = (int)volume.mode.value;

                passData.TracingKernel = passIndex;
                passData.Variables = _variables;
                passData.Material = _material.Value;
                passData.TracingInCS = _tracingInCS;
                passData.OffsetBuffer = _rendererData.DepthMipChainInfo.GetOffsetBufferData(_rendererData.DepthPyramidMipLevelOffsetsBuffer);
                
                builder.SetRenderAttachment(hitPointTexture, 0);
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(depthStencilTexture);
                passData.DepthStencilTexture = depthStencilTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                builder.UseTexture(depthPyramidTexture);
                passData.DepthPyramidTexture = depthPyramidTexture;
                
                passData.BlueNoiseResources = _rendererData.DitheredTextureHandleSet1SPP;
                builder.UseTexture(passData.BlueNoiseResources.owenScrambled256Tex);
                builder.UseTexture(passData.BlueNoiseResources.rankingTile);
                builder.UseTexture(passData.BlueNoiseResources.scramblingTile);
                builder.UseTexture(passData.BlueNoiseResources.scramblingTex);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (TracingPassData data, RasterGraphContext context) =>
                {
                    IllusionRendererData.BindDitheredTextureSet(context.cmd, data.BlueNoiseResources);
                    var propertyBlock = new MaterialPropertyBlock();
                    SetPixelShaderProperties(propertyBlock, data.Variables);
                    propertyBlock.SetBuffer(IllusionShaderProperties._DepthPyramidMipLevelOffsets, data.OffsetBuffer);
                    propertyBlock.SetTexture(IllusionShaderProperties._StencilTexture, data.DepthStencilTexture, RenderTextureSubElement.Stencil);
                    propertyBlock.SetVector(IllusionShaderProperties._BlitScaleBias, new Vector4(1, 1, 0, 0));
                    
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, data.TracingKernel, MeshTopology.Triangles, 3, 1, propertyBlock);
                });
                
                return passData.HitPointTexture;
            }
        }
        
        private TextureHandle RenderReprojectionRasterPass(RenderGraph renderGraph, TextureHandle hitPointTexture,
            TextureHandle colorPyramidTexture, TextureHandle motionVectorTexture, TextureHandle normalTexture,
            TextureHandle ssrAccumTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ReprojectionPassData>("SSR Reprojection (Raster)", out var passData, _reprojectionSampler))
            {
                passData.Variables = _variables;
                passData.Material = _material.Value;
                passData.ReprojectInCS = _reprojectInCS;
                
                builder.UseTexture(hitPointTexture);
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(colorPyramidTexture);
                passData.ColorPyramidTexture = colorPyramidTexture;
                builder.UseTexture(motionVectorTexture);
                passData.MotionVectorTexture = motionVectorTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                builder.SetRenderAttachment(ssrAccumTexture, 0);
                passData.SsrAccumTexture = ssrAccumTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (ReprojectionPassData data, RasterGraphContext context) =>
                {
                    var propertyBlock = new MaterialPropertyBlock();
                    SetPixelShaderProperties(propertyBlock, data.Variables);
                    
                    propertyBlock.SetTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorTexture);
                    propertyBlock.SetTexture(IllusionShaderProperties._ColorPyramidTexture, data.ColorPyramidTexture);
                    propertyBlock.SetTexture(IllusionShaderProperties._CameraNormalsTexture, data.NormalTexture);
                    propertyBlock.SetTexture(Properties.SsrHitPointTexture, data.HitPointTexture);
                    propertyBlock.SetVector(IllusionShaderProperties._BlitScaleBias, new Vector4(1, 1, 0, 0));
                    
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, ReprojectPassIndex, MeshTopology.Triangles, 3, 1, propertyBlock);
                });
                
                return passData.SsrAccumTexture;
            }
        }
        
        private TextureHandle RenderAccumulationPass(RenderGraph renderGraph, TextureHandle hitPointTexture, TextureHandle colorPyramidTexture,
            TextureHandle motionVectorTexture, TextureHandle ssrAccum, TextureHandle ssrAccumPrev, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<AccumulationPassData>("SSR Accumulation", out var passData, _accumulationSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);
                
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
                
                passData.Variables = _variables;
                passData.ComputeShader = _computeShader;
                passData.AccumulationKernel = kernel;
                passData.Width = _rtWidth;
                passData.Height = _rtHeight;
                passData.ViewCount = IllusionRendererData.MaxViewCount;

                builder.UseTexture(hitPointTexture);
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(colorPyramidTexture);
                passData.ColorPyramidTexture = colorPyramidTexture;
                builder.UseTexture(motionVectorTexture);
                passData.MotionVectorTexture = motionVectorTexture;
                builder.UseTexture(ssrAccum, AccessFlags.ReadWrite);
                passData.SsrAccum = ssrAccum;
                builder.UseTexture(ssrAccumPrev);
                passData.SsrAccumPrev = ssrAccumPrev;
                // passData.SsrLightingTextureRW = builder.UseTexture(ssrLightingTextureRW, AccessFlags.Write);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (AccumulationPassData data, ComputeGraphContext context) =>
                {
                    ConstantBuffer.Push(context.cmd, data.Variables, data.ComputeShader, Properties.ShaderVariablesScreenSpaceReflection);
                    
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.AccumulationKernel, IllusionShaderProperties._MotionVectorTexture, data.MotionVectorTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.AccumulationKernel, IllusionShaderProperties._ColorPyramidTexture, data.ColorPyramidTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.AccumulationKernel, Properties.SsrAccumTexture, data.SsrAccum);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.AccumulationKernel, Properties.SsrAccumPrev, data.SsrAccumPrev);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.AccumulationKernel,Properties.SsrHitPointTexture, data.HitPointTexture);
                    // context.cmd.SetComputeTextureParam(data.ComputeShader, data.AccumulationKernel, Properties.SsrLightingTextureRW, data.SsrLightingTextureRW);
                    
                    int groupsX = IllusionRenderingUtils.DivRoundUp(data.Width, 8);
                    int groupsY = IllusionRenderingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.ComputeShader, data.AccumulationKernel, groupsX, groupsY, data.ViewCount);
                });
                
                return passData.SsrAccum;
            }
        }
        
        private void ClearTexturePass(RenderGraph renderGraph, TextureHandle targetTexture, Color clearColor)
        {
            using (var builder = renderGraph.AddUnsafePass<ClearPassData>("Clear SSR Texture", out var passData))
            {
                passData.ClearColor = clearColor;
                builder.UseTexture(targetTexture, AccessFlags.Write);
                passData.TargetTexture = targetTexture;
                    
                builder.AllowPassCulling(false);
                    
                builder.SetRenderFunc(static (ClearPassData data, UnsafeGraphContext context) =>
                {
                    CoreUtils.SetRenderTarget(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), data.TargetTexture, ClearFlag.Color, data.ClearColor);
                });
            }
        }

        private static void ClearColorBuffer2D(CombinedSSRPassData data, ComputeCommandBuffer cmd, 
            TextureHandle texture, Color clearColor, bool useAsync)
        {
            if (!useAsync)
            {
                CoreUtils.SetRenderTarget(cmd, texture, ClearFlag.Color, clearColor);
                return;
            }
            
            cmd.SetComputeTextureParam(data.ClearBuffer2DCS, data.ClearBuffer2DKernel, IllusionShaderProperties._Buffer2D, texture);
            cmd.SetComputeVectorParam(data.ClearBuffer2DCS, IllusionShaderProperties._ClearValue, clearColor);
            cmd.SetComputeVectorParam(data.ClearBuffer2DCS, IllusionShaderProperties._BufferSize, new Vector4(data.Width, data.Height, 0.0f, 0.0f));
            cmd.DispatchCompute(data.ClearBuffer2DCS, data.ClearBuffer2DKernel,
                IllusionRenderingUtils.DivRoundUp(data.Width, 8),
                IllusionRenderingUtils.DivRoundUp(data.Height, 8),
                IllusionRendererData.MaxViewCount);
        }
        
        private TextureHandle RenderSSRComputePass(RenderGraph renderGraph, TextureHandle hitPointTexture,
            TextureHandle depthStencilTexture, TextureHandle normalTexture, TextureHandle depthPyramidTexture,
            TextureHandle colorPyramidTexture, TextureHandle motionVectorTexture, TextureHandle ssrAccum, 
            TextureHandle ssrAccumPrev, bool useAsyncCompute, bool isNewFrame)
        {
            using (var builder = renderGraph.AddComputePass<CombinedSSRPassData>("Render SSR", out var passData, profilingSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);
                
                var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
                
                // Determine accumulation kernel
                int accumulationKernel = 0;
#if UNITY_EDITOR
                bool debugDisplay = volume.fullScreenDebugMode.value;
#else
                bool debugDisplay = IllusionRuntimeRenderingConfig.Get().EnableScreenSpaceReflectionDebug;
#endif
                if (_needAccumulate)
                {
                    if (debugDisplay)
                    {
                        if (volume.enableWorldSpeedRejection.value)
                        {
                            accumulationKernel = _accumulateSmoothSpeedRejectionBothDebugKernel;
                        }
                        else
                        {
                            accumulationKernel = _accumulateNoWorldSpeedRejectionBothDebugKernel;
                        }
                    }
                    else
                    {
                        if (volume.enableWorldSpeedRejection.value)
                        {
                            accumulationKernel = _accumulateSmoothSpeedRejectionBothKernel;
                        }
                        else
                        {
                            accumulationKernel = _accumulateNoWorldSpeedRejectionBothKernel;
                        }
                    }
                }
                
                // Setup pass data
                passData.Variables = _variables;
                passData.ComputeShader = _computeShader;
                passData.TracingKernel = _tracingKernel;
                passData.ReprojectionKernel = _reprojectionKernel;
                passData.AccumulationKernel = accumulationKernel;
                passData.Width = _rtWidth;
                passData.Height = _rtHeight;
                passData.ViewCount = IllusionRendererData.MaxViewCount;
                passData.OffsetBuffer = _rendererData.DepthMipChainInfo.GetOffsetBufferData(_rendererData.DepthPyramidMipLevelOffsetsBuffer);
                passData.UsePBRAlgo = _needAccumulate;
                passData.UseAsync = useAsyncCompute;
                passData.PreviousAccumNeedClear = _previousAccumNeedClear;
                passData.ValidColorPyramid = isNewFrame;
                
                // Clear resources
                passData.ClearBuffer2DCS = _clearBuffer2DCS;
                passData.ClearBuffer2DKernel = _clearBuffer2DKernel;
                
                // Texture handles
                builder.UseTexture(hitPointTexture, AccessFlags.ReadWrite);
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(depthStencilTexture);
                passData.DepthStencilTexture = depthStencilTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                builder.UseTexture(depthPyramidTexture);
                passData.DepthPyramidTexture = depthPyramidTexture;
                builder.UseTexture(colorPyramidTexture);
                passData.ColorPyramidTexture = colorPyramidTexture;
                builder.UseTexture(motionVectorTexture);
                passData.MotionVectorTexture = motionVectorTexture;
                builder.UseTexture(ssrAccum, AccessFlags.ReadWrite);
                passData.SsrAccum = ssrAccum;
                
                if (_needAccumulate)
                {
                    builder.UseTexture(ssrAccumPrev, AccessFlags.ReadWrite);
                    passData.SsrAccumPrev = ssrAccumPrev;
                }
                
                // Blue noise resources
                passData.BlueNoiseResources = _rendererData.DitheredTextureHandleSet1SPP;
                builder.UseTexture(passData.BlueNoiseResources.owenScrambled256Tex);
                builder.UseTexture(passData.BlueNoiseResources.rankingTile);
                builder.UseTexture(passData.BlueNoiseResources.scramblingTile);
                builder.UseTexture(passData.BlueNoiseResources.scramblingTex);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (CombinedSSRPassData data, ComputeGraphContext ctx) =>
                {
                    var cs = data.ComputeShader;
                    ConstantBuffer.Push(ctx.cmd, data.Variables, cs, Properties.ShaderVariablesScreenSpaceReflection);
                    
                    IllusionRendererData.BindDitheredTextureSet(ctx.cmd, data.BlueNoiseResources);
                    
                    CoreUtils.SetKeyword(ctx.cmd, "SSR_APPROX", !data.UsePBRAlgo);
                    
                    // Clear operations
                    if (data.UsePBRAlgo || data.UseAsync)
                    {
                        ClearColorBuffer2D(data, ctx.cmd, data.SsrAccum, Color.clear, data.UseAsync);
                    }
                    
                    if (data.UsePBRAlgo && data.PreviousAccumNeedClear)
                    {
                        ClearColorBuffer2D(data, ctx.cmd, data.SsrAccumPrev, Color.clear, data.UseAsync);
                    }
                    
                    if (data.UseAsync)
                    {
                        ClearColorBuffer2D(data, ctx.cmd, data.HitPointTexture, Color.clear, data.UseAsync);
                    }
                    
                    int groupsX = IllusionRenderingUtils.DivRoundUp(data.Width, 8);
                    int groupsY = IllusionRenderingUtils.DivRoundUp(data.Height, 8);
                    
                    // Tracing pass
                    using (new ProfilingScope(ctx.cmd, _tracingSampler))
                    {
                        ctx.cmd.SetComputeBufferParam(cs, data.TracingKernel,
                            IllusionShaderProperties._DepthPyramidMipLevelOffsets, data.OffsetBuffer);
                        ctx.cmd.SetComputeTextureParam(cs, data.TracingKernel, IllusionShaderProperties._StencilTexture,
                            data.DepthStencilTexture, 0, RenderTextureSubElement.Stencil);
                        ctx.cmd.SetComputeTextureParam(cs, data.TracingKernel,
                            IllusionShaderProperties._CameraNormalsTexture, data.NormalTexture);
                        ctx.cmd.SetComputeTextureParam(cs, data.TracingKernel, Properties.SsrHitPointTexture,
                            data.HitPointTexture);

                        ctx.cmd.DispatchCompute(cs, data.TracingKernel, groupsX, groupsY, data.ViewCount);
                    }

                    // Reprojection pass
                    using (new ProfilingScope(ctx.cmd, _reprojectionSampler))
                    {
                        ctx.cmd.SetComputeTextureParam(cs, data.ReprojectionKernel,
                            IllusionShaderProperties._MotionVectorTexture, data.MotionVectorTexture);
                        ctx.cmd.SetComputeTextureParam(cs, data.ReprojectionKernel,
                            IllusionShaderProperties._ColorPyramidTexture, data.ColorPyramidTexture);
                        ctx.cmd.SetComputeTextureParam(cs, data.ReprojectionKernel,
                            IllusionShaderProperties._CameraNormalsTexture, data.NormalTexture);
                        ctx.cmd.SetComputeTextureParam(cs, data.ReprojectionKernel, Properties.SsrHitPointTexture,
                            data.HitPointTexture);
                        ctx.cmd.SetComputeTextureParam(cs, data.ReprojectionKernel, Properties.SsrAccumTexture,
                            data.SsrAccum);

                        ctx.cmd.DispatchCompute(cs, data.ReprojectionKernel, groupsX, groupsY, data.ViewCount);
                    }

                    // Accumulation pass (PBR mode only)
                    if (data.UsePBRAlgo)
                    {
                        if (!data.ValidColorPyramid)
                        {
                            ClearColorBuffer2D(data, ctx.cmd, data.SsrAccum, Color.clear, data.UseAsync);
                            ClearColorBuffer2D(data, ctx.cmd, data.SsrAccumPrev, Color.clear, data.UseAsync);
                        }
                        else
                        {
                            using (new ProfilingScope(ctx.cmd, _accumulationSampler))
                            {
                                ctx.cmd.SetComputeTextureParam(cs, data.AccumulationKernel,
                                    IllusionShaderProperties._MotionVectorTexture, data.MotionVectorTexture);
                                ctx.cmd.SetComputeTextureParam(cs, data.AccumulationKernel,
                                    IllusionShaderProperties._ColorPyramidTexture, data.ColorPyramidTexture);
                                ctx.cmd.SetComputeTextureParam(cs, data.AccumulationKernel, Properties.SsrAccumTexture,
                                    data.SsrAccum);
                                ctx.cmd.SetComputeTextureParam(cs, data.AccumulationKernel, Properties.SsrAccumPrev,
                                    data.SsrAccumPrev);
                                ctx.cmd.SetComputeTextureParam(cs, data.AccumulationKernel,
                                    Properties.SsrHitPointTexture, data.HitPointTexture);

                                ctx.cmd.DispatchCompute(cs, data.AccumulationKernel, groupsX, groupsY, data.ViewCount);
                            }
                        }
                    }
                });
                
                return passData.SsrAccum;
            }
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = new RenderingData(frameData);
            var resource = frameData.Get<UniversalResourceData>();
            
            // Check if SSR is enabled
            if (!IsSSREnabled(frameData))
            {
                var blackHandle = renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());
                RenderGraphUtils.SetGlobalTexture(renderGraph, IllusionShaderProperties.SsrLightingTexture, blackHandle);
                return;
            }
            
            // Prepare SSR data
            PrepareSSRData(ref renderingData, true);
            PrepareVariables(ref renderingData.cameraData);

            // Determine async compute usage
            bool useAsyncCompute = _reprojectInCS && _tracingInCS && _needAccumulate 
                                   && IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute;
            
            // Get input textures from renderer
            TextureHandle depthStencilTexture = frameData.GetDepthWriteTextureHandle();
            TextureHandle normalTexture = resource.cameraNormalsTexture;
            TextureHandle depthPyramidTexture = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            TextureHandle motionVectorTexture = resource.motionVectorColor;
            
            // Get previous frame color pyramid
            var preFrameColorRT = _rendererData.GetPreviousFrameColorRT(frameData, out bool isNewFrame);
            TextureHandle colorPyramidTexture = renderGraph.ImportTexture(preFrameColorRT);
            
            // Set global textures for SSR
            if (!isNewFrame)
            {
                motionVectorTexture = renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());
            }
            
            CoreUtils.SetKeyword(_computeShader, "SSR_APPROX", !_needAccumulate);
            
            // Create transient textures for hit points and lighting
            TextureHandle hitPointTexture = renderGraph.CreateTexture(new TextureDesc(_rtWidth, _rtHeight)
            {
                colorFormat = GraphicsFormat.R16G16_UNorm,
                clearBuffer = !useAsyncCompute,
                clearColor = Color.clear,
                enableRandomWrite = _tracingInCS,
                name = "SSR_HitPoint_Texture"
            });

            TextureHandle finalResult;
            if (_tracingInCS && _reprojectInCS)
            {
                var ssrAccumRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                TextureHandle ssrAccum = renderGraph.ImportTexture(ssrAccumRT);
                
                var ssrAccumPrevRT = _rendererData.GetPreviousFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                TextureHandle ssrAccumPrev = renderGraph.ImportTexture(ssrAccumPrevRT);
                
                finalResult = RenderSSRComputePass(renderGraph, hitPointTexture, depthStencilTexture,
                    normalTexture, depthPyramidTexture, colorPyramidTexture, motionVectorTexture,
                    ssrAccum, ssrAccumPrev, useAsyncCompute, isNewFrame);
            }
            else
            {
                var ssrAccumRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                TextureHandle ssrAccum = renderGraph.ImportTexture(ssrAccumRT);
                ClearTexturePass(renderGraph, ssrAccum, Color.clear);
                    
                if (_previousAccumNeedClear)
                {
                    var ssrAccumPrevRT = _rendererData.GetPreviousFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                    TextureHandle ssrAccumPrev = renderGraph.ImportTexture(ssrAccumPrevRT);
                    ClearTexturePass(renderGraph, ssrAccumPrev, Color.clear);
                }
                
                // Execute tracing pass
                var tracedHitPoint = RenderTracingRasterPass(renderGraph, hitPointTexture, depthStencilTexture, 
                    normalTexture, depthPyramidTexture);

                // Execute reprojection pass
                var reprojectedResult = RenderReprojectionRasterPass(renderGraph, tracedHitPoint, colorPyramidTexture,
                    motionVectorTexture, normalTexture, ssrAccum);

                // Execute accumulation pass for PBR mode
                if (_needAccumulate)
                {
                    var ssrAccumPrevRT = _rendererData.GetPreviousFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
                    TextureHandle ssrAccumPrev = renderGraph.ImportTexture(ssrAccumPrevRT);
                    
                    finalResult = RenderAccumulationPass(renderGraph, tracedHitPoint, colorPyramidTexture, motionVectorTexture,
                        ssrAccum, ssrAccumPrev, false);
                }
                else
                {
                    finalResult = reprojectedResult;
                }
            }

            _rendererData.ScreenSpaceReflectionTexture = finalResult;
            if (!useAsyncCompute)
            {
                // Set global texture for SSR result
                RenderGraphUtils.SetGlobalTexture(renderGraph, IllusionShaderProperties.SsrLightingTexture, finalResult);
            }
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
            
            // public static readonly int SsrLightingTextureRW = Shader.PropertyToID("_SsrLightingTextureRW");
            
            public static readonly int ShaderVariablesScreenSpaceReflection = MemberNameHelpers.ShaderPropertyID();
        }
    }
}