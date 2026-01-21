using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class StencilVRSGenerationPass : ScriptableRenderPass, IDisposable
    {
        private TextureHandle _sirColorMask;
        
        private TextureHandle _sri;
        
        private readonly LazyMaterial _material = new("Hidden/StencilVRS");
        
        private const string PassName = "Stencil VRS Generation";

        public StencilVRSGenerationPass(RenderPassEvent passEvent)
        {
            profilingSampler = new ProfilingSampler(PassName);
            renderPassEvent = passEvent;
        }
        
        private class PassData
        {
            internal Material Material;
            internal TextureHandle StencilTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) 
        {
            if (!ShadingRateInfo.supportsPerImageTile) 
            {
                Debug.Log("VRS is not supported!");
                return;
            }

            // Get the render pipeline's conversion look-up table 
            var vrsPipelineResources = GraphicsSettings.GetRenderPipelineSettings<VrsRenderPipelineRuntimeResources>();
            var lut = vrsPipelineResources.conversionLookupTable;
            vrsPipelineResources.visualizationLookupTable = lut;

            var material = _material.Value;
            material.SetColor(Properties.ShadingRateColor1X1, lut[ShadingRateFragmentSize.FragmentSize1x1]);
            material.SetColor(Properties.ShadingRateColor2X2, lut[ShadingRateFragmentSize.FragmentSize2x2]);
            material.SetColor(Properties.ShadingRateColor4X4, lut[ShadingRateFragmentSize.FragmentSize4x4]);
            
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle depthStencilTexture = frameData.GetDepthWriteTextureHandle();
            
            var vrsData = frameData.Create<StencilVRSData>(); 
            var tileSize = ShadingRateImage.GetAllocTileSize(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var passData, profilingSampler))
            {
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                RenderTextureDescriptor textureProperties = new RenderTextureDescriptor(tileSize.x, tileSize.y, RenderTextureFormat.Default, 0);
                _sirColorMask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, textureProperties, "_ShadingRateColor", false);

                builder.SetRenderAttachment(_sirColorMask, 0);
                
                builder.UseTexture(depthStencilTexture);
                passData.StencilTexture = depthStencilTexture;
                
                vrsData.ShadingRateColorMask = _sirColorMask;
                passData.Material = material;
                
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RasterCommandBuffer cmd = context.cmd;
                    cmd.SetGlobalTexture(IllusionShaderProperties._StencilTexture, data.StencilTexture, RenderTextureSubElement.Stencil);
                    Blitter.BlitTexture(cmd, new Vector4(1, 1, 0,0), data.Material, 0);
                });

                //Create sri target
                RenderTextureDescriptor sriDesc = new RenderTextureDescriptor(tileSize.x, tileSize.y, ShadingRateInfo.graphicsFormat,
                    GraphicsFormat.None)
                {
                    enableRandomWrite = true,
                    enableShadingRate = true,
                    autoGenerateMips = false
                };

                _sri = UniversalRenderer.CreateRenderGraphTexture(renderGraph, sriDesc, "_SRI", false);
            }

            Vrs.ColorMaskTextureToShadingRateImage(renderGraph, _sri, _sirColorMask, TextureDimension.Tex2D, true);
            vrsData.ShadingRateImage = _sri;
        }

        public void Dispose()
        {
            _material.DestroyCache();
        }
        
        private static class Properties
        {
            public static readonly int ShadingRateColor1X1 = Shader.PropertyToID("_ShadingRateColor1x1");
        
            public static readonly int ShadingRateColor2X2 = Shader.PropertyToID("_ShadingRateColor2x2");
        
            public static readonly int ShadingRateColor4X4 = Shader.PropertyToID("_ShadingRateColor4x4");
        }
    }
}