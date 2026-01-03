using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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

#if UNITY_2023_1_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources,
                ref RenderingData renderingData)
            {
                using (new ProfilingScope((CommandBuffer)null, profilingSampler))
                {
                    _rendererFeature.PerformSetup(renderingData.cameraData.renderer, ref renderingData, _rendererData);
                }
            }
#else
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // pass
            } 
#endif

            public void Dispose()
            {
                // pass
            }
        }
    }
}

