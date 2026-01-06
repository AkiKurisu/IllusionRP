#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    internal class MotionVectorsDebugPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _motionVectorDebugMaterial = new(IllusionShaders.DebugMotionVectors);
        
        public MotionVectorsDebugPass(IllusionRendererData rendererData)
        {
            profilingSampler = new ProfilingSampler("Motion Vectors Debug");
            renderPassEvent = IllusionRenderPassEvent.MotionVectorDebugPass;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            var motionVectorColorRT = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);
            if (!motionVectorColorRT.IsValid()) return;
            var colorRT = cameraData.renderer.cameraColorTargetHandle;
            var material = _motionVectorDebugMaterial.Value;
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetRenderTarget(colorRT);
                material.SetTexture(IllusionShaderProperties._MotionVectorTexture, motionVectorColorRT);
                cmd.Blit( colorRT, colorRT, material);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

#if UNITY_2023_1_OR_NEWER
        private class MotionVectorsDebugPassData
        {
            internal Material MotionVectorDebugMaterial;
            internal TextureHandle MotionVectorColor;
            internal TextureHandle ColorDestination;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources,
            ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            UniversalRenderer renderer = (UniversalRenderer)cameraData.renderer;

            // Try to get motion vector texture from renderer resources first (preferred for RenderGraph)
            TextureHandle motionVectorColorHandle = TextureHandle.nullHandle;
            var motionVectorFromResources = renderer.resources.GetTexture(UniversalResource.MotionVectorColor);
            if (!motionVectorFromResources.IsValid()) return;

            
            TextureHandle colorTargetHandle = renderer.activeColorTexture;
            
            using (var builder = renderGraph.AddRasterRenderPass<MotionVectorsDebugPassData>("Motion Vectors Debug", 
                out var passData, profilingSampler))
            {
                passData.MotionVectorDebugMaterial = _motionVectorDebugMaterial.Value;
                passData.MotionVectorColor = builder.UseTexture(motionVectorColorHandle);
                passData.ColorDestination = builder.UseTextureFragment(colorTargetHandle, 0);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (MotionVectorsDebugPassData data, RasterGraphContext context) =>
                {
                    data.MotionVectorDebugMaterial.SetTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                    Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), data.MotionVectorDebugMaterial, 0);
                });
            }
        }
#endif

        public void Dispose()
        {
            _motionVectorDebugMaterial.DestroyCache();
        }
    }
}
#endif