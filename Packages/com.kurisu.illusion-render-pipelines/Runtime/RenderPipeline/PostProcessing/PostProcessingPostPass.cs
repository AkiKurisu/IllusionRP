using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    public class PostProcessingPostPass : ScriptableRenderPass
    {
        private readonly IllusionRendererData _rendererData;
        
        public PostProcessingPostPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.PostProcessPostPass;
            profilingSampler = new ProfilingSampler("Post Processing Post");
        }

        private class PostProcessingPostPassData
        {
            internal IllusionRendererData RendererData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PostProcessingPostPassData>("Post Processing Post", out var passData, profilingSampler))
            {
                builder.AllowPassCulling(false);
                passData.RendererData = _rendererData;

                builder.SetRenderFunc(static (PostProcessingPostPassData data, UnsafeGraphContext _) =>
                {
                    data.RendererData.DidResetPostProcessingHistoryInLastFrame = data.RendererData.ResetPostProcessingHistory;

                    data.RendererData.ResetPostProcessingHistory = false;
                });
            }
        }
    }
}