using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
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
        private class CopyMip0PassData
        {
            internal TextureHandle Source;
            internal TextureHandle Destination;
            internal MipGenerator MipGenerator;
            internal Vector2Int PyramidSize;
            internal Vector4 BlitScaleBias;
            internal TextureDimension SourceDimension;
        }

        private class ColorPyramidPassData
        {
            internal TextureHandle ColorPyramid;
            internal TextureHandle TempPyramid;
            internal MipGenerator MipGenerator;
            internal Vector2Int PyramidSize;
            internal IllusionRendererData RendererData;
            internal bool DestinationUseDynamicScale;
        }

        private class CopyHistoryPassData
        {
            internal TextureHandle DepthSource;
            internal TextureHandle DepthDestination;
            internal TextureHandle NormalSource;
            internal TextureHandle NormalDestination;
            internal bool HasNormalTexture;
            internal int Width;
            internal int Height;
            internal GPUCopy GPUCopy;
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

            Vector2Int pyramidSize = new Vector2Int(renderingData.cameraData.camera.pixelWidth, renderingData.cameraData.camera.pixelHeight);

            // Calculate scale and bias for DRS (Dynamic Resolution Scaling)
            bool isHardwareDrsOn = DynamicResolutionHandler.instance.HardwareDynamicResIsEnabled();
            var hardwareTextureSize = pyramidSize;
            if (isHardwareDrsOn)
                hardwareTextureSize = DynamicResolutionHandler.instance.ApplyScalesOnSize(hardwareTextureSize);

            float sourceScaleX = pyramidSize.x / (float)hardwareTextureSize.x;
            float sourceScaleY = pyramidSize.y / (float)hardwareTextureSize.y;
            Vector4 blitScaleBias = new Vector4(sourceScaleX, sourceScaleY, 0f, 0f);

            // Pass 1: Copy source to pyramid mip 0 using fragment shader (RasterPass)
            using (var builder = renderGraph.AddRasterRenderPass<CopyMip0PassData>("Copy Source to Color Pyramid Mip0", 
                out var copyPassData, new ProfilingSampler("Copy to Mip0")))
            {
                copyPassData.Source = builder.UseTexture(cameraColor);
                copyPassData.Destination = builder.UseTextureFragment(colorPyramidHandle, 0);
                copyPassData.MipGenerator = _rendererData.MipGenerator;
                copyPassData.PyramidSize = pyramidSize;
                copyPassData.BlitScaleBias = blitScaleBias;
                copyPassData.SourceDimension = TextureDimension.Tex2D; // Assuming 2D texture

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CopyMip0PassData data, RasterGraphContext context) =>
                {
                    data.MipGenerator.CopySourceToColorPyramidMip0(context.cmd, data.Source, data.Destination, 
                        data.PyramidSize, data.BlitScaleBias, data.SourceDimension);
                });
            }

            // Pass 2: Generate mip chain using compute shader (ComputePass)
            using (var builder = renderGraph.AddComputePass<ColorPyramidPassData>("Color Pyramid Mip Generation", 
                out var mipPassData, profilingSampler))
            {
                mipPassData.ColorPyramid = builder.UseTexture(colorPyramidHandle, IBaseRenderGraphBuilder.AccessFlags.Write);
                
                // Get or allocate temp downsample pyramid and import it
                var tempPyramidRT = _rendererData.MipGenerator.GetOrAllocateTempDownsamplePyramid(colorPyramidRT.rt.graphicsFormat);
                mipPassData.TempPyramid = builder.UseTexture(renderGraph.ImportTexture(tempPyramidRT), 
                    IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
                
                mipPassData.MipGenerator = _rendererData.MipGenerator;
                mipPassData.PyramidSize = pyramidSize;
                mipPassData.RendererData = _rendererData;
                mipPassData.DestinationUseDynamicScale = colorPyramidRT.rt.useDynamicScale;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((ColorPyramidPassData data, ComputeGraphContext context) =>
                {
                    data.RendererData.ColorPyramidHistoryMipCount = data.MipGenerator.RenderColorGaussianPyramidMips(
                        context.cmd, data.ColorPyramid, data.TempPyramid, data.PyramidSize, data.DestinationUseDynamicScale);
                });
            }

            // Set global texture for shaders
            RenderGraphUtils.SetGlobalTexture(renderGraph, IllusionShaderProperties._ColorPyramidTexture, colorPyramidHandle);

            // Pass 3: Copy history depth and normal buffers if needed
            if (_rendererData.RequireHistoryDepthNormal)
            {
                CopyHistoryGraphicsBuffersRenderGraph(renderGraph, ref renderingData);
            }
        }

        private void CopyHistoryGraphicsBuffersRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;

            // Get current depth pyramid
            var currentDepth = _rendererData.DepthPyramidRT;
            if (currentDepth == null || !currentDepth.IsValid())
                return;

            // Allocate depth history buffer if needed
            var depthHistoryRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Depth);
            if (depthHistoryRT == null)
            {
                RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                {
                    return rtHandleSystem.Alloc(Vector2.one, filterMode: FilterMode.Point,
                        colorFormat: currentDepth.rt.graphicsFormat,
                        enableRandomWrite: currentDepth.rt.enableRandomWrite,
                        name: $"{id}_Depth_History_Buffer_{frameIndex}");
                }

                depthHistoryRT = _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.Depth, Allocator, 1);
            }

            // Check for normal texture
            TextureHandle normalTexture = renderer.resources.GetTexture(UniversalResource.CameraNormalsTexture);
            bool hasNormalTexture = normalTexture.IsValid();
            RTHandle normalHistoryRT = null;

            if (hasNormalTexture)
            {
                var graphicsFormat = DepthNormalOnlyPass.GetGraphicsFormat();
                normalHistoryRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Normal);
                if (normalHistoryRT == null)
                {
                    RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                    {
                        return rtHandleSystem.Alloc(Vector2.one, filterMode: FilterMode.Point,
                            colorFormat: graphicsFormat,
                            enableRandomWrite: true,
                            name: $"{id}_Normal_History_Buffer_{frameIndex}");
                    }

                    normalHistoryRT = _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.Normal, Allocator, 1);
                }
            }

            // Create the copy history pass
            using (var builder = renderGraph.AddComputePass<CopyHistoryPassData>("Copy History Graphics Buffers",
                out var passData, _copyHistorySampler))
            {
                passData.DepthSource = builder.UseTexture(renderGraph.ImportTexture(currentDepth));
                passData.DepthDestination = builder.UseTexture(renderGraph.ImportTexture(depthHistoryRT),
                    IBaseRenderGraphBuilder.AccessFlags.Write);

                passData.HasNormalTexture = hasNormalTexture;
                if (hasNormalTexture)
                {
                    passData.NormalSource = builder.UseTexture(normalTexture);
                    passData.NormalDestination = builder.UseTexture(renderGraph.ImportTexture(normalHistoryRT),
                        IBaseRenderGraphBuilder.AccessFlags.Write);
                }

                passData.Width = descriptor.width;
                passData.Height = descriptor.height;
                passData.GPUCopy = _rendererData.GPUCopy;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CopyHistoryPassData data, ComputeGraphContext context) =>
                {
                    // Copy depth history using GPUCopy compute shader (single channel for depth)
                    data.GPUCopy.SampleCopyChannel_xyzw2x(context.cmd, data.DepthSource, data.DepthDestination,
                        new RectInt(0, 0, data.Width, data.Height));

                    // Copy normal history if available (full 4 channels for normal)
                    if (data.HasNormalTexture)
                    {
                        data.GPUCopy.SampleCopyChannel_xyzw2xyzw(context.cmd, data.NormalSource, data.NormalDestination,
                            new RectInt(0, 0, data.Width, data.Height));
                    }
                });
            }
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