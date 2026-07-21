#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
            renderPassEvent = IllusionRenderPassEvent.FullScreenDebugPass;
            ConfigureInput(ScriptableRenderPassInput.Motion);
        }
        
        private class MotionVectorsDebugPassData
        {
            internal Material MotionVectorDebugMaterial;
            internal TextureHandle MotionVectorColor;
            internal Vector4 DebugParams;
        }

        private class FinalBlitPassData
        {
            internal TextureHandle Source;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var motionVectorFromResources = resources.motionVectorColor;
            TextureHandle colorTargetHandle = resources.cameraColor;
            var debugOutputDesc = renderGraph.GetTextureDesc(colorTargetHandle);
            debugOutputDesc.name = "Motion Vectors Debug Output";
            debugOutputDesc.msaaSamples = MSAASamples.None;
            debugOutputDesc.depthBufferBits = DepthBits.None;
            debugOutputDesc.clearBuffer = false;
            TextureHandle debugOutput = renderGraph.CreateTexture(debugOutputDesc);
            
            using (var builder = renderGraph.AddRasterRenderPass<MotionVectorsDebugPassData>("Motion Vectors Debug",
                out var passData, profilingSampler))
            {
                passData.MotionVectorDebugMaterial = _motionVectorDebugMaterial.Value;
                passData.DebugParams = new Vector4(
                    motionVectorFromResources.IsValid() ? 1.0f : 0.0f,
                    0.0f,
                    0.0f,
                    0.0f);
                if (motionVectorFromResources.IsValid())
                {
                    builder.UseTexture(motionVectorFromResources);
                    passData.MotionVectorColor = motionVectorFromResources;
                }
                
                builder.SetRenderAttachment(debugOutput, 0);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (MotionVectorsDebugPassData data, RasterGraphContext context) =>
                {
                    if (data.MotionVectorColor.IsValid())
                        data.MotionVectorDebugMaterial.SetTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                    data.MotionVectorDebugMaterial.SetVector("_DebugMotionVectorsParams", data.DebugParams);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.MotionVectorDebugMaterial, 0,
                        MeshTopology.Triangles, 3, 1);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<FinalBlitPassData>("Motion Vectors Debug Final Blit",
                out var passData, profilingSampler))
            {
                builder.UseTexture(debugOutput);
                passData.Source = debugOutput;
                builder.SetRenderAttachment(colorTargetHandle, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (FinalBlitPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), 0.0f, false);
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
