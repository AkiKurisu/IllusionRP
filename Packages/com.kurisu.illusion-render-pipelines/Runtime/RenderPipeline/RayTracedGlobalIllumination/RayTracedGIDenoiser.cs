using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering
{
    internal struct RayTracedGIDenoiserInputs
    {
        public RTHandle ViewZ;
        public RTHandle NormalRoughness;
        public RTHandle MotionVectors;
        public bool ScreenMotion;
        public RTHandle Diffuse;
        public RTHandle Output;
        public RTHandle Specular;
        public RTHandle SpecularOutput;
        public Matrix4x4 WorldToView;
        public Matrix4x4 ViewToClip;
        public Vector2 Jitter;
        public int Width;
        public int Height;
        public float DenoisingRange;
        public RayTracedGIDenoiser Denoiser;
        public bool Checkerboard;
        public uint FrameIndex;
    }

    internal interface IRayTracedGIDenoiser : IDisposable
    {
        bool IsAvailable { get; }

        void Record(RenderGraph renderGraph, Camera camera, in RayTracedGIDenoiserInputs inputs, bool resetHistory);

        void Release(int cameraId);
    }

    internal static class RayTracedGIDenoiserLoader
    {
        private const string BackendTypeName =
            "Illusion.Rendering.RayTracedGlobalIllumination.UnityRHI.UnityRHIRayTracedGIDenoiser, Illusion.RenderPipelines.RayTracedGlobalIllumination.UnityRHI";

        public static IRayTracedGIDenoiser Create()
        {
            Type type = Type.GetType(BackendTypeName, false);
            return type == null ? null : Activator.CreateInstance(type) as IRayTracedGIDenoiser;
        }
    }
}
