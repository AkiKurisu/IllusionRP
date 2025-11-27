using System;
using Illusion.Rendering.RayTracing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    /// <summary>
    /// DiffuseShadowDenoiser pass for Main Directional Light Shadow.
    /// </summary>
    public class DiffuseShadowDenoisePass : ScriptableRenderPass, IDisposable
    {
        // The resources required by this component
        private readonly ComputeShader _shadowDenoiser;

        // Kernels that we are using
        private readonly int _bilateralFilterHSingleDirectionalKernel;

        private readonly int _bilateralFilterVSingleDirectionalKernel;

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
            _shadowDenoiser = _rendererData.RuntimeResources.diffuseShadowDenoiserCS;
            _bilateralFilterHSingleDirectionalKernel = _shadowDenoiser.FindKernel("BilateralFilterHSingleDirectional");
            _bilateralFilterVSingleDirectionalKernel = _shadowDenoiser.FindKernel("BilateralFilterVSingleDirectional");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
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

            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!_rendererData.ContactShadowsSampling) return;
            
            // Prepare data
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            var renderer = cameraData.renderer;
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();

            _depthStencilBuffer = UniversalRenderingUtility.GetDepthTexture(renderer);
            if (_depthStencilBuffer == null) return;

            _normalBuffer = UniversalRenderingUtility.GetNormalTexture(renderer);
            if (_normalBuffer == null) return;

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


            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // TODO: Add distance based denoise support
                // Raise the distance based denoiser keyword
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", true);

                // Evaluate the dispatch parameters
                int numTilesX = IllusionRenderingUtils.DivRoundUp(_texWidth, 8);
                int numTilesY = IllusionRenderingUtils.DivRoundUp(_texHeight, 8);

                // Bind input uniforms for both dispatches
                cmd.SetComputeFloatParam(_shadowDenoiser, RayTracingShaderProperties.RaytracingLightAngle, _lightAngle);
                cmd.SetComputeIntParam(_shadowDenoiser, RayTracingShaderProperties.DenoiserFilterRadius, _kernelSize);
                cmd.SetComputeFloatParam(_shadowDenoiser, RayTracingShaderProperties.CameraFOV, _cameraFov);

                // Bind Input Textures
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel, RayTracingShaderProperties.DepthTexture,
                    _depthStencilBuffer);

                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                    RayTracingShaderProperties.NormalBufferTexture, _normalBuffer);

                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                    RayTracingShaderProperties.DenoiseInputTexture, _rendererData.ContactShadowsRT);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                //     RayTracingShaderIds.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                    RayTracingShaderProperties.DenoiseOutputTextureRW, _intermediateBuffer);

                // Do the Horizontal pass
                cmd.DispatchCompute(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel, numTilesX, numTilesY, _viewCount);

                // Bind Input Textures
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel, RayTracingShaderProperties.DepthTexture,
                    _depthStencilBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                    RayTracingShaderProperties.NormalBufferTexture, _normalBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                    RayTracingShaderProperties.DenoiseInputTexture, _intermediateBuffer);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                //     RayTracingShaderIds.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                    RayTracingShaderProperties.DenoiseOutputTextureRW, _rendererData.ContactShadowsDenoisedRT);

                // Do the Vertical pass
                cmd.DispatchCompute(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel, numTilesX, numTilesY, _viewCount);

                // TODO: Add distance based denoise support
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", false);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _intermediateBuffer?.Release();
        }
    }
}