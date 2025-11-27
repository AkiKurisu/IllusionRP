using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public class TransparentDepthNormalPostPass : ScriptableRenderPass, IDisposable
    {
        private const string DepthProfilerTag = "Transparent Post Depth Normal";

        private FilteringSettings _filteringSettings;

        private RenderStateBlock _renderStateBlock;

        private static readonly ShaderTagId PostDepthNormalsTagId = new("PostDepthNormals");

        public TransparentDepthNormalPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.TransparentDepthNormalPostPass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Will not fall back to PostDepthOnly even when ssao, ssr is disabled.
            // We use two post depth pass to distinguish materials need post depth
            // for both screen space effect and post process or post process only.
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        private void DoDepthNormal(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var depthTexture = UniversalRenderingUtility.GetDepthWriteTexture(ref renderingData.cameraData);
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(renderingData.cameraData.renderer);
            if (!depthTexture.IsValid() || !normalTexture.IsValid()) return;
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.SetRenderTarget(normalTexture, depthTexture);
            context.ExecuteCommandBuffer(cmd);
            var drawSettings = RenderingUtils.CreateDrawingSettings(PostDepthNormalsTagId,
                ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
            context.DrawRenderers(renderingData.cullResults, ref drawSettings,
                ref _filteringSettings, ref _renderStateBlock);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif
            
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(DepthProfilerTag)))
            {
                DoDepthNormal(cmd, context, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            // pass
        }
    }
}