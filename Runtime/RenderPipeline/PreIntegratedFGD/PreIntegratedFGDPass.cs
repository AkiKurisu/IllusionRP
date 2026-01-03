using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    public class PreIntegratedFGDPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;

        private readonly PreIntegratedFGD.FGDIndex _index;

        private static readonly ProfilingSampler PreIntegratedFGDSampler = new("PreIntegrated FGD");

        private readonly RTHandle _rtHandle;

        public PreIntegratedFGDPass(IllusionRendererData rendererData, PreIntegratedFGD.FGDIndex fgdIndex)
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
            _rendererData = rendererData;
            _index = fgdIndex;
            _rtHandle = _rendererData.PreIntegratedFGD.Build(fgdIndex);
        }

#if UNITY_2023_1_OR_NEWER
        private class PreIntegratedFGDPassData
        {
            internal TextureHandle OutputTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            if (_rendererData.PreIntegratedFGD.IsInit(_index)) return;
            
            // Render the FGD texture if needed
            using (var builder = renderGraph.AddLowLevelPass<PreIntegratedFGDPassData>("PreIntegrated FGD", out var passData, PreIntegratedFGDSampler))
            {
                passData.OutputTexture = renderGraph.ImportTexture(_rtHandle);

                builder.UseTexture(passData.OutputTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PreIntegratedFGDPassData data, LowLevelGraphContext rgContext) =>
                {
                    var cmd = rgContext.cmd;
                    _rendererData.PreIntegratedFGD.RenderInit(cmd, data.OutputTexture, _index);
                    _rendererData.PreIntegratedFGD.Bind(cmd, data.OutputTexture, _index);
                });
            }
        }
#endif

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