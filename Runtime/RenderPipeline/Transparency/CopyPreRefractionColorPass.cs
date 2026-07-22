using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copies the opaque scene color for screen-space refraction.
    /// </summary>
    public sealed class CopyPreRefractionColorPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;
        private readonly Material _copyMaterial;
        private bool _enabled;

        private sealed class PassData
        {
            internal TextureHandle Source;
            internal Material CopyMaterial;
        }

        public CopyPreRefractionColorPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            _copyMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal/CoreBlit");
            renderPassEvent = IllusionRenderPassEvent.CopyPreRefractionColorPass;
            profilingSampler = new ProfilingSampler("Copy Pre-Refraction Color");
        }

        public void Setup(bool enabled)
        {
            _enabled = enabled;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle source = resources.activeColorTexture;
            if (!_enabled || !source.IsValid() || !_copyMaterial)
            {
                TextureHandle black = renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());
                RenderGraphUtils.SetGlobalTexture(renderGraph,
                    IllusionShaderProperties._PreRefractionColorTexture, black);
                return;
            }

            var descriptor = cameraData.cameraTargetDescriptor;
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.enableRandomWrite = false;

            RenderingUtils.ReAllocateHandleIfNeeded(ref _rendererData.PreRefractionColorRT, descriptor,
                FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PreRefractionColorTexture");
            TextureHandle destination = renderGraph.ImportTexture(_rendererData.PreRefractionColorRT);

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Copy Pre-Refraction Color", out var passData, profilingSampler);
            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0);
            builder.SetGlobalTextureAfterPass(destination, IllusionShaderProperties._PreRefractionColorTexture);
            builder.AllowPassCulling(false);

            passData.Source = source;
            passData.CopyMaterial = _copyMaterial;
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), data.CopyMaterial, 0);
            });
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_copyMaterial);
        }
    }
}
