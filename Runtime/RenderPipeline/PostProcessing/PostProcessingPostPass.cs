using UnityEngine.Rendering;
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
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _rendererData.DidResetPostProcessingHistoryInLastFrame = _rendererData.ResetPostProcessingHistory;

            _rendererData.ResetPostProcessingHistory = false;
        }
    }
}