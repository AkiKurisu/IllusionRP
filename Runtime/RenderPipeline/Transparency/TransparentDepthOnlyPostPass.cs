using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    /// <summary>
    /// Render transparent objects depth only after opaque objects.
    /// </summary>
    public class TransparentDepthOnlyPostPass : ScriptableRenderPass, IDisposable
    {
        private const string DepthProfilerTag = "Transparent Post Depth";

        private FilteringSettings _filteringSettings;

        private RenderStateBlock _renderStateBlock;

        private static readonly ShaderTagId PostDepthNormalsTagId = new("PostDepthOnly");

        public TransparentDepthOnlyPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.TransparentDepthOnlyPostPass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            profilingSampler = new ProfilingSampler("Transparent Post Depth");
        }

#if !UNITY_2023_1_OR_NEWER
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private void DoDepthOnly(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var depthTexture = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            if (!depthTexture.IsValid()) return;
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.SetRenderTarget(depthTexture);
            context.ExecuteCommandBuffer(cmd);
            var drawSettings = RenderingUtils.CreateDrawingSettings(PostDepthNormalsTagId,
                ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
            context.DrawRenderers(renderingData.cullResults, ref drawSettings,
                ref _filteringSettings, ref _renderStateBlock);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(DepthProfilerTag)))
            {
                DoDepthOnly(cmd, context, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

#if UNITY_2023_1_OR_NEWER
        private class PassData
        {
            internal RendererListHandle RendererList;
            internal TextureHandle DepthTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif
            
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle depthTexture = renderer.activeDepthTexture;
            
            // If texture is not available in frameResources, fall back to activeDepthTexture
            if (!depthTexture.IsValid())
            {
                depthTexture = renderer.activeDepthTexture;
            }
            
            if (!depthTexture.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(DepthProfilerTag, out var passData, profilingSampler))
            {
                // Setup depth texture
                passData.DepthTexture = builder.UseTextureFragmentDepth(depthTexture);

                // Setup renderer list
                var drawSettings = RenderingUtils.CreateDrawingSettings(PostDepthNormalsTagId,
                    ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
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
#endif

        public void Dispose()
        {
            // pass
        }
    }
}