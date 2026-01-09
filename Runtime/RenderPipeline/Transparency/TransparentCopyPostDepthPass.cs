using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy depth to depth buffer before overdraw.
    /// </summary>
    public class TransparentCopyPostDepthPass : CopyDepthPass, IDisposable
    {
        private readonly Material _copyDepthMaterial;

#if UNITY_2023_1_OR_NEWER
        private readonly CopyDepthPass _copyDepthPass;
#endif

        private TransparentCopyPostDepthPass(Material copyDepthMaterial, bool copyResolvedDepth = false)
            : base(IllusionRenderPassEvent.TransparentCopyPostDepthPass, 
                copyDepthMaterial, false, false, copyResolvedDepth)
        {
            _copyDepthMaterial = copyDepthMaterial;
            profilingSampler = new ProfilingSampler("CopyPostDepth");
#if UNITY_2023_1_OR_NEWER
            _copyDepthPass = new CopyDepthPass(renderPassEvent, copyDepthMaterial, false, false, copyResolvedDepth)
            {
                profilingSampler = profilingSampler
            };
            ConfigureInput(ScriptableRenderPassInput.Depth);
#endif
        }

        public static TransparentCopyPostDepthPass Create()
        {
            var copyDepthMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/CopyDepth");
            Assert.IsTrue((bool)copyDepthMaterial);
            return new TransparentCopyPostDepthPass(copyDepthMaterial, RenderingUtils.MultisampleDepthResolveSupported());
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

#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle source = frameResources.GetTexture(UniversalResource.CameraDepthTexture);
            TextureHandle destination = renderer.activeDepthTexture;

            _copyDepthPass.CopyToDepth = true;
            _copyDepthPass.Render(renderGraph, destination, source, ref renderingData, bindAsCameraDepth: false, passName: "Copy Post Depth");
        }
#endif

        public void Dispose()
        {
            CoreUtils.Destroy(_copyDepthMaterial);
        }
    }
}