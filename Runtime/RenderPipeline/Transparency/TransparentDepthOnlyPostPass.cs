using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Renders transparent post-depth with forward GBuffer (smoothness) and depth only (no camera normals).
    /// </summary>
    public class TransparentDepthOnlyPostPass : ScriptableRenderPass, IDisposable
    {
        private const string DepthProfilerTag = "Transparent Depth Post (GBuffer + Depth)";

        private readonly FilteringSettings _filteringSettings;

        private readonly IllusionRendererData _rendererData;

        private static readonly List<ShaderTagId> ShaderTagIds = new()
        {
            new ShaderTagId("PostDepthOnly")
        };

        public TransparentDepthOnlyPostPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.TransparentDepthOnlyPrePass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all);
            profilingSampler = new ProfilingSampler("Transparent Post Depth");
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private class PassData
        {
            internal RendererListHandle RendererList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
#if UNITY_EDITOR
            if (cameraData.cameraType == CameraType.Preview)
                return;
#endif

            TextureHandle depthTexture = resource.cameraDepthTexture;
            if (!depthTexture.IsValid())
                return;

            TextureHandle forwardGBufferHandle = renderGraph.ImportTexture(_rendererData.ForwardGBufferRT);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(DepthProfilerTag, out var passData, profilingSampler))
            {
                builder.SetRenderAttachment(forwardGBufferHandle, 0);
                builder.SetRenderAttachmentDepth(depthTexture);

                var drawSettings = UniversalRenderingUtility.CreateDrawingSettings(ShaderTagIds, frameData, cameraData.defaultOpaqueSortFlags);
                var rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, _filteringSettings);
                passData.RendererList = renderGraph.CreateRendererList(rendererListParams);
                builder.UseRendererList(passData.RendererList);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetGlobalTextureAfterPass(forwardGBufferHandle, IllusionShaderProperties._ForwardGBuffer);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.RendererList);
                });
            }
        }

        public void Dispose()
        {
            // pass
        }
    }
}
