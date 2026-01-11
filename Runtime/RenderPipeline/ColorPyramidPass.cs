using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

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

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle cameraColor = resource.activeColorTexture;

            // Ensure history buffer is allocated
            if (_rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain) == null)
            {
                _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain,
                    HistoryBufferAllocatorFunction, 1);
            }

            var colorPyramidRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ColorBufferMipChain);
            TextureHandle colorPyramidHandle = renderGraph.ImportTexture(colorPyramidRT);

            Vector2Int pyramidSize = new Vector2Int(cameraData.camera.pixelWidth, cameraData.camera.pixelHeight);

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
                builder.UseTexture(cameraColor);
                copyPassData.Source = cameraColor;
                
                builder.SetRenderAttachment(colorPyramidHandle, 0);
                copyPassData.Destination = colorPyramidHandle;
                
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
                builder.UseTexture(colorPyramidHandle, AccessFlags.Write);
                mipPassData.ColorPyramid = colorPyramidHandle;
                
                // Get or allocate temp downsample pyramid and import it
                var tempPyramidRT = _rendererData.MipGenerator.GetOrAllocateTempDownsamplePyramid(colorPyramidRT.rt.graphicsFormat);
                var tempPyramid = renderGraph.ImportTexture(tempPyramidRT);
                builder.UseTexture(tempPyramid, AccessFlags.ReadWrite);
                mipPassData.TempPyramid = tempPyramid;
                
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
                CopyHistoryGraphicsBuffersRenderGraph(renderGraph, resource, cameraData);
            }
        }

        private void CopyHistoryGraphicsBuffersRenderGraph(RenderGraph renderGraph, UniversalResourceData resource, UniversalCameraData cameraData)
        {
            var descriptor = cameraData.cameraTargetDescriptor;

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
            TextureHandle normalTexture = resource.cameraNormalsTexture;
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
                var depth = renderGraph.ImportTexture(currentDepth);
                builder.UseTexture(depth);
                passData.DepthSource = depth;
                
                var depthHistory = renderGraph.ImportTexture(depthHistoryRT);
                builder.UseTexture(depthHistory, AccessFlags.Write);
                passData.DepthDestination = depthHistory;

                passData.HasNormalTexture = hasNormalTexture;
                if (hasNormalTexture)
                {
                    builder.UseTexture(normalTexture);
                    passData.NormalSource = normalTexture;
                    var normalDestination = renderGraph.ImportTexture(normalHistoryRT);
                    builder.UseTexture(normalDestination, AccessFlags.Write);
                    passData.NormalDestination = normalDestination;
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

        public void Dispose()
        {
            // pass
        }
    }
}