using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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
                _rendererData.BindHistoryColor(cmd, renderingData);
                _rendererData.BindAmbientProbe(cmd);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
               
#if UNITY_2023_1_OR_NEWER
        private class SetGlobalVariablesPassData
        {
            internal IllusionRendererData RendererData;
            
            internal RenderingData RenderingData;

            internal TextureHandle ActiveColor;
            
            internal TextureHandle PreviousFrameColor;

            internal TextureHandle MotionVectorColor;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddComputePass<SetGlobalVariablesPassData>("Set Global Variables", out var passData, profilingSampler))
            {
                UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
                TextureHandle cameraColor = renderer.activeColorTexture;
                builder.UseTexture(cameraColor);
                passData.ActiveColor = cameraColor;
                
                passData.RendererData = _rendererData;
                passData.RenderingData = renderingData;
                
                var previousFrameRT = _rendererData.GetPreviousFrameColorRT(renderingData.cameraData, out _);
                if (!previousFrameRT.IsValid()) previousFrameRT = _rendererData.GetBlackTextureRT();
                Assert.IsTrue(previousFrameRT.IsValid());
                
                passData.PreviousFrameColor = renderGraph.ImportTexture(previousFrameRT);
                frameResources.SetTexture(IllusionFrameResource.PreviousFrameColor, passData.PreviousFrameColor);
                builder.UseTexture(passData.PreviousFrameColor);
                
                var motionVectorColorRT = renderer.resources.GetTexture(UniversalResource.MotionVectorColor);
                passData.MotionVectorColor = motionVectorColorRT;
                builder.UseTexture(motionVectorColorRT);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (SetGlobalVariablesPassData data, ComputeGraphContext context) =>
                {
                    data.RendererData.PushGlobalBuffers(context.cmd, data.ActiveColor, ref data.RenderingData);
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._HistoryColorTexture, data.PreviousFrameColor);
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                    data.RendererData.BindAmbientProbe(context.cmd);
                });
            }
        }
#endif
    }
}