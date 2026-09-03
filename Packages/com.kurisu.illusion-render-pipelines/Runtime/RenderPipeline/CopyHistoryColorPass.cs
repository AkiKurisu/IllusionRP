using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy history color to history color texture.
    /// </summary>
    public class CopyHistoryColorPass : CopyColorPass, IDisposable
    {
        private readonly Material _blitMaterial;

        private readonly Material _samplingMaterial;

        private readonly IllusionRendererData _rendererData;

        private CopyHistoryColorPass(IllusionRendererData rendererData, Material samplingMaterial, Material copyColorMaterial)
            : base(RenderPassEvent.BeforeRenderingPostProcessing - 1, samplingMaterial, copyColorMaterial)
        {
            _rendererData = rendererData;
            _samplingMaterial = samplingMaterial;
            _blitMaterial = copyColorMaterial;
        }

        public static CopyHistoryColorPass Create(IllusionRendererData rendererData)
        {
            var blitMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal/CoreBlit");
            var samplingMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/Sampling");
            return new CopyHistoryColorPass(rendererData, samplingMaterial, blitMaterial);
        }

        private class PassData
        {
            internal TextureHandle Source;
            internal TextureHandle Destination;
            internal Material CopyColorMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle cameraColor = resource.activeColorTexture;

            // Allocate history color texture
            var descriptor = cameraData.cameraTargetDescriptor;
            ConfigureDescriptor(Downsampling.None, ref descriptor, out var filterMode);
            RenderingUtils.ReAllocateHandleIfNeeded(ref _rendererData.CameraPreviousColorTextureRT, descriptor, filterMode,
                TextureWrapMode.Clamp, name: "_CameraPreviousColorTexture");

            TextureHandle destinationHandle = renderGraph.ImportTexture(_rendererData.CameraPreviousColorTextureRT);

            // Copy color to history
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Copy History Color", out var passData, profilingSampler))
            {
                builder.UseTexture(cameraColor);
                passData.Source = cameraColor;
                
                builder.SetRenderAttachment(destinationHandle, 0);
                passData.Destination = destinationHandle;
                passData.CopyColorMaterial = _blitMaterial;

                builder.AllowPassCulling(false);
                
                builder.SetGlobalTextureAfterPass(destinationHandle, Properties._CameraPreviousColorTexture);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Clear destination
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                    Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), data.CopyColorMaterial, 0);
                });
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_blitMaterial);
            CoreUtils.Destroy(_samplingMaterial);
        }
        
        private static class Properties
        {
            public static readonly int _CameraPreviousColorTexture = MemberNameHelpers.ShaderPropertyID();
        }
    }
}