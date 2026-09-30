using System.Runtime.InteropServices;
using UnityEngine;

namespace Illusion.Rendering.PathTracing
{

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingViewConstants
    {
        public Matrix4x4 MatWorldToView;
        public Matrix4x4 MatViewToClip;
        public Matrix4x4 MatWorldToClip;
        public Matrix4x4 MatWorldToClipNoOffset;
        public Matrix4x4 MatClipToWorldNoOffset;
        public Vector2 ViewportOrigin;
        public Vector2 ViewportSize;
        public Vector2 ViewportSizeInv;
        public Vector2 PixelOffset;
        public Vector2 ClipToWindowScale;
        public Vector2 ClipToWindowBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingMatrix3x4
    {
        public Vector4 Row0;
        public Vector4 Row1;
        public Vector4 Row2;

        public static PathTracingMatrix3x4 FromMatrix(Matrix4x4 m)
        {
            return new PathTracingMatrix3x4 { Row0 = m.GetColumn(0), Row1 = m.GetColumn(1), Row2 = m.GetColumn(2) };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingEnvMapSceneParams
    {
        public PathTracingMatrix3x4 Transform;
        public PathTracingMatrix3x4 InvTransform;
        public Vector3 ColorMultiplier;
        public float Enabled;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingEnvMapImportanceSamplingParams
    {
        public Vector2 ImportanceInvDim;
        public uint ImportanceBaseMip;
        public uint Padding0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingCameraData
    {
        public Vector3 PosW;
        public float NearZ;
        public Vector3 DirectionW;
        public float PixelConeSpreadAngle;
        public Vector3 CameraU;
        public float FarZ;
        public Vector3 CameraV;
        public float FocalDistance;
        public Vector3 CameraW;
        public float AspectRatio;
        public uint ViewportSizeX;
        public uint ViewportSizeY;
        public float ApertureRadius;
        public float Padding0;
        public Vector2 Jitter;
        public float Padding1;
        public float Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracerConstants
    {
        public uint ImageWidth;
        public uint ImageHeight;
        public uint SampleBaseIndex;
        public float PerPixelJitterAAScale;

        public uint BounceCount;
        public uint DiffuseBounceCount;
        public float EnvironmentMapDiffuseSampleMIPLevel;
        public float TexLODBias;

        public float InvSubSampleCount;
        public float FireflyFilterThreshold;
        public float PreExposedGrayLuminance;
        public uint DenoisingEnabled;

        public uint FrameIndex;
        public uint UseReSTIRDI;
        public uint UseReSTIRGI;
        public uint Padding5;

        public float StablePlanesSplitStopThreshold;
        public float Padding3;
        public uint Padding4;
        public float StablePlanesSuppressPrimaryIndirectSpecularK;

        public float DenoiserRadianceClampK;
        public float DLSSRRBrightnessClampK;
        public float StablePlanesAntiAliasingFallthrough;
        public uint ActiveStablePlaneCount;

        public uint MaxStablePlaneVertexDepth;
        public uint AllowPrimarySurfaceReplacement;
        public uint GenericTSLineStride;
        public uint GenericTSPlaneStride;

        public uint NEEEnabled;
        public uint NEEType;
        public uint NEECandidateSamples;
        public uint NEEFullSamples;

        public uint Padding6;
        public uint STFMagnificationMethod;
        public uint STFFilterMode;
        public float STFGaussianSigma;

        public PathTracingCameraData Camera;
        public PathTracingCameraData PrevCamera;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingDebugConstants
    {
        public int PickX;
        public int PickY;
        public int Pick;
        public float DebugLineScale;
        public uint ShowWireframe;
        public int DebugViewType;
        public int DebugViewStablePlaneIndex;
        public int ExploreDeltaTree;
        public int ImageWidth;
        public int ImageHeight;
        public int MouseX;
        public int MouseY;
        public Vector3 CameraPosW;
        public float Padding0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingSampleConstants
    {
        public PathTracingViewConstants View;
        public PathTracingViewConstants PreviousView;
        public PathTracingEnvMapSceneParams EnvMapSceneParams;
        public PathTracingEnvMapImportanceSamplingParams EnvMapImportanceSamplingParams;
        public PathTracerConstants PtConsts;
        public PathTracingDebugConstants Debug;
        public Vector4 DenoisingHitParamConsts;
        public uint MaterialCount;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;

        public static readonly int Stride = Marshal.SizeOf<PathTracingSampleConstants>();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingSampleMiniConstants
    {
        public Vector4Int Params;
        public Vector4Int Params1;
        public Vector4Int Params2;
        public Vector4Int Params3;

        public static readonly int Stride = Marshal.SizeOf<PathTracingSampleMiniConstants>();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Vector4Int
    {
        public uint X;
        public uint Y;
        public uint Z;
        public uint W;
    }
}
