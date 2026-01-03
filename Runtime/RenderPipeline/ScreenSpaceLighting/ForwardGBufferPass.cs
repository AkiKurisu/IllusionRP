using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    public class ForwardGBufferPass : ScriptableRenderPass, IDisposable
    {
        private int _passIndex;

        private string _targetName;

        private readonly List<ShaderTagId> _shaderTagIdList = new();

        private FilteringSettings _filteringSettings;

        private RenderStateBlock _renderStateBlock;

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
        }

#if !UNITY_2023_1_OR_NEWER
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.ForwardGBufferRT, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_ForwardGBuffer");
            cmd.SetGlobalTexture(_rendererData.ForwardGBufferRT.name, _rendererData.ForwardGBufferRT.nameID);
        }
#endif

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Normal);
        }

        public void Dispose()
        {
            // pass
        }

#if UNITY_2023_1_OR_NEWER
        private class PassData
        {
            internal TextureHandle ForwardGBufferHandle;
            internal TextureHandle DepthHandle;
            internal RenderingData RenderingData;
            internal RendererListHandle RendererListHdl;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle depthTexture = renderer.activeDepthTexture;

            // Allocate Forward GBuffer RT
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.ForwardGBufferRT, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_ForwardGBuffer");

            TextureHandle forwardGBufferHandle = renderGraph.ImportTexture(_rendererData.ForwardGBufferRT);

            // Render Forward GBuffer
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Forward GBuffer", out var passData, profilingSampler))
            {
                passData.ForwardGBufferHandle = builder.UseTextureFragment(forwardGBufferHandle, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
                passData.DepthHandle = builder.UseTextureFragmentDepth(depthTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.RenderingData = renderingData;

                // Create renderer list
                SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(_shaderTagIdList, ref renderingData, sortingCriteria);
                var param = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);
                passData.RendererListHdl = renderGraph.CreateRendererList(param);
                builder.UseRendererList(passData.RendererListHdl);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Clear forward GBuffer
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);

                    // Draw renderers with ForwardGBuffer shader tag
                    context.cmd.DrawRendererList(data.RendererListHdl);
                });
            }

            // Set global texture for shaders
            RenderGraphUtils.SetGlobalTexture(renderGraph, "_ForwardGBuffer", forwardGBufferHandle);
        }
#endif

#if !UNITY_2023_1_OR_NEWER
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderer.cameraColorTargetHandle == null)
                return;
            var depthTexture = UniversalRenderingUtility.GetDepthWriteTexture(ref cameraData);
            if (!depthTexture.IsValid())
            {
                return;
            }
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                ClearForwardGBuffer(context, cmd, depthTexture);
                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                var drawSettings = CreateDrawingSettings(_shaderTagIdList,
                    ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
                context.DrawRenderers(renderingData.cullResults, ref drawSettings,
                    ref _filteringSettings, ref _renderStateBlock);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ClearForwardGBuffer(ScriptableRenderContext context, CommandBuffer cmd, RTHandle depthTexture)
        {
            cmd.SetRenderTarget(_rendererData.ForwardGBufferRT, depthTexture);
            cmd.ClearRenderTarget(false, true, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
#endif
    }
}