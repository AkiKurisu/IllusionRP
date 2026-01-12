using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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

        private class SetGlobalVariablesPassData
        {
            internal IllusionRendererData RendererData;
            internal UniversalCameraData CameraData;
            internal UniversalLightData LightData;
            internal TextureHandle ActiveColor;
            internal TextureHandle PreviousFrameColor;
            internal TextureHandle MotionVectorColor;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph,  ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            using (var builder = renderGraph.AddRasterRenderPass<SetGlobalVariablesPassData>("Set Global Variables", out var passData, profilingSampler))
            {
                var resource = frameData.Get<UniversalResourceData>();
                TextureHandle cameraColor = resource.activeColorTexture;
                builder.UseTexture(cameraColor);
                passData.ActiveColor = cameraColor;
                
                passData.RendererData = _rendererData;
                passData.CameraData = cameraData;
                passData.LightData = lightData;
                
                var previousFrameRT = _rendererData.GetPreviousFrameColorRT(frameData, out _);
                if (!previousFrameRT.IsValid()) previousFrameRT = _rendererData.GetBlackTextureRT();
                
                passData.PreviousFrameColor = renderGraph.ImportTexture(previousFrameRT);
                builder.UseTexture(passData.PreviousFrameColor);
                
                var motionVectorColorRT = resource.motionVectorColor;
                passData.MotionVectorColor = motionVectorColorRT;
                builder.UseTexture(motionVectorColorRT);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (SetGlobalVariablesPassData data, RasterGraphContext context) =>
                {
                    bool yFlip = RenderingUtils.IsHandleYFlipped(context, in data.ActiveColor);
                    data.RendererData.PushGlobalBuffers(context.cmd, data.CameraData, data.LightData, yFlip);
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._HistoryColorTexture, data.PreviousFrameColor);
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                    data.RendererData.BindAmbientProbe(context.cmd);
                });
            }
        }
    }
}