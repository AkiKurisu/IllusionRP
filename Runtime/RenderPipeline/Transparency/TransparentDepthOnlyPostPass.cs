using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Render transparent objects depth only after opaque objects.
    /// </summary>
    public class TransparentDepthOnlyPostPass : ScriptableRenderPass, IDisposable
    {
        private const string DepthProfilerTag = "Transparent Post Depth";

        private FilteringSettings _filteringSettings;

        private RenderStateBlock _renderStateBlock;

        private static readonly ShaderTagId PostDepthNormalsTagId = new("PostDepthOnly");

        public TransparentDepthOnlyPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.TransparentDepthOnlyPostPass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private void DoDepthOnly(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var depthTexture = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            if (!depthTexture.IsValid()) return;
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.SetRenderTarget(depthTexture);
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
                DoDepthOnly(cmd, context, ref renderingData);
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