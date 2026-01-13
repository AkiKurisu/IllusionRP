using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class TransparentDepthNormalPostPass : ScriptableRenderPass, IDisposable
    {
        private const string DepthProfilerTag = "Transparent Post Depth Normal";

        private readonly FilteringSettings _filteringSettings;

        private static readonly ShaderTagId PostDepthNormalsTagId = new("PostDepthNormals");

        public TransparentDepthNormalPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.TransparentDepthNormalPostPass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all);
            profilingSampler = new ProfilingSampler("Transparent Post Depth Normal");
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
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
            
            TextureHandle depthTexture = frameData.GetDepthWriteTextureHandle();
            TextureHandle normalTexture = resource.cameraNormalsTexture;
            
            if (!depthTexture.IsValid() || !normalTexture.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(DepthProfilerTag, out var passData, profilingSampler))
            {
                // Setup normal and depth textures
                builder.SetRenderAttachment(normalTexture, 0);
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