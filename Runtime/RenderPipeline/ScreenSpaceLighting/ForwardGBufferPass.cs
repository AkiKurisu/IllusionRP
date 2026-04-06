using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class ForwardGBufferPass : ScriptableRenderPass, IDisposable
    {
        private readonly List<ShaderTagId> _shaderTagIdList = new();

        private readonly FilteringSettings _filteringSettings;

        /// <summary>No per-drawer override: depth/write state comes from the ForwardGBuffer shader pass.</summary>
        private readonly RenderStateBlock _renderStateBlock;

        private readonly IllusionRendererData _rendererData;

        public ForwardGBufferPass(IllusionRendererData rendererData)
        {
            profilingSampler = new ProfilingSampler("Forward GBuffer");
            _shaderTagIdList.Add(new ShaderTagId("ForwardGBuffer"));

            _rendererData = rendererData;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all);
            renderPassEvent = IllusionRenderPassEvent.ForwardGBufferPass;
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                // ZWrite On
                // ZTest LessEqual
                depthState = new DepthState(true, CompareFunction.LessEqual)
            };
            
            // Match URP GetRenderPassInputs: depth + normals so prepass / resource requirements align with ForwardGBuffer writing _CameraNormalsTexture.
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public void Dispose()
        {
            // pass
        }

        private class PassData
        {
            internal TextureHandle ForwardGBufferHandle;
            internal RendererListHandle RendererListHdl;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var resource = frameData.Get<UniversalResourceData>();

            // Allocate Forward GBuffer RT
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;

            RenderingUtils.ReAllocateHandleIfNeeded(ref _rendererData.ForwardGBufferRT, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_ForwardGBuffer");

            TextureHandle depthTexture = frameData.GetDepthWriteTextureHandle();
            TextureHandle normalsTexture = resource.cameraNormalsTexture;
            if (!depthTexture.IsValid() || !normalsTexture.IsValid()) return;

            TextureHandle forwardGBufferHandle = renderGraph.ImportTexture(_rendererData.ForwardGBufferRT);

            using (var builder = renderGraph.AddUnsafePass<PassData>("Clear Forward GBuffer", out var passData, profilingSampler))
            {
                builder.UseTexture(forwardGBufferHandle, AccessFlags.Write);
                passData.ForwardGBufferHandle = forwardGBufferHandle;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.SetRenderTarget(data.ForwardGBufferHandle);
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                });
            }

            // Render Forward GBuffer (MRT: smoothness + camera normals) + depth
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Forward GBuffer", out var passData, profilingSampler))
            {
                builder.SetRenderAttachment(forwardGBufferHandle, 0);
                builder.SetRenderAttachment(normalsTexture, 1);
                builder.SetRenderAttachmentDepth(depthTexture);
                passData.ForwardGBufferHandle = forwardGBufferHandle;

                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = UniversalRenderingUtility.CreateDrawingSettings(_shaderTagIdList, frameData, sortingCriteria);
                RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawingSettings, _filteringSettings, _renderStateBlock, ref passData.RendererListHdl);
                builder.UseRendererList(passData.RendererListHdl);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetGlobalTextureAfterPass(forwardGBufferHandle, IllusionShaderProperties._ForwardGBuffer);
                builder.SetGlobalTextureAfterPass(normalsTexture, IllusionShaderProperties._CameraNormalsTexture);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.RendererListHdl);
                });
            }
        }
    }
}