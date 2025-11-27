using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Pass to set up global variables, history color texture and motion vector.
    /// </summary>
    public class SetGlobalVariablesPass : ScriptableRenderPass
    {
        private readonly IllusionRendererData _rendererData;
        
        public SetGlobalVariablesPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.SetGlobalVariablesPass;
            profilingSampler = new ProfilingSampler("Set Global Variables");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                _rendererData.PushGlobalBuffers(cmd, ref renderingData);
                _rendererData.BindGlobalTextures(cmd, ref renderingData);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}