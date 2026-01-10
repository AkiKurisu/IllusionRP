using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.Shadows
{
    /// <summary>
    /// DiffuseShadowDenoiser pass for Main Directional Light Shadow.
    /// </summary>
    public class DiffuseShadowDenoisePass : ScriptableRenderPass, IDisposable
    {
        // The denoiser instance
        private readonly DiffuseShadowDenoiser _denoiser;

        // Camera parameters
        private int _texWidth;

        private int _texHeight;

        private int _viewCount;

        // Evaluation parameters
        private float _lightAngle;

        private float _cameraFov;

        private int _kernelSize;

        // Other parameters
        private RTHandle _depthStencilBuffer;

        private RTHandle _normalBuffer;

        private RTHandle _distanceBuffer;

        private RTHandle _intermediateBuffer;

        private readonly ProfilingSampler _profilingSampler;

        private readonly IllusionRendererData _rendererData;

        public DiffuseShadowDenoisePass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.DiffuseShadowDenoisePass;
            _profilingSampler = new ProfilingSampler("Diffuse Shadow Denoise");
            _denoiser = new DiffuseShadowDenoiser(_rendererData.RuntimeResources.diffuseShadowDenoiserCS);
#if UNITY_2023_1_OR_NEWER
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
#endif
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            PrepareTextures(ref renderingData);
        }

        private void PrepareTextures(ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

            // Temporary buffers
            RenderingUtils.ReAllocateIfNeeded(ref _intermediateBuffer, desc, name: "Intermediate buffer");

            // Output buffer
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.ContactShadowsDenoisedRT, desc, name: "Denoised Buffer");
            // TODO: Add distance based denoise support
            // _distanceBuffer = null;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        private void PrepareDenoiseParameters(ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();

            _cameraFov = camera.fieldOfView * Mathf.PI / 180.0f;
            // Convert the angular diameter of the directional light to radians (from degrees)
            const float angularDiameter = 2.5f;
            _lightAngle = angularDiameter * Mathf.PI / 180.0f;
            _kernelSize = contactShadows.filterSizeTraced.value;

            int actualWidth = cameraData.cameraTargetDescriptor.width;
            int actualHeight = cameraData.cameraTargetDescriptor.height;
            _texWidth = actualWidth;
            _texHeight = actualHeight;
            _viewCount = 1;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!_rendererData.ContactShadowsSampling) return;
            
            // Prepare data
            var cameraData = renderingData.cameraData;
            var renderer = cameraData.renderer;

            _depthStencilBuffer = UniversalRenderingUtility.GetDepthTexture(renderer);
            if (_depthStencilBuffer == null) return;

            _normalBuffer = UniversalRenderingUtility.GetNormalTexture(renderer);
            if (_normalBuffer == null) return;

            PrepareDenoiseParameters(ref renderingData);

            var cmd = CommandBufferPool.Get();
            
            // Call denoiser
            _denoiser.DenoiseBuffer(cmd,
                _depthStencilBuffer, _normalBuffer,
                _rendererData.ContactShadowsRT, _intermediateBuffer, _rendererData.ContactShadowsDenoisedRT,
                _texWidth, _texHeight, _viewCount,
                _lightAngle, _cameraFov, _kernelSize,
                _profilingSampler);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            if (!_rendererData.ContactShadowsSampling) return;
            
            // TODO: Replace with TransientTexture
            PrepareTextures(ref renderingData);
            
            // Prepare parameters
            PrepareDenoiseParameters(ref renderingData);

            // Import depth and normal textures
            TextureHandle depthHandle = frameResources.GetTexture(UniversalResource.CameraDepthTexture);
            TextureHandle normalHandle = frameResources.GetTexture(UniversalResource.CameraNormalsTexture);

            // Import input and output textures
            TextureHandle noisyHandle = renderGraph.ImportTexture(_rendererData.ContactShadowsRT);
            TextureHandle denoisedHandle = renderGraph.ImportTexture(_rendererData.ContactShadowsDenoisedRT);
            TextureHandle intermediateHandle = renderGraph.ImportTexture(_intermediateBuffer);
            
            // Call denoiser RenderGraph version - writes directly to output
            _denoiser.DenoiseBuffer(renderGraph,
                depthHandle, normalHandle, 
                noisyHandle, intermediateHandle, denoisedHandle,
                _texWidth, _texHeight, _viewCount,
                _lightAngle, _cameraFov, _kernelSize,
                _profilingSampler);
        }
#endif

        public void Dispose()
        {
            _intermediateBuffer?.Release();
        }
    }
}