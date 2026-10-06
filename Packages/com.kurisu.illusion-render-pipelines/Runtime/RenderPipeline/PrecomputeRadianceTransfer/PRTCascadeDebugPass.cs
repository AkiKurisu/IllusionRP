#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    /// <summary>
    /// Colors each pixel by the PRT camera cascades that light it.
    /// </summary>
    internal sealed class PRTCascadeDebugPass : ScriptableRenderPass, IDisposable
    {
        private static readonly int SourceTexture = Shader.PropertyToID("_SourceTexture");
        private static readonly int DepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        private readonly LazyMaterial _material = new(IllusionShaders.DebugPRTCascades);

        public PRTCascadeDebugPass()
        {
            profilingSampler = new ProfilingSampler("PRT Cascades Debug");
            renderPassEvent = IllusionRenderPassEvent.FullScreenDebugPass;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private class PassData
        {
            internal Material Material;
            internal TextureHandle Source, Depth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle color = resources.cameraColor;
            var description = renderGraph.GetTextureDesc(color);
            description.name = "PRT Cascades Debug Source";
            description.clearBuffer = false;
            TextureHandle source = renderGraph.CreateTexture(description);
            renderGraph.AddCopyPass(color, source, "PRT Cascades Debug Copy");

            using var builder = renderGraph.AddRasterRenderPass<PassData>("PRT Cascades Debug", out var passData, profilingSampler);
            passData.Material = _material.Value;
            passData.Source = source;
            passData.Depth = resources.cameraDepthTexture;
            builder.UseTexture(source);
            builder.UseTexture(passData.Depth);
            builder.SetRenderAttachment(color, 0);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                data.Material.SetTexture(SourceTexture, data.Source);
                data.Material.SetTexture(DepthTexture, data.Depth);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3, 1);
            });
        }

        public void Dispose() => _material.DestroyCache();
    }
}
#endif
