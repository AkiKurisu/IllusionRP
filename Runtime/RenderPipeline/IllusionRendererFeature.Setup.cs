using System;
using UnityEngine;
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
                internal TextureHandle CurrentExposureTexture;
                internal TextureHandle PreviousExposureTexture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph,  ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();
                
                _rendererFeature.PerformSetup(frameData, _rendererData);
                _rendererData.BindDitheredRNGData1SPP(renderGraph);
                
                // Bind exposure textures immediately via Shader.SetGlobalTexture during recording.
                // This ensures exposure globals are set reliably regardless of render graph pass execution.
                // The render graph pass below also sets them via cmd.SetGlobalTexture as a standard path.
                var immediateExposureRT = _rendererData.GetExposureTexture();
                if (immediateExposureRT?.rt != null)
                    Shader.SetGlobalTexture(IllusionShaderProperties._ExposureTexture, immediateExposureRT.rt);
                var immediatePrevExposureRT = _rendererData.GetPreviousExposureTexture();
                if (immediatePrevExposureRT?.rt != null)
                    Shader.SetGlobalTexture(IllusionShaderProperties._PrevExposureTexture, immediatePrevExposureRT.rt);
                
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
                    
                    // Import exposure textures for global binding at frame start (before main rendering)
                    var currentExposureRT = _rendererData.GetExposureTexture();
                    passData.CurrentExposureTexture = renderGraph.ImportTexture(currentExposureRT);
                    builder.UseTexture(passData.CurrentExposureTexture);
                    
                    var previousExposureRT = _rendererData.GetPreviousExposureTexture();
                    passData.PreviousExposureTexture = renderGraph.ImportTexture(previousExposureRT);
                    builder.UseTexture(passData.PreviousExposureTexture);
                    
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (SetGlobalVariablesPassData data, RasterGraphContext context) =>
                    {
                        bool yFlip = RenderingUtils.IsHandleYFlipped(context, in data.ActiveColor);
                        data.RendererData.PushGlobalBuffers(context.cmd, data.CameraData, data.LightData, yFlip);
                        context.cmd.SetGlobalTexture(IllusionShaderProperties._HistoryColorTexture, data.PreviousFrameColor);
                        context.cmd.SetGlobalTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                        
                        // Set exposure globals at frame start so main rendering uses consistent pre-exposure
                        context.cmd.SetGlobalTexture(IllusionShaderProperties._ExposureTexture, data.CurrentExposureTexture);
                        context.cmd.SetGlobalTexture(IllusionShaderProperties._PrevExposureTexture, data.PreviousExposureTexture);
                        
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

