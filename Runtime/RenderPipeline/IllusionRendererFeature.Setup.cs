using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public partial class IllusionRendererFeature
    {
        /// <summary>
        /// Setup pass that handles renderer configuration and setup logic.
        /// </summary>
        private class SetupPass : ScriptableRenderPass, IDisposable
        {
            private readonly IllusionRendererFeature _rendererFeature;
            
            private readonly IllusionRendererData _rendererData;

            private RenderingData _renderingData;

            public SetupPass(IllusionRendererFeature rendererFeature, IllusionRendererData rendererData)
            {
                _rendererFeature = rendererFeature;
                _rendererData = rendererData;
                renderPassEvent = IllusionRenderPassEvent.SetGlobalVariablesPass;
                profilingSampler = new ProfilingSampler("Global Setup");
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
                
                _rendererFeature.PerformSetup(frameData, _rendererData);
                _rendererData.BindDitheredRNGData1SPP(renderGraph);
                
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
            
            public void Dispose()
            {
                // pass
            }
        }
    }
}

