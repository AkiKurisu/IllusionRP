using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Normal);
        }

        public void Dispose()
        {
            // pass
        }

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
    }
}