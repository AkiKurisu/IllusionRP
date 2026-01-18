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
        private int _passIndex;

        private string _targetName;

        private readonly List<ShaderTagId> _shaderTagIdList = new();

        private readonly FilteringSettings _filteringSettings;

        private readonly RenderStateBlock _renderStateBlock;

        private readonly IllusionRendererData _rendererData;

        public ForwardGBufferPass(IllusionRendererData rendererData)
        {
            profilingSampler = new ProfilingSampler("Forward GBuffer");
            _shaderTagIdList.Add(new ShaderTagId("ForwardGBuffer"));

            _rendererData = rendererData;
            _filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            renderPassEvent = IllusionRenderPassEvent.ForwardGBufferPass;
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                // ZWrite Off
                // ZTest Equal
                depthState = new DepthState(false, CompareFunction.Equal)
            };
            ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        }

        public void Dispose()
        {
            // pass
        }
        
        private class PassData
        {
            internal TextureHandle ForwardGBufferHandle;
            internal TextureHandle DepthHandle;
            internal UniversalRenderingData RenderingData;
            internal RendererListHandle RendererListHdl;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();

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
            if (!depthTexture.IsValid()) return;
            
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
            
            // Render Forward GBuffer
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Forward GBuffer", out var passData, profilingSampler))
            {
                builder.SetRenderAttachment(forwardGBufferHandle, 0);
                passData.ForwardGBufferHandle = forwardGBufferHandle;
                builder.SetRenderAttachmentDepth(depthTexture);
                passData.DepthHandle = depthTexture;
                passData.RenderingData = renderingData;

                // Create renderer list
                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = UniversalRenderingUtility.CreateDrawingSettings(_shaderTagIdList, frameData, sortingCriteria);
                RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawingSettings, _filteringSettings, _renderStateBlock, ref passData.RendererListHdl);
                builder.UseRendererList(passData.RendererListHdl);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetGlobalTextureAfterPass(forwardGBufferHandle, Properties._ForwardGBuffer);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    // Draw renderers with ForwardGBuffer shader tag
                    context.cmd.DrawRendererList(data.RendererListHdl);
                });
            }
        }
        
        private static class Properties
        {
            public static readonly int _ForwardGBuffer = MemberNameHelpers.ShaderPropertyID();
        }
    }
}