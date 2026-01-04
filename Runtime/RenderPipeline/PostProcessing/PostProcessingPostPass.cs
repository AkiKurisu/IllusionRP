using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _rendererData.DidResetPostProcessingHistoryInLastFrame = _rendererData.ResetPostProcessingHistory;

            _rendererData.ResetPostProcessingHistory = false;
        }
        
#if UNITY_2023_1_OR_NEWER
        private class PostProcessingPostPassData
        {
            internal IllusionRendererData RendererData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources,
            ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddLowLevelPass<PostProcessingPostPassData>("Post Processing Post", out var passData, profilingSampler))
            {
                builder.AllowPassCulling(false);
                passData.RendererData = _rendererData;

                builder.SetRenderFunc(static (PostProcessingPostPassData data, LowLevelGraphContext _) =>
                {
                    data.RendererData.DidResetPostProcessingHistoryInLastFrame = data.RendererData.ResetPostProcessingHistory;

                    data.RendererData.ResetPostProcessingHistory = false;
                });
            }
        }
#endif
    }
}