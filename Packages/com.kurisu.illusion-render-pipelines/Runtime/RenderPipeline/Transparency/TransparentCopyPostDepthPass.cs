using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy depth to depth buffer before overdraw.
    /// </summary>
    public class TransparentCopyPostDepthPass : ScriptableRenderPass, IDisposable
    {
        private readonly CopyDepthPass _copyDepthPass;

        public TransparentCopyPostDepthPass()
        {
            Shader copyDephPS = null;
            if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>(out var universalRendererShaders))
            {
                copyDephPS = universalRendererShaders.copyDepthPS;
            }
            profilingSampler = new ProfilingSampler("CopyPostDepth");
            renderPassEvent = IllusionRenderPassEvent.TransparentCopyPostDepthPass;
            _copyDepthPass = new CopyDepthPass(renderPassEvent, copyDephPS, false, false, RenderingUtils.MultisampleDepthResolveSupported())
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
            TextureHandle destination = resource.activeDepthTexture;

            _copyDepthPass.CopyToDepth = true;
            _copyDepthPass.Render(renderGraph, destination, source, resource, cameraData, bindAsCameraDepth: false, passName: "Copy Post Depth");
        }

        public void Dispose()
        {
            // pass
        }
    }
}