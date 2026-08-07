using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Renders water surface data for screen space reflections.
    /// </summary>
    public sealed class WaterSSRDataPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;
        private readonly List<ShaderTagId> _shaderTagIds = new() { new ShaderTagId("WaterSSRData") };
        private readonly FilteringSettings _filteringSettings = new(RenderQueueRange.transparent);
        private readonly RenderStateBlock _renderStateBlock;

        private sealed class PassData
        {
            internal RendererListHandle RendererList;
        }

        public WaterSSRDataPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.WaterSSRDataPass;
            profilingSampler = new ProfilingSampler("Water SSR Data");
            ConfigureInput(ScriptableRenderPassInput.Depth);

            _renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(true, CompareFunction.LessEqual)
            };
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var transparentDepthData = frameData.GetOrCreate<TransparentDepthData>();

            var colorDescriptor = cameraData.cameraTargetDescriptor;
            colorDescriptor.msaaSamples = 1;
            colorDescriptor.depthBufferBits = 0;
            colorDescriptor.depthStencilFormat = GraphicsFormat.None;
            colorDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            colorDescriptor.enableRandomWrite = false;
            colorDescriptor.useMipMap = false;
            colorDescriptor.autoGenerateMips = false;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _rendererData.WaterSSRNormalRT, colorDescriptor,
                FilterMode.Point, TextureWrapMode.Clamp, name: "_WaterSSRNormalTexture");

            TextureHandle normal = renderGraph.ImportTexture(_rendererData.WaterSSRNormalRT);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       "Clear Water SSR Data", out var clearData, profilingSampler))
            {
                builder.SetRenderAttachment(normal, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(normal, IllusionShaderProperties._WaterSSRNormalTexture);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                });
            }

            TextureHandle waterDepth = transparentDepthData.PostDepthTexture;
            if (!waterDepth.IsValid())
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       "Water SSR Data", out var passData, profilingSampler))
            {
                builder.SetRenderAttachment(normal, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(waterDepth, AccessFlags.ReadWrite);

                SortingCriteria sorting = SortingCriteria.CommonTransparent;
                DrawingSettings drawingSettings = UniversalRenderingUtility.CreateDrawingSettings(
                    _shaderTagIds, frameData, sorting);
                RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph,
                    ref renderingData.cullResults, drawingSettings, _filteringSettings,
                    _renderStateBlock, ref passData.RendererList);
                builder.UseRendererList(passData.RendererList);
                builder.SetGlobalTextureAfterPass(normal, IllusionShaderProperties._WaterSSRNormalTexture);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.RendererList);
                });
            }
        }

        public void Dispose()
        {
        }
    }
}
