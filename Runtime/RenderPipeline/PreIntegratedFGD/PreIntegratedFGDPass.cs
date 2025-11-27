using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class PreIntegratedFGDPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;

        private readonly PreIntegratedFGD.FGDIndex _index;

        public PreIntegratedFGDPass(IllusionRendererData rendererData, PreIntegratedFGD.FGDIndex fgdIndex)
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
            _rendererData = rendererData;
            _index = fgdIndex;
            _rendererData.PreIntegratedFGD.Build(fgdIndex);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            _rendererData.PreIntegratedFGD.RenderInit(cmd, _index);
            _rendererData.PreIntegratedFGD.Bind(cmd, _index);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _rendererData.PreIntegratedFGD.Cleanup(_index);
        }
    }
}