using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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

#if !UNITY_2023_1_OR_NEWER
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            ConfigureDescriptor(Downsampling.None, ref descriptor, out var filterMode);
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.CameraPreviousColorTextureRT, descriptor, filterMode,
                TextureWrapMode.Clamp, name: "_CameraPreviousColorTexture");
            ConfigureTarget(_rendererData.CameraPreviousColorTextureRT);
            ConfigureClear(ClearFlag.Color, Color.clear);
            Setup(renderingData.cameraData.renderer.cameraColorTargetHandle, _rendererData.CameraPreviousColorTextureRT, Downsampling.None);
            base.OnCameraSetup(cmd, ref renderingData);
        }
#endif

#if UNITY_2023_1_OR_NEWER
        private class PassData
        {
            internal TextureHandle Source;
            internal TextureHandle Destination;
            internal Material CopyColorMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle cameraColor = renderer.activeColorTexture;

            // Allocate history color texture
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            ConfigureDescriptor(Downsampling.None, ref descriptor, out var filterMode);
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.CameraPreviousColorTextureRT, descriptor, filterMode,
                TextureWrapMode.Clamp, name: "_CameraPreviousColorTexture");

            TextureHandle destinationHandle = renderGraph.ImportTexture(_rendererData.CameraPreviousColorTextureRT);

            // Copy color to history
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Copy History Color", out var passData, profilingSampler))
            {
                passData.Source = builder.UseTexture(cameraColor, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.Destination = builder.UseTextureFragment(destinationHandle, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
                passData.CopyColorMaterial = _blitMaterial;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Clear destination
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                    
                    // Copy color using blit
                    if (data.CopyColorMaterial != null)
                    {
                        Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), data.CopyColorMaterial, 0);
                    }
                });
            }

            // Set global texture for shaders
            RenderGraphUtils.SetGlobalTexture(renderGraph, "_CameraPreviousColorTexture", destinationHandle);
        }
#endif

        public void Dispose()
        {
            CoreUtils.Destroy(_blitMaterial);
            CoreUtils.Destroy(_samplingMaterial);
        }
    }
}