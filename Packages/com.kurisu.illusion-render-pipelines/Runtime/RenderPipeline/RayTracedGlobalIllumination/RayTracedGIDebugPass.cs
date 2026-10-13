#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    internal sealed class RayTracedGIDebugPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;
        private readonly Material _copyMaterial;

        private sealed class PassData
        {
            internal TextureHandle Source;
            internal Material Material;
        }

        public RayTracedGIDebugPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            _copyMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal/CoreBlit");
            renderPassEvent = IllusionRenderPassEvent.FullScreenDebugPass;
            profilingSampler = new ProfilingSampler("Ray Traced GI Radiance Cache Debug View");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle source = _rendererData.RayTracedGIDebugTexture;
            if (!source.IsValid() || !resources.activeColorTexture.IsValid() || !_copyMaterial)
                return;

            using var builder = renderGraph.AddRasterRenderPass<PassData>("Ray Traced GI Radiance Cache Debug View", out var passData, profilingSampler);
            builder.UseTexture(source);
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            builder.AllowPassCulling(false);
            passData.Source = source;
            passData.Material = _copyMaterial;
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), data.Material, 0);
            });
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_copyMaterial);
        }
    }
}
#endif
