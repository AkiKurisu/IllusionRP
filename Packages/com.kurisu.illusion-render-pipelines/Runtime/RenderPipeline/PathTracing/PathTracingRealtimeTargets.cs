using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingRealtimeTargets : IDisposable
    {
        public const int StablePlaneCount = 3;

        private const int StablePlaneStride = 80;

        private const int TileSize = 8;

        public int Width { get; private set; }

        public int Height { get; private set; }

        private int _outputWidth;

        private int _outputHeight;

        public uint LineStride { get; private set; }

        public uint PlaneStride { get; private set; }

        public RTHandle StablePlanesHeader { get; private set; }

        public GraphicsBuffer StablePlanes { get; private set; }

        public RTHandle StableRadiance { get; private set; }

        public RTHandle Color { get; private set; }

        public RTHandle DiffuseAlbedo { get; private set; }

        public RTHandle SpecularAlbedo { get; private set; }

        public RTHandle NormalsAndRoughness { get; private set; }

        public RTHandle SpecularMotionVectors { get; private set; }

        public RTHandle SpecularHitTScratch { get; private set; }

        public RTHandle Output { get; private set; }

        public void Ensure(int width, int height, int outputWidth, int outputHeight)
        {
            if (StablePlanes != null && Width == width && Height == height && _outputWidth == outputWidth && _outputHeight == outputHeight)
                return;

            Release();
            Width = width;
            Height = height;
            _outputWidth = outputWidth;
            _outputHeight = outputHeight;
            LineStride = (uint)((width + TileSize - 1) / TileSize * TileSize);
            PlaneStride = LineStride * (uint)((height + TileSize - 1) / TileSize * TileSize);

            StablePlanesHeader = RTHandles.Alloc(width, height, slices: StablePlaneCount + 1, dimension: TextureDimension.Tex2DArray,
                colorFormat: GraphicsFormat.R32_UInt, enableRandomWrite: true, filterMode: FilterMode.Point, name: "_PathTracingStablePlanesHeader");
            StablePlanes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)PlaneStride * StablePlaneCount, StablePlaneStride)
            {
                name = "PathTracingStablePlanes"
            };
            StableRadiance = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_PathTracingStableRadiance");
            Color = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_PathTracingRealtimeColor");
            DiffuseAlbedo = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_PathTracingDiffuseAlbedo");
            SpecularAlbedo = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_PathTracingSpecularAlbedo");
            NormalsAndRoughness = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_PathTracingNormalsAndRoughness");
            SpecularMotionVectors = Alloc(GraphicsFormat.R16G16_SFloat, "_PathTracingSpecularMotionVectors");
            SpecularHitTScratch = Alloc(GraphicsFormat.R32_SFloat, "_PathTracingSpecularHitTScratch");
            Output = RTHandles.Alloc(outputWidth, outputHeight, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true,
                filterMode: FilterMode.Point, name: "_PathTracingRayReconstructionOutput");
        }

        private RTHandle Alloc(GraphicsFormat format, string name)
        {
            return RTHandles.Alloc(Width, Height, colorFormat: format, enableRandomWrite: true, filterMode: FilterMode.Point, name: name);
        }

        private void Release()
        {
            RTHandles.Release(StablePlanesHeader);
            StablePlanes?.Release();
            RTHandles.Release(StableRadiance);
            RTHandles.Release(Color);
            RTHandles.Release(DiffuseAlbedo);
            RTHandles.Release(SpecularAlbedo);
            RTHandles.Release(NormalsAndRoughness);
            RTHandles.Release(SpecularMotionVectors);
            RTHandles.Release(SpecularHitTScratch);
            RTHandles.Release(Output);
            StablePlanesHeader = StableRadiance = Color = DiffuseAlbedo = SpecularAlbedo = NormalsAndRoughness = null;
            SpecularMotionVectors = SpecularHitTScratch = Output = null;
            StablePlanes = null;
        }

        public void Dispose()
        {
            Release();
        }
    }
}
