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
    public class ColorPyramidPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;
                
        private readonly ProfilingSampler _copyHistorySampler = new("Copy History");

        public ColorPyramidPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.ColorPyramidPass;
            profilingSampler = new ProfilingSampler("Color Pyramid");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (_rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain) == null)
            {
                _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain,
                    HistoryBufferAllocatorFunction, 1);
            }
        }
        
        // BufferedRTHandleSystem API expects an allocator function. We define it here.
        private static RTHandle HistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;
            return rtHandleSystem.Alloc(Vector2.one,
                // TextureXR.slices, 
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                // dimension: TextureXR.dimension,
                enableRandomWrite: true, useMipMap: true, autoGenerateMips: false, 
                // useDynamicScale: true,
                name: $"{viewName}_CameraColorBufferMipChain{frameIndex}");
        }

#if UNITY_2023_1_OR_NEWER
        private class ColorPyramidPassData
        {
            internal TextureHandle InputColorTexture;
            internal TextureHandle OutputColorPyramid;
            internal MipGenerator MipGenerator;
            internal Camera Camera;
            internal IllusionRendererData RendererData;
            internal bool RequireHistoryDepthNormal;
            internal RenderingData RenderingData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle cameraColor = renderer.activeColorTexture;

            // Ensure history buffer is allocated
            if (_rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain) == null)
            {
                _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain,
                    HistoryBufferAllocatorFunction, 1);
            }

            var colorPyramidRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain);
            TextureHandle colorPyramidHandle = renderGraph.ImportTexture(colorPyramidRT);

            // TODO: Convert to ComputePassData
            // Generate color pyramid
            using (var builder = renderGraph.AddRenderPass<ColorPyramidPassData>("Color Pyramid", out var passData, profilingSampler))
            {
                passData.InputColorTexture = builder.ReadTexture(cameraColor);
                passData.OutputColorPyramid = builder.WriteTexture(colorPyramidHandle);
                passData.MipGenerator = _rendererData.MipGenerator;
                passData.Camera = renderingData.cameraData.camera;
                passData.RendererData = _rendererData;
                passData.RequireHistoryDepthNormal = _rendererData.RequireHistoryDepthNormal;
                passData.RenderingData = renderingData;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ColorPyramidPassData data, RenderGraphContext context) =>
                {
                    Vector2Int pyramidSize = new Vector2Int(data.Camera.pixelWidth, data.Camera.pixelHeight);
                    data.RendererData.ColorPyramidHistoryMipCount = data.MipGenerator.RenderColorGaussianPyramid(context.cmd, 
                        pyramidSize, data.InputColorTexture, passData.OutputColorPyramid);
                });
            }

            // Set global texture for shaders
            RenderGraphUtils.SetGlobalTexture(renderGraph, IllusionShaderProperties._ColorPyramidTexture, colorPyramidHandle);
        }
#endif
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            var cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Color Pyramid
                var colorPyramidRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain);
                cmd.SetGlobalTexture(IllusionShaderProperties._ColorPyramidTexture, colorPyramidRT);
                Vector2Int pyramidSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                _rendererData.ColorPyramidHistoryMipCount =
                    _rendererData.MipGenerator.RenderColorGaussianPyramid(cmd, pyramidSize, cameraColor, colorPyramidRT.rt);
                
                // Copy History if needed
                if (_rendererData.RequireHistoryDepthNormal)
                {
                    using (new ProfilingScope(cmd, _copyHistorySampler))
                    {
                        _rendererData.CopyHistoryGraphicsBuffers(cmd, ref renderingData);
                    }
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