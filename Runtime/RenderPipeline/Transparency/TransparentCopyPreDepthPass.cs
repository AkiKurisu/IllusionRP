using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy current depth before writing transparent post depth, should be used with <see cref="TransparentDepthNormalPostPass"/>.
    /// </summary>
    public class TransparentCopyPreDepthPass : CopyDepthPass, IDisposable
    {
        private readonly Material _copyDepthMaterial;

        private readonly IllusionRendererData _rendererData;
        
#if UNITY_SWITCH || UNITY_ANDROID
        const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt;

        const int k_DepthBufferBits = 24;
#else
        const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
        
        const int k_DepthBufferBits = 32;
#endif

        private TransparentCopyPreDepthPass(IllusionRendererData rendererData, Material copyDepthMaterial, bool copyResolvedDepth = false)
            : base(IllusionRenderPassEvent.TransparentCopyPreDepthPass, 
                copyDepthMaterial, true, false, copyResolvedDepth)
        {
            _rendererData = rendererData;
            _copyDepthMaterial = copyDepthMaterial;
            profilingSampler = new ProfilingSampler("CopyPreDepth");
        }

        public static TransparentCopyPreDepthPass Create(IllusionRendererData rendererData)
        {
            var copyDepthMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/CopyDepth");
            Assert.IsTrue((bool)copyDepthMaterial);
            return new TransparentCopyPreDepthPass(rendererData, copyDepthMaterial, RenderingUtils.MultisampleDepthResolveSupported());
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var depthDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = k_DepthStencilFormat;
            depthDescriptor.depthBufferBits = k_DepthBufferBits;

            depthDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.CameraPreDepthTextureRT, depthDescriptor, wrapMode: TextureWrapMode.Clamp, name: "_CameraPreDepthTexture");

            cmd.SetGlobalTexture(_rendererData.CameraPreDepthTextureRT.name, _rendererData.CameraPreDepthTextureRT.nameID);
            var depthTexture = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            Setup(depthTexture, _rendererData.CameraPreDepthTextureRT);
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

        public void Dispose()
        {
            CoreUtils.Destroy(_copyDepthMaterial);
        }
    }
}