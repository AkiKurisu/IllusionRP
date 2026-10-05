using System.Runtime.InteropServices;
using UnityEngine;

namespace Illusion.Rendering
{
    // Keep in sync with RayTracedGIConstants.hlsl and the RTXDI parameter headers.

    [StructLayout(LayoutKind.Sequential)]
    internal struct RayTracedGIView
    {
        public Vector3 CameraPosition;
        public float RayBias;
        public Vector3 CameraU;
        public float FarPlane;
        public Vector3 CameraV;
        public float EnvironmentDistance;
        public Vector3 CameraW;
        public float PixelConeSpreadAngle;
        public Vector3 CameraForward;
        public float YFlip;
        public Vector4 ZBufferParams;
        public Vector2 ViewportSize;
        public Vector2 ViewportSizeInv;
        public Vector2 Jitter;
        public Vector2 Pad1;
        public Vector4 TextureSizeAndScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiRuntimeParameters
    {
        public uint NeighborOffsetMask;
        public uint ActiveCheckerboardField;
        public uint FrameIndex;
        public uint Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiReservoirBufferParameters
    {
        public uint ReservoirBlockRowPitch;
        public uint ReservoirArrayPitch;
        public uint Pad1;
        public uint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiGIBufferIndices
    {
        public uint SecondarySurfaceReSTIRDIOutputBufferIndex;
        public uint TemporalResamplingInputBufferIndex;
        public uint TemporalResamplingOutputBufferIndex;
        public uint SpatialResamplingInputBufferIndex;
        public uint SpatialResamplingOutputBufferIndex;
        public uint FinalShadingInputBufferIndex;
        public uint Pad1;
        public uint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiGITemporalResamplingParameters
    {
        public float DepthThreshold;
        public float NormalThreshold;
        public uint MaxHistoryLength;
        public uint EnableFallbackSampling;
        public uint BiasCorrectionMode;
        public uint MaxReservoirAge;
        public uint EnablePermutationSampling;
        public uint UniformRandomNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiBoilingFilterParameters
    {
        public uint EnableBoilingFilter;
        public float BoilingFilterStrength;
        public uint Pad1;
        public uint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiGISpatialResamplingParameters
    {
        public float DepthThreshold;
        public float NormalThreshold;
        public uint NumSamples;
        public float SamplingRadius;
        public uint BiasCorrectionMode;
        public uint Pad1;
        public uint Pad2;
        public uint Pad3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiGISpatioTemporalResamplingParameters
    {
        public float DepthThreshold;
        public float NormalThreshold;
        public uint BiasCorrectionMode;
        public uint NumSamples;
        public float SamplingRadius;
        public uint MaxHistoryLength;
        public uint EnableFallbackSampling;
        public uint MaxReservoirAge;
        public uint EnablePermutationSampling;
        public uint UniformRandomNumber;
        public uint Pad1;
        public uint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiGIFinalShadingParameters
    {
        public uint EnableFinalVisibility;
        public uint EnableFinalMIS;
        public uint Pad1;
        public uint Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RtxdiGIParameters
    {
        public RtxdiReservoirBufferParameters ReservoirBufferParams;
        public RtxdiGIBufferIndices BufferIndices;
        public RtxdiGITemporalResamplingParameters TemporalResamplingParams;
        public RtxdiBoilingFilterParameters BoilingFilterParams;
        public RtxdiGISpatialResamplingParameters SpatialResamplingParams;
        public RtxdiGISpatioTemporalResamplingParameters SpatioTemporalResamplingParams;
        public RtxdiGIFinalShadingParameters FinalShadingParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RayTracedGIConstants
    {
        public RayTracedGIView View;
        public RayTracedGIView PrevView;
        public RtxdiRuntimeParameters RuntimeParams;
        public RtxdiGIParameters ReSTIRGI;
        public uint DenoiserMode;
        public uint EnableAccumulation;
        public uint HistoryValid;
        public uint MotionVectors;
        public Vector4 ReblurHitDistParams;
        public uint Specular;
        public float RadianceClamp;
        public uint Pad2;
        public uint Pad3;

        public static readonly int Stride = Marshal.SizeOf<RayTracedGIConstants>();
    }
}
