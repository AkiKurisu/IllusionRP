using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Render transparent objects depth only after opaque objects.
    /// </summary>
    public class TransparentDepthOnlyPostPass : ScriptableRenderPass, IDisposable
    {
        private const string DepthProfilerTag = "Transparent Post Depth";

        private readonly FilteringSettings _filteringSettings;

        private static readonly ShaderTagId PostDepthNormalsTagId = new("PostDepthOnly");

        public TransparentDepthOnlyPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.TransparentDepthOnlyPostPass;
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
            if (!depthTexture.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(DepthProfilerTag, out var passData, profilingSampler))
            {
                // Setup depth texture
                builder.SetRenderAttachmentDepth(depthTexture);

                // Setup renderer list
                var drawSettings = UniversalRenderingUtility.CreateDrawingSettings(PostDepthNormalsTagId, frameData, cameraData.defaultOpaqueSortFlags);
                var rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, _filteringSettings);
                passData.RendererList = renderGraph.CreateRendererList(rendererListParams);
                builder.UseRendererList(passData.RendererList);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

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