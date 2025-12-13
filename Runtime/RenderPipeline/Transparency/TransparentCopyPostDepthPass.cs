using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy depth to depth buffer before overdraw.
    /// </summary>
    public class TransparentCopyPostDepthPass : CopyDepthPass, IDisposable
    {
        private TransparentCopyPostDepthPass(Shader copyDepthShader, bool copyResolvedDepth = false)
            : base(IllusionRenderPassEvent.TransparentCopyPostDepthPass, 
                copyDepthShader, false, false, copyResolvedDepth)
        {
            profilingSampler = new ProfilingSampler("CopyPostDepth");
        }

        public static TransparentCopyPostDepthPass Create()
        {
            Shader copyDephPS = null;
            if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>(
                    out var universalRendererShaders))
            {
                copyDephPS = universalRendererShaders.copyDepthPS;
            }
            Assert.IsTrue((bool)copyDephPS);
            return new TransparentCopyPostDepthPass(copyDephPS, RenderingUtils.MultisampleDepthResolveSupported());
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var depthTexture = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            Setup(depthTexture, renderingData.cameraData.renderer.cameraDepthTargetHandle);
            base.OnCameraSetup(cmd, ref renderingData);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Just wrap original profiler sampler
            using (new ProfilingScope(renderingData.commandBuffer, profilingSampler))
            {
                base.Execute(context, ref renderingData);
            }
        }
    }
}