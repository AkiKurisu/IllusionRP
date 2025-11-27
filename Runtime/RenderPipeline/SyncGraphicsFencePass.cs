using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class SyncGraphicsFencePass: ScriptableRenderPass
    {
        private readonly IllusionGraphicsFenceEvent _syncFenceEvent;

        private readonly IllusionRendererData _rendererData;
        
        public SyncGraphicsFencePass(RenderPassEvent evt, IllusionGraphicsFenceEvent syncFenceEvent, IllusionRendererData rendererData)
        {
            renderPassEvent = evt;
            _syncFenceEvent = syncFenceEvent;
            _rendererData = rendererData;
            profilingSampler = new ProfilingSampler($"Sync GraphicsFence {syncFenceEvent.ToString()}");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute) return;
            
            using (new ProfilingScope(renderingData.commandBuffer, profilingSampler))
            {
                _rendererData.WaitOnAsyncGraphicsFence(renderingData.commandBuffer, _syncFenceEvent);
            }
        }
    }
}