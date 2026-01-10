using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.Shadows
{
    /// <summary>
    /// Diffuse shadow denoiser for contact shadows using bilateral filtering.
    /// Supports both legacy CommandBuffer and RenderGraph paths.
    /// </summary>
    public class DiffuseShadowDenoiser
    {
        // The compute shader required by this component
        private readonly ComputeShader _shadowDenoiser;

        // Kernels that we are using
        private readonly int _bilateralFilterHSingleDirectionalKernel;
        
        private readonly int _bilateralFilterVSingleDirectionalKernel;

        public DiffuseShadowDenoiser(ComputeShader shadowDenoiserCS)
        {
            _shadowDenoiser = shadowDenoiserCS;

            _bilateralFilterHSingleDirectionalKernel = _shadowDenoiser.FindKernel("BilateralFilterHSingleDirectional");
            _bilateralFilterVSingleDirectionalKernel = _shadowDenoiser.FindKernel("BilateralFilterVSingleDirectional");
        }

        /// <summary>
        /// Legacy path: Denoise buffer using CommandBuffer and RTHandles
        /// </summary>
        public void DenoiseBuffer(CommandBuffer cmd, 
            RTHandle depthBuffer, RTHandle normalBuffer,
            RTHandle noisyBuffer, RTHandle intermediateBuffer, RTHandle outputBuffer,
            int texWidth, int texHeight, int viewCount,
            float lightAngle, float cameraFov, int kernelSize,
            ProfilingSampler profilingSampler)
        {
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // TODO: Add distance based denoise support
                // Raise the distance based denoiser keyword
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", true);

                // Evaluate the dispatch parameters
                int numTilesX = IllusionRenderingUtils.DivRoundUp(texWidth, 8);
                int numTilesY = IllusionRenderingUtils.DivRoundUp(texHeight, 8);

                // Bind input uniforms for both dispatches
                cmd.SetComputeFloatParam(_shadowDenoiser, RayTracing.RayTracingShaderProperties.RaytracingLightAngle, lightAngle);
                cmd.SetComputeIntParam(_shadowDenoiser, RayTracing.RayTracingShaderProperties.DenoiserFilterRadius, kernelSize);
                cmd.SetComputeFloatParam(_shadowDenoiser, RayTracing.RayTracingShaderProperties.CameraFOV, cameraFov);

                // Bind Input Textures for horizontal pass
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel, 
                    RayTracing.RayTracingShaderProperties.DepthTexture, depthBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                    RayTracing.RayTracingShaderProperties.NormalBufferTexture, normalBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                    RayTracing.RayTracingShaderProperties.DenoiseInputTexture, noisyBuffer);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                //     RayTracingShaderProperties.DistanceTexture, distanceBuffer);

                // Bind output texture for horizontal pass
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel,
                    RayTracing.RayTracingShaderProperties.DenoiseOutputTextureRW, intermediateBuffer);

                // Do the Horizontal pass
                cmd.DispatchCompute(_shadowDenoiser, _bilateralFilterHSingleDirectionalKernel, numTilesX, numTilesY, viewCount);

                // Bind Input Textures for vertical pass
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel, 
                    RayTracing.RayTracingShaderProperties.DepthTexture, depthBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                    RayTracing.RayTracingShaderProperties.NormalBufferTexture, normalBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                    RayTracing.RayTracingShaderProperties.DenoiseInputTexture, intermediateBuffer);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                //     RayTracingShaderProperties.DistanceTexture, distanceBuffer);

                // Bind output texture for vertical pass
                cmd.SetComputeTextureParam(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel,
                    RayTracing.RayTracingShaderProperties.DenoiseOutputTextureRW, outputBuffer);

                // Do the Vertical pass
                cmd.DispatchCompute(_shadowDenoiser, _bilateralFilterVSingleDirectionalKernel, numTilesX, numTilesY, viewCount);

                // TODO: Add distance based denoise support
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", false);
            }
        }

