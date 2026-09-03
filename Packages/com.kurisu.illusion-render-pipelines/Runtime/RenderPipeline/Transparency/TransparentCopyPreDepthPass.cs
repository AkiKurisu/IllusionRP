using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy current depth before writing transparent post depth, should be used with <see cref="TransparentDepthOnlyPostPass"/>.
    /// </summary>
    public class TransparentCopyPreDepthPass :  ScriptableRenderPass, IDisposable
    {
        private readonly CopyDepthPass _copyDepthPass;

        public TransparentCopyPreDepthPass()
        {
            Shader copyDephPS = null;
            if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>(out var universalRendererShaders))
            {
                copyDephPS = universalRendererShaders.copyDepthPS;
            }
            profilingSampler = new ProfilingSampler("CopyPreDepth");
            renderPassEvent = IllusionRenderPassEvent.TransparentCopyPreDepthPass;
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
            var universalRenderer = (UniversalRenderer)cameraData.renderer;
            TextureHandle source = resource.cameraDepthTexture;
            if (!source.IsValid())
                return;
             
            var transparentDepthData = frameData.GetOrCreate<TransparentDepthData>();
            transparentDepthData.PreDepthTexture = source;

            var postDepthDesc = renderGraph.GetTextureDesc(source);
            postDepthDesc.name = "_CameraTransparentPostDepthTexture";
            postDepthDesc.format = universalRenderer.cameraDepthTextureFormat;
            postDepthDesc.msaaSamples = MSAASamples.None;
            postDepthDesc.bindTextureMS = false;
            postDepthDesc.useMipMap = false;
            postDepthDesc.autoGenerateMips = false;
            postDepthDesc.enableRandomWrite = false;
            postDepthDesc.clearBuffer = true;
            postDepthDesc.clearColor = Color.clear;
            postDepthDesc.filterMode = FilterMode.Point;
            postDepthDesc.wrapMode = TextureWrapMode.Clamp;

            TextureHandle destination = renderGraph.CreateTexture(postDepthDesc);
            transparentDepthData.PostDepthTexture = destination;

            _copyDepthPass.CopyToDepth = true;
            _copyDepthPass.Render(renderGraph, destination, source, resource, cameraData,
                bindAsCameraDepth: false, passName: "Prepare Transparent Post Depth");
        }

        public void Dispose()
        {
            _copyDepthPass?.Dispose();
        }
    }
}
