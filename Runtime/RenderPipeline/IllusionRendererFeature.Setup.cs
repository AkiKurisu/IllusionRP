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
                renderPassEvent = RenderPassEvent.BeforeRendering;
                profilingSampler = new ProfilingSampler("Global Setup");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (new ProfilingScope((CommandBuffer)null, profilingSampler))
                {
                    var renderingData = new RenderingData(frameData);
                    _rendererFeature.PerformSetup(renderingData.cameraData.renderer, ref renderingData, _rendererData);
                }
            }
            
            public void Dispose()
            {
                // pass
            }
        }
    }
}

