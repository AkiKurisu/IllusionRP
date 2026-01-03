using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    /// <summary>
    /// Hiz generate pass using a packed atlas
    /// </summary>
    public class DepthPyramidPass : ScriptableRenderPass, IDisposable
    {
        private PackedMipChainInfo _mipChainInfo;

        private readonly IllusionRendererData _rendererData;

        private static readonly ProfilingSampler CopyDepthSampler = new("Copy Depth Buffer");

        private static readonly ProfilingSampler DepthPyramidSampler = new("Depth Pyramid");

        public DepthPyramidPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.DepthPyramidPass;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            _mipChainInfo = _rendererData.DepthMipChainInfo;
            var mipChainSize = _rendererData.DepthMipChainSize;
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.enableRandomWrite = true;
            depthDescriptor.width = mipChainSize.x;
            depthDescriptor.height = mipChainSize.y;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.DepthPyramidRT, depthDescriptor, name: "CameraDepthBufferMipChain");
            cmd.SetGlobalTexture(IllusionShaderProperties._DepthPyramid, _rendererData.DepthPyramidRT);
        }

#if UNITY_2023_1_OR_NEWER
        private class CopyDepthPassData
        {
            internal TextureHandle InputDepthTexture;
            internal TextureHandle OutputDepthPyramid;
            internal GPUCopy GPUCopy;
            internal int Width;
            internal int Height;
        }

        private class DepthPyramidPassData
        {
            internal TextureHandle DepthPyramidTexture;
            internal MipGenerator MipGenerator;
            internal PackedMipChainInfo MipChainInfo;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            
            // Get or allocate depth pyramid RT
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            _mipChainInfo = _rendererData.DepthMipChainInfo;
            var mipChainSize = _rendererData.DepthMipChainSize;
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.enableRandomWrite = true;
            depthDescriptor.width = mipChainSize.x;
            depthDescriptor.height = mipChainSize.y;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.DepthPyramidRT, depthDescriptor, name: "CameraDepthBufferMipChain");

            TextureHandle depthPyramidHandle = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            TextureHandle cameraDepth = renderer.activeDepthTexture;

            // Copy depth to pyramid mip 0
            using (var builder = renderGraph.AddRenderPass<CopyDepthPassData>("Copy Depth Buffer", out var passData, CopyDepthSampler))
            {
                passData.InputDepthTexture = builder.ReadTexture(cameraDepth);
                passData.OutputDepthPyramid = builder.WriteTexture(depthPyramidHandle);
                passData.GPUCopy = _rendererData.GPUCopy;
                passData.Width = cameraTargetDescriptor.width;
                passData.Height = cameraTargetDescriptor.height;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CopyDepthPassData data, RenderGraphContext context) =>
                {
                    data.GPUCopy.SampleCopyChannel_xyzw2x(context.cmd, data.InputDepthTexture, data.OutputDepthPyramid,
                        new RectInt(0, 0, data.Width, data.Height));
                });
            }

            // Generate depth pyramid
            using (var builder = renderGraph.AddRenderPass<DepthPyramidPassData>("Depth Pyramid", out var passData, DepthPyramidSampler))
            {
                passData.DepthPyramidTexture = builder.WriteTexture(depthPyramidHandle);
                passData.MipGenerator = _rendererData.MipGenerator;
                passData.MipChainInfo = _mipChainInfo;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DepthPyramidPassData data, RenderGraphContext context) =>
                {
                    data.MipGenerator.RenderMinDepthPyramid(context.cmd, data.DepthPyramidTexture, data.MipChainInfo);
                });
            }

            // Set global texture for shaders
            RenderGraphUtils.SetGlobalTexture(renderGraph, IllusionShaderProperties._DepthPyramid, depthPyramidHandle);
        }
#endif

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            // In prepass stage use DepthTexture
            var cameraDepth = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            var cmd = CommandBufferPool.Get();
            // Copy Depth
            if (cameraDepth != null && cameraDepth.rt)
            {
                var gpuCopy = _rendererData.GPUCopy;
                using (new ProfilingScope(cmd, CopyDepthSampler))
                {
                    gpuCopy.SampleCopyChannel_xyzw2x(cmd, cameraDepth, _rendererData.DepthPyramidRT,
                        new RectInt(0, 0, cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                }
            }
            // Depth Pyramid
            {
                using (new ProfilingScope(cmd, DepthPyramidSampler))
                {
                    _rendererData.MipGenerator.RenderMinDepthPyramid(cmd, _rendererData.DepthPyramidRT.rt, _mipChainInfo);
                }
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