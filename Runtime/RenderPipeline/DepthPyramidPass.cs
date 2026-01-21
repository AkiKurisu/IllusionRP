using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

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
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

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

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            
            if (cameraData.cameraType is CameraType.Preview or CameraType.Reflection) return;
            
            // Get or allocate depth pyramid RT
            var cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
            _mipChainInfo = _rendererData.DepthMipChainInfo;
            var mipChainSize = _rendererData.DepthMipChainSize;
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.enableRandomWrite = true;
            depthDescriptor.width = mipChainSize.x;
            depthDescriptor.height = mipChainSize.y;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _rendererData.DepthPyramidRT, depthDescriptor, name: "CameraDepthBufferMipChain");

            TextureHandle depthPyramidHandle = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            TextureHandle cameraDepth = resource.cameraDepthTexture;

            // Copy depth to pyramid mip 0
            using (var builder = renderGraph.AddComputePass<CopyDepthPassData>("Copy Depth Buffer", out var passData, CopyDepthSampler))
            {
                builder.UseTexture(cameraDepth);
                passData.InputDepthTexture = cameraDepth;

                builder.UseTexture(depthPyramidHandle, AccessFlags.Write);
                passData.OutputDepthPyramid = depthPyramidHandle;
                passData.GPUCopy = _rendererData.GPUCopy;
                passData.Width = cameraTargetDescriptor.width;
                passData.Height = cameraTargetDescriptor.height;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CopyDepthPassData data, ComputeGraphContext context) =>
                {
                    data.GPUCopy.SampleCopyChannel_xyzw2x(context.cmd, data.InputDepthTexture, data.OutputDepthPyramid,
                        new RectInt(0, 0, data.Width, data.Height));
                });
            }

            // Generate depth pyramid
            using (var builder = renderGraph.AddComputePass<DepthPyramidPassData>("Depth Pyramid", out var passData, DepthPyramidSampler))
            {
                builder.UseTexture(depthPyramidHandle, AccessFlags.Write);
                passData.DepthPyramidTexture = depthPyramidHandle;
                passData.MipGenerator = _rendererData.MipGenerator;
                passData.MipChainInfo = _mipChainInfo;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetGlobalTextureAfterPass(depthPyramidHandle, IllusionShaderProperties._DepthPyramid);

                builder.SetRenderFunc((DepthPyramidPassData data, ComputeGraphContext context) =>
                {
                    data.MipGenerator.RenderMinDepthPyramid(context.cmd, data.DepthPyramidTexture, data.MipChainInfo);
                });
            }
        }
        
        public void Dispose()
        {
            // pass
        }
    }
}