using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class StencilVRSDebugPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _material = new("Hidden/DebugStencilVRS");
        
        private const string PassName = "Stencil VRS Debugging";

        public StencilVRSDebugPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            profilingSampler = new ProfilingSampler(PassName);
        }
        
        private class PassData
        {
            public Material Material;
            public TextureHandle ShadingRateTex;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) 
        {
            if (!ShadingRateInfo.supportsPerImageTile) return;


            var resourceData = frameData.Get<UniversalResourceData>();
            var vrsData = frameData.Get<StencilVRSData>();
            
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var passData, profilingSampler))
            {
                builder.UseTexture(vrsData.ShadingRateColorMask);
                passData.ShadingRateTex = vrsData.ShadingRateColorMask; 

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                passData.Material = _material.Value;
                
                builder.AllowPassCulling(false); 

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RasterCommandBuffer cmd = context.cmd;
                    Blitter.BlitTexture(cmd, data.ShadingRateTex, new Vector4(1, 1, 0, 0), data.Material, 0);
                });
            }
        }

        public void Dispose()
        {
            _material.DestroyCache();
        }
    }
}