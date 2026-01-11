#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    internal class MotionVectorsDebugPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _motionVectorDebugMaterial = new(IllusionShaders.DebugMotionVectors);
        
        public MotionVectorsDebugPass(IllusionRendererData rendererData)
        {
            profilingSampler = new ProfilingSampler("Motion Vectors Debug");
            renderPassEvent = IllusionRenderPassEvent.MotionVectorDebugPass;
            ConfigureInput(ScriptableRenderPassInput.Motion);
        }
        
        private class MotionVectorsDebugPassData
        {
            internal Material MotionVectorDebugMaterial;
            internal TextureHandle MotionVectorColor;
            internal TextureHandle ColorDestination;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var motionVectorFromResources = resources.motionVectorColor;
            if (!motionVectorFromResources.IsValid()) return;

            
            TextureHandle colorTargetHandle = resources.activeColorTexture;
            
            using (var builder = renderGraph.AddRasterRenderPass<MotionVectorsDebugPassData>("Motion Vectors Debug", 
                out var passData, profilingSampler))
            {
                passData.MotionVectorDebugMaterial = _motionVectorDebugMaterial.Value;
                builder.UseTexture(motionVectorFromResources);
                passData.MotionVectorColor = motionVectorFromResources;
                
                builder.SetRenderAttachment(colorTargetHandle, 0);
                passData.ColorDestination = colorTargetHandle;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (MotionVectorsDebugPassData data, RasterGraphContext context) =>
                {
                    data.MotionVectorDebugMaterial.SetTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                    Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), data.MotionVectorDebugMaterial, 0);
                });
            }
        }

        public void Dispose()
        {
            _motionVectorDebugMaterial.DestroyCache();
        }
    }
}
#endif