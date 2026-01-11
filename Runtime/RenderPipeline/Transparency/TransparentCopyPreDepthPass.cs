using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy current depth before writing transparent post depth, should be used with <see cref="TransparentDepthNormalPostPass"/>.
    /// </summary>
    public class TransparentCopyPreDepthPass :  ScriptableRenderPass, IDisposable
    {
        private readonly Material _copyDepthMaterial;

        private readonly IllusionRendererData _rendererData;
        
#if UNITY_SWITCH || UNITY_ANDROID
        private const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt;

        private const int k_DepthBufferBits = 24;
#else
        private const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
        
        private const int k_DepthBufferBits = 32;
#endif

        private readonly CopyDepthPass _copyDepthPass;

        public TransparentCopyPreDepthPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            Shader copyDephPS = null;
            if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>(out var universalRendererShaders))
            {
                copyDephPS = universalRendererShaders.copyDepthPS;
            }
            profilingSampler = new ProfilingSampler("CopyPreDepth");
            _copyDepthPass = new CopyDepthPass(renderPassEvent, copyDephPS, true, false, RenderingUtils.MultisampleDepthResolveSupported())
                {
                    profilingSampler = profilingSampler
                };
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle source = resource.cameraDepthTexture;
            
            // Allocate pre-depth texture
            var depthDescriptor = cameraData.cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = k_DepthStencilFormat;
            depthDescriptor.depthBufferBits = k_DepthBufferBits;
            depthDescriptor.msaaSamples = 1; // Depth-Only pass don't use MSAA

            RenderingUtils.ReAllocateHandleIfNeeded(ref _rendererData.CameraPreDepthTextureRT, depthDescriptor, 
                wrapMode: TextureWrapMode.Clamp, name: "_CameraPreDepthTexture");
            
            TextureHandle destination = renderGraph.ImportTexture(_rendererData.CameraPreDepthTextureRT);

            _copyDepthPass.CopyToDepth = true;
            _copyDepthPass.Render(renderGraph, destination, source, resource, cameraData, bindAsCameraDepth: false, passName: "Copy Pre Depth");
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_copyDepthMaterial);
        }
    }
}