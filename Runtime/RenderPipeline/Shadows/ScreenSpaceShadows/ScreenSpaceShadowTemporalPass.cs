using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Illusion.Rendering.RayTracing;

namespace Illusion.Rendering.Shadows
{
    public class ScreenSpaceShadowTemporalPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;
        private readonly ComputeShader _temporalFilterCS;
        private readonly DiffuseShadowDenoiser _diffuseShadowDenoiser;

        private readonly int _validateHistoryKernel;
        private readonly int _temporalAccumulationSingleKernel;
        private readonly int _copyHistoryKernel;

        private readonly ProfilingSampler _temporalSampler = new("Screen Space Shadow Temporal");
        private readonly ProfilingSampler _spatialSampler = new("Screen Space Shadow Spatial");

        private bool _hasDirectionalHistoryState;
        private Vector3 _lastMainLightDirection;
        private uint _lastHistoryFrameCount;

        private class TemporalPassData
        {
            public ComputeShader TemporalFilterCS;
            public int ValidateHistoryKernel;
            public int TemporalAccumulationKernel;
            public int CopyHistoryKernel;
            public int Width;
            public int Height;
            public int ViewCount;
            public float HistoryValidity;
            public float PixelSpreadAngleTangent;
            public Vector4 HistorySizeAndScale;
            public Vector4 ResolutionMultiplier;
            public TextureHandle DepthTexture;
            public TextureHandle HistoryDepthTexture;
            public TextureHandle NormalTexture;
            public TextureHandle HistoryNormalTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle InputShadowTexture;
            public TextureHandle HistoryShadowTexture;
            public TextureHandle ValidationTexture;
            public TextureHandle TemporalOutputTexture;
            public TextureHandle FinalShadowSignalTexture;
        }

        private class SetGlobalShadowTexturePassData
        {
            public TextureHandle ShadowTexture;
        }

        private static class ShaderProperties
        {
            public static readonly int EnableExposureControl = Shader.PropertyToID("_EnableExposureControl");
            public static readonly int DenoiserResolutionMultiplierVals = Shader.PropertyToID("_DenoiserResolutionMultiplierVals");
        }

        public ScreenSpaceShadowTemporalPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceShadowsPass;
            profilingSampler = _temporalSampler;
            _temporalFilterCS = rendererData.RuntimeResources.temporalFilterCS;
            _validateHistoryKernel = _temporalFilterCS.FindKernel("ValidateHistory");
            _temporalAccumulationSingleKernel = _temporalFilterCS.FindKernel("TemporalAccumulationSingle");
            _copyHistoryKernel = _temporalFilterCS.FindKernel("CopyHistory");
            _diffuseShadowDenoiser = new DiffuseShadowDenoiser(rendererData.RuntimeResources.diffuseShadowDenoiserCS);
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Motion);
        }

        public void Dispose()
        {
            // No owned RTHandles in this pass.
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!_rendererData.PCSSShadowSampling || _rendererData.ScreenSpaceShadowsRT == null || !_rendererData.ScreenSpaceShadowsRT.IsValid())
            {
                return;
            }

            var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
            if (!pcssParams.shadowTemporalAccumulation.value)
            {
                return;
            }

            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();

            TextureHandle sourceShadowTexture = renderGraph.ImportTexture(_rendererData.ScreenSpaceShadowsRT);
            TextureHandle historyShadowTexture = ImportOrAllocateShadowHistory(renderGraph);
            if (!historyShadowTexture.IsValid())
            {
                return;
            }
            _ = ImportOrAllocateShadowHistoryValidity(renderGraph);

            var historyDepthRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Depth);
            TextureHandle historyDepthTexture = historyDepthRT != null && historyDepthRT.IsValid()
                ? renderGraph.ImportTexture(historyDepthRT)
                : resource.cameraDepthTexture;

            var historyNormalRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Normal);
            TextureHandle historyNormalTexture = historyNormalRT != null && historyNormalRT.IsValid()
                ? renderGraph.ImportTexture(historyNormalRT)
                : resource.cameraNormalsTexture;

            Vector4 historySizeAndScale = historyDepthRT != null && historyDepthRT.IsValid()
                ? _rendererData.EvaluateRayTracingHistorySizeAndScale(historyDepthRT)
                : Vector4.one;

            int width = cameraData.cameraTargetDescriptor.width;
            int height = cameraData.cameraTargetDescriptor.height;
            int viewCount = IllusionRendererData.MaxViewCount;

            float historyValidity = EvaluateHistoryValidity(lightData);
            float pixelSpreadAngleTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, width, height);
            Vector4 resolutionMultiplier = Vector4.one;

            TextureHandle validationTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R8_UInt,
                enableRandomWrite = true,
                clearBuffer = true,
                clearColor = Color.black,
                name = "Shadow Temporal Validation"
            });

            TextureHandle temporalOutputTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16_SFloat,
                enableRandomWrite = true,
                name = "Screen Space Shadow Temporal Output"
            });
            TextureHandle finalShadowSignalTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16_SFloat,
                enableRandomWrite = true,
                name = "Screen Space Shadow Temporal Signal"
            });

            using (var builder = renderGraph.AddComputePass<TemporalPassData>("Screen Space Shadow Temporal", out var passData, _temporalSampler))
            {
                passData.TemporalFilterCS = _temporalFilterCS;
                passData.ValidateHistoryKernel = _validateHistoryKernel;
                passData.TemporalAccumulationKernel = _temporalAccumulationSingleKernel;
                passData.CopyHistoryKernel = _copyHistoryKernel;
                passData.Width = width;
                passData.Height = height;
                passData.ViewCount = viewCount;
                passData.HistoryValidity = historyValidity;
                passData.PixelSpreadAngleTangent = pixelSpreadAngleTangent;
                passData.HistorySizeAndScale = historySizeAndScale;
                passData.ResolutionMultiplier = resolutionMultiplier;
                passData.DepthTexture = resource.cameraDepthTexture;
                passData.HistoryDepthTexture = historyDepthTexture;
                passData.NormalTexture = resource.cameraNormalsTexture;
                passData.HistoryNormalTexture = historyNormalTexture;
                passData.MotionVectorTexture = resource.motionVectorColor;
                passData.InputShadowTexture = sourceShadowTexture;
                passData.HistoryShadowTexture = historyShadowTexture;
                passData.ValidationTexture = validationTexture;
                passData.TemporalOutputTexture = temporalOutputTexture;
                passData.FinalShadowSignalTexture = finalShadowSignalTexture;

                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.HistoryDepthTexture);
                builder.UseTexture(passData.NormalTexture);
                builder.UseTexture(passData.HistoryNormalTexture);
                builder.UseTexture(passData.MotionVectorTexture);
                builder.UseTexture(passData.InputShadowTexture);
                builder.UseTexture(passData.HistoryShadowTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ValidationTexture, AccessFlags.Write);
                builder.UseTexture(passData.TemporalOutputTexture, AccessFlags.Write);
                builder.UseTexture(passData.FinalShadowSignalTexture, AccessFlags.Write);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((TemporalPassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel, RayTracingShaderProperties.DepthTexture, data.DepthTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel, RayTracingShaderProperties.HistoryDepthTexture, data.HistoryDepthTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel, RayTracingShaderProperties.NormalBufferTexture, data.NormalTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel, RayTracingShaderProperties.HistoryNormalTexture, data.HistoryNormalTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel, IllusionShaderProperties._MotionVectorTexture, data.MotionVectorTexture);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, RayTracingShaderProperties.HistoryValidity, data.HistoryValidity);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, RayTracingShaderProperties.PixelSpreadAngleTangent, data.PixelSpreadAngleTangent);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, RayTracingShaderProperties.HistorySizeAndScale, data.HistorySizeAndScale);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel, RayTracingShaderProperties.ValidationBufferRW, data.ValidationTexture);

                    int tilesX = IllusionRenderingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = IllusionRenderingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.ValidateHistoryKernel, tilesX, tilesY, data.ViewCount);

                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel, RayTracingShaderProperties.DenoiseInputTexture, data.InputShadowTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel, RayTracingShaderProperties.HistoryBuffer, data.HistoryShadowTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel, RayTracingShaderProperties.DepthTexture, data.DepthTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel, RayTracingShaderProperties.ValidationBuffer, data.ValidationTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel, IllusionShaderProperties._MotionVectorTexture, data.MotionVectorTexture);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, RayTracingShaderProperties.HistoryValidity, data.HistoryValidity);
                    context.cmd.SetComputeIntParam(data.TemporalFilterCS, RayTracingShaderProperties.ReceiverMotionRejection, 1);
                    context.cmd.SetComputeIntParam(data.TemporalFilterCS, RayTracingShaderProperties.OccluderMotionRejection, 0);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, RayTracingShaderProperties.PixelSpreadAngleTangent, data.PixelSpreadAngleTangent);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, ShaderProperties.DenoiserResolutionMultiplierVals, data.ResolutionMultiplier);
                    context.cmd.SetComputeIntParam(data.TemporalFilterCS, ShaderProperties.EnableExposureControl, 0);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel, RayTracingShaderProperties.AccumulationOutputTextureRW, data.TemporalOutputTexture);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.TemporalAccumulationKernel, tilesX, tilesY, data.ViewCount);

                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.CopyHistoryKernel, RayTracingShaderProperties.DenoiseInputTexture, data.TemporalOutputTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.CopyHistoryKernel, RayTracingShaderProperties.DenoiseOutputTextureRW, data.HistoryShadowTexture);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, ShaderProperties.DenoiserResolutionMultiplierVals, data.ResolutionMultiplier);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.CopyHistoryKernel, tilesX, tilesY, data.ViewCount);

                    // Extract single-channel shadow signal for downstream passes.
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.CopyHistoryKernel, RayTracingShaderProperties.DenoiseInputTexture, data.TemporalOutputTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.CopyHistoryKernel, RayTracingShaderProperties.DenoiseOutputTextureRW, data.FinalShadowSignalTexture);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, ShaderProperties.DenoiserResolutionMultiplierVals, data.ResolutionMultiplier);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.CopyHistoryKernel, tilesX, tilesY, data.ViewCount);
                });
            }

            TextureHandle finalShadowTexture = finalShadowSignalTexture;
            if (pcssParams.shadowSpatialDenoise.value)
            {
                TextureHandle denoiseIntermediate = renderGraph.CreateTexture(new TextureDesc(width, height)
                {
                    colorFormat = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "Screen Space Shadow Spatial Intermediate"
                });

                TextureHandle denoiseOutput = renderGraph.CreateTexture(new TextureDesc(width, height)
                {
                    colorFormat = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "Screen Space Shadow Spatial Output"
                });

                float lightAngleRadians = pcssParams.angularDiameter.value * Mathf.Deg2Rad;
                float cameraFovRadians = cameraData.camera.fieldOfView * Mathf.Deg2Rad;
                _diffuseShadowDenoiser.DenoiseBuffer(renderGraph,
                    resource.cameraDepthTexture, resource.cameraNormalsTexture,
                    finalShadowSignalTexture, denoiseIntermediate, denoiseOutput,
                    width, height, viewCount,
                    lightAngleRadians, cameraFovRadians, pcssParams.shadowDenoiseKernelSize.value,
                    _spatialSampler);

                finalShadowTexture = denoiseOutput;
            }

            using (var builder = renderGraph.AddComputePass<SetGlobalShadowTexturePassData>("Bind Screen Space Shadow Texture", out var passData))
            {
                passData.ShadowTexture = finalShadowTexture;
                builder.UseTexture(passData.ShadowTexture);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((SetGlobalShadowTexturePassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(IllusionShaderProperties.ScreenSpaceShadowmapTexture, data.ShadowTexture);
                });
            }
        }

        private TextureHandle ImportOrAllocateShadowHistory(RenderGraph renderGraph)
        {
            var historyRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceShadowHistory);
            if (historyRT == null)
            {
                var allocator = new IllusionRendererData.CustomHistoryAllocator(
                    Vector2.one,
                    GraphicsFormat.R16G16_SFloat,
                    "ScreenSpaceShadowHistory");
                historyRT = _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceShadowHistory, allocator.Allocator, 1);
            }

            return historyRT != null && historyRT.IsValid() ? renderGraph.ImportTexture(historyRT) : TextureHandle.nullHandle;
        }

        private TextureHandle ImportOrAllocateShadowHistoryValidity(RenderGraph renderGraph)
        {
            var historyRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceShadowHistoryValidity);
            if (historyRT == null)
            {
                var allocator = new IllusionRendererData.CustomHistoryAllocator(
                    Vector2.one,
                    GraphicsFormat.R16_SFloat,
                    "ScreenSpaceShadowHistoryValidity");
                historyRT = _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceShadowHistoryValidity, allocator.Allocator, 1);
            }

            return historyRT != null && historyRT.IsValid() ? renderGraph.ImportTexture(historyRT) : TextureHandle.nullHandle;
        }

        private float EvaluateHistoryValidity(UniversalLightData lightData)
        {
            float historyValidity = 1.0f;

            int mainLightIndex = lightData.mainLightIndex;
            if (mainLightIndex < 0 || mainLightIndex >= lightData.visibleLights.Length || lightData.visibleLights[mainLightIndex].light == null)
            {
                _hasDirectionalHistoryState = false;
                return 0.0f;
            }

            Vector3 currentMainLightDirection = lightData.visibleLights[mainLightIndex].light.transform.forward;
            bool lightDirectionChanged = !_hasDirectionalHistoryState
                                         || (currentMainLightDirection - _lastMainLightDirection).sqrMagnitude > 1e-6f;
            bool nonConsecutiveFrame = !_hasDirectionalHistoryState || (_lastHistoryFrameCount + 1) != _rendererData.FrameCount;
            bool invalidByFrame = _rendererData.IsFirstFrame || nonConsecutiveFrame;

            if (lightDirectionChanged || invalidByFrame)
            {
                historyValidity = 0.0f;
            }

            _lastMainLightDirection = currentMainLightDirection;
            _lastHistoryFrameCount = _rendererData.FrameCount;
            _hasDirectionalHistoryState = true;
            return historyValidity;
        }

        private static float GetPixelSpreadTangent(float fov, int width, int height)
        {
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) / (height * 0.5f);
        }
    }
}
