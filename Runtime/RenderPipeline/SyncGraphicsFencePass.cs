using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute) return;
            
            // pass
        }
    }
}