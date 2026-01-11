using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class PreIntegratedFGDPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;

        private readonly PreIntegratedFGD.FGDIndex _index;

        private readonly RTHandle _rtHandle;

        public PreIntegratedFGDPass(IllusionRendererData rendererData, PreIntegratedFGD.FGDIndex fgdIndex)
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
            _rendererData = rendererData;
            _index = fgdIndex;
            _rtHandle = _rendererData.PreIntegratedFGD.Build(fgdIndex);
            profilingSampler = new ProfilingSampler("PreIntegrated FGD");
        }

        private class PreIntegratedFGDPassData
        {
            internal TextureHandle OutputTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Render the FGD texture if needed
            using (var builder = renderGraph.AddUnsafePass<PreIntegratedFGDPassData>("PreIntegrated FGD", out var passData, profilingSampler))
            {
                passData.OutputTexture = renderGraph.ImportTexture(_rtHandle);

                builder.UseTexture(passData.OutputTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PreIntegratedFGDPassData data, UnsafeGraphContext rgContext) =>
                {
                    var cmd = rgContext.cmd;
                    _rendererData.PreIntegratedFGD.RenderInit(cmd, data.OutputTexture, _index);
                    _rendererData.PreIntegratedFGD.Bind(cmd, data.OutputTexture, _index);
                });
            }
        }
        
        public void Dispose()
        {
            _rendererData.PreIntegratedFGD.Cleanup(_index);
        }
    }
}