using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PathTracing
{
    internal struct PathTracingRayReconstructionInputs
    {
        public RTHandle Color;
        public RTHandle Output;
        public RTHandle MotionVectors;
        public RTHandle Depth;
        public RTHandle DiffuseAlbedo;
        public RTHandle SpecularAlbedo;
        public RTHandle NormalsAndRoughness;
        public RTHandle SpecularMotionVectors;
        public Matrix4x4 WorldToView;
        public Matrix4x4 ViewToClip;
        public Vector2 Jitter;
        public int Width;
        public int Height;
        public int OutputWidth;
        public int OutputHeight;
        public PathTracingRayReconstructionQuality Quality;
    }

    internal static class PathTracingRayReconstructionScale
    {
        public static float Of(PathTracingRayReconstructionQuality quality) => quality switch
        {
            PathTracingRayReconstructionQuality.Quality => 2.0f / 3.0f,
            PathTracingRayReconstructionQuality.Balanced => 0.58f,
            PathTracingRayReconstructionQuality.Performance => 0.5f,
            PathTracingRayReconstructionQuality.UltraPerformance => 1.0f / 3.0f,
            _ => 1.0f
        };

        public static int Apply(int size, float scale) => Mathf.Max(1, Mathf.RoundToInt(size * scale));
    }

    internal interface IPathTracingRayReconstruction : IDisposable
    {
        bool IsAvailable { get; }

        void Record(RenderGraph renderGraph, Camera camera, in PathTracingRayReconstructionInputs inputs, bool resetHistory);

        void Release(int cameraId);
    }

    internal static class PathTracingRayReconstructionLoader
    {
        private static Func<IPathTracingRayReconstruction> _factory;

        internal static void Register(Func<IPathTracingRayReconstruction> factory) => _factory = factory;

        public static IPathTracingRayReconstruction Create() => _factory?.Invoke();
    }
}