#if UNITY_2023_1_OR_NEWER
        private class DenoisePassData
        {
            // Camera parameters
            public int TexWidth;
            public int TexHeight;
            public int ViewCount;

            // Evaluation parameters
            public float LightAngle;
            public float CameraFov;
            public int KernelSize;

            // Kernels
            public int BilateralHKernel;
            public int BilateralVKernel;

            // Other parameters
            public ComputeShader DiffuseShadowDenoiserCs;

            public TextureHandle DepthStencilBuffer;
            public TextureHandle NormalBuffer;
            public TextureHandle NoisyBuffer;
            public TextureHandle IntermediateBuffer;
            public TextureHandle OutputBuffer;
        }

        /// <summary>
        /// RenderGraph path: Denoise buffer using RenderGraph and TextureHandles
        /// </summary>
        public void DenoiseBuffer(RenderGraph renderGraph,
            TextureHandle depthBuffer, TextureHandle normalBuffer,
            TextureHandle noisyBuffer, TextureHandle intermediateBuffer, TextureHandle outputBuffer,
            int texWidth, int texHeight, int viewCount,
            float lightAngle, float cameraFov, int kernelSize,
            ProfilingSampler profilingSampler)
        {
            using (var builder = renderGraph.AddComputePass<DenoisePassData>("Diffuse Shadow Denoise", out var passData, profilingSampler))
            {
                // Set the camera parameters
                passData.TexWidth = texWidth;
                passData.TexHeight = texHeight;
                passData.ViewCount = viewCount;

                // Evaluation parameters
                passData.CameraFov = cameraFov;
                passData.LightAngle = lightAngle;
                passData.KernelSize = kernelSize;

                // Kernels
                passData.BilateralHKernel = _bilateralFilterHSingleDirectionalKernel;
                passData.BilateralVKernel = _bilateralFilterVSingleDirectionalKernel;

                // Other parameters
                passData.DiffuseShadowDenoiserCs = _shadowDenoiser;

                // Input buffers
                passData.DepthStencilBuffer = builder.UseTexture(depthBuffer);
                passData.NormalBuffer = builder.UseTexture(normalBuffer);
                passData.NoisyBuffer = builder.UseTexture(noisyBuffer);

                // Temporary buffer
                // TODO: Transient has bug in Unity 2023
                // passData.IntermediateBuffer = builder.CreateTransientTexture(new TextureDesc(texWidth, texHeight, false, false)
                // { 
                //     colorFormat = GraphicsFormat.R16G16B16A16_SFloat, 
                //     enableRandomWrite = true, 
                //     name = "Intermediate buffer" 
                // });
                passData.IntermediateBuffer = builder.UseTexture(intermediateBuffer, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);

                // Output buffer - write to the provided output texture
                passData.OutputBuffer = builder.UseTexture(outputBuffer, IBaseRenderGraphBuilder.AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (DenoisePassData data, ComputeGraphContext context) =>
                {
                    // TODO: Add distance based denoise support
                    // Raise the distance based denoiser keyword
                    // CoreUtils.SetKeyword(context.cmd, "DISTANCE_BASED_DENOISER", true);

                    // Evaluate the dispatch parameters
                    int denoiserTileSize = 8;
                    int numTilesX = (data.TexWidth + (denoiserTileSize - 1)) / denoiserTileSize;
                    int numTilesY = (data.TexHeight + (denoiserTileSize - 1)) / denoiserTileSize;

                    // Bind input uniforms for both dispatches
                    context.cmd.SetComputeFloatParam(data.DiffuseShadowDenoiserCs, RayTracing.RayTracingShaderProperties.RaytracingLightAngle, data.LightAngle);
                    context.cmd.SetComputeIntParam(data.DiffuseShadowDenoiserCs, RayTracing.RayTracingShaderProperties.DenoiserFilterRadius, data.KernelSize);
                    context.cmd.SetComputeFloatParam(data.DiffuseShadowDenoiserCs, RayTracing.RayTracingShaderProperties.CameraFOV, data.CameraFov);

                    // Bind Input Textures for horizontal pass
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralHKernel, 
                        RayTracing.RayTracingShaderProperties.DepthTexture, data.DepthStencilBuffer);
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralHKernel, 
                        RayTracing.RayTracingShaderProperties.NormalBufferTexture, data.NormalBuffer);
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralHKernel, 
                        RayTracing.RayTracingShaderProperties.DenoiseInputTexture, data.NoisyBuffer);
                    
                    // TODO: Add distance based denoise support
                    // context.cmd.SetComputeTextureParam(data.diffuseShadowDenoiserCS, data.bilateralHKernel, 
                    //     RayTracingShaderProperties.DistanceTexture, data.distanceBuffer);

                    // Bind output texture for horizontal pass
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralHKernel, 
                        RayTracing.RayTracingShaderProperties.DenoiseOutputTextureRW, data.IntermediateBuffer);

                    // Do the Horizontal pass
                    context.cmd.DispatchCompute(data.DiffuseShadowDenoiserCs, data.BilateralHKernel, numTilesX, numTilesY, data.ViewCount);

                    // Bind Input Textures for vertical pass
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralVKernel, 
                        RayTracing.RayTracingShaderProperties.DepthTexture, data.DepthStencilBuffer);
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralVKernel, 
                        RayTracing.RayTracingShaderProperties.NormalBufferTexture, data.NormalBuffer);
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralVKernel, 
                        RayTracing.RayTracingShaderProperties.DenoiseInputTexture, data.IntermediateBuffer);
                    
                    // TODO: Add distance based denoise support
                    // context.cmd.SetComputeTextureParam(data.diffuseShadowDenoiserCS, data.bilateralVKernel, 
                    //     RayTracingShaderProperties.DistanceTexture, data.distanceBuffer);

                    // Bind output texture for vertical pass
                    context.cmd.SetComputeTextureParam(data.DiffuseShadowDenoiserCs, data.BilateralVKernel, 
                        RayTracing.RayTracingShaderProperties.DenoiseOutputTextureRW, data.OutputBuffer);

                    // Do the Vertical pass
                    context.cmd.DispatchCompute(data.DiffuseShadowDenoiserCs, data.BilateralVKernel, numTilesX, numTilesY, data.ViewCount);

                    // TODO: Add distance based denoise support
                    // CoreUtils.SetKeyword(context.cmd, "DISTANCE_BASED_DENOISER", false);
                });
            }
        }
#endif
    }
}

