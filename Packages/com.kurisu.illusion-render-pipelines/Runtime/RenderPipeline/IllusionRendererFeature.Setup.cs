using System;
using Illusion.Rendering.PostProcessing;
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

            private readonly ProfilingSampler _fixedExposureSampler = new("Fixed Exposure");

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

            private class FixedExposurePassData
            {
                internal ComputeShader ExposureCS;
                internal int Kernel;
                internal Vector4 ExposureParams;
                internal Vector4 ExposureParams2;
                internal TextureHandle OutputTexture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph,  ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();
                
                _rendererFeature.PerformSetup(frameData, _rendererData);
                _rendererData.BindDitheredRNGData1SPP(renderGraph);

                if (_rendererData.CanRunFixedExposurePass())
                {
                    using var builder = renderGraph.AddComputePass<FixedExposurePassData>("Fixed Exposure", out var exposureData,
                        _fixedExposureSampler);
                    exposureData.ExposureCS = _rendererData.RuntimeResources.exposureCS;
                    exposureData.Kernel = exposureData.ExposureCS.FindKernel("KFixedExposure");
                    _rendererData.GetFixedExposureParameters(out exposureData.ExposureParams, out exposureData.ExposureParams2);

                    var exposureOutput = renderGraph.ImportTexture(_rendererData.GetFixedExposureOutputTexture());
                    builder.UseTexture(exposureOutput, AccessFlags.Write);
                    exposureData.OutputTexture = exposureOutput;
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (FixedExposurePassData data, ComputeGraphContext context) =>
                    {
                        context.cmd.SetComputeVectorParam(data.ExposureCS, ExposureShaderIDs._ExposureParams, data.ExposureParams);
                        context.cmd.SetComputeVectorParam(data.ExposureCS, ExposureShaderIDs._ExposureParams2, data.ExposureParams2);
                        context.cmd.SetComputeTextureParam(data.ExposureCS, data.Kernel, ExposureShaderIDs._OutputTexture, data.OutputTexture);
                        context.cmd.DispatchCompute(data.ExposureCS, data.Kernel, 1, 1, 1);
                    });
                }
                
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
                    if (previousFrameRT == null || !previousFrameRT.IsValid()) previousFrameRT = _rendererData.GetBlackTextureRT();
                    
                    passData.PreviousFrameColor = renderGraph.ImportTexture(previousFrameRT);
                    builder.UseTexture(passData.PreviousFrameColor);
                    
                    var motionVectorColorRT = resource.motionVectorColor;
                    passData.MotionVectorColor = motionVectorColorRT;
                    builder.UseTexture(motionVectorColorRT);

                    passData.CurrentExposureTexture = renderGraph.ImportTexture(_rendererData.GetExposureTexture());
                    builder.UseTexture(passData.CurrentExposureTexture);
                    passData.PreviousExposureTexture = renderGraph.ImportTexture(_rendererData.GetPreviousExposureTexture());
                    builder.UseTexture(passData.PreviousExposureTexture);
                    
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (SetGlobalVariablesPassData data, RasterGraphContext context) =>
                    {
                        bool yFlip = RenderingUtils.IsHandleYFlipped(context, in data.ActiveColor);
                        data.RendererData.PushGlobalBuffers(context.cmd, data.CameraData, data.LightData, yFlip);
                        context.cmd.SetGlobalTexture(IllusionShaderProperties._HistoryColorTexture, data.PreviousFrameColor);
                        context.cmd.SetGlobalTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
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

