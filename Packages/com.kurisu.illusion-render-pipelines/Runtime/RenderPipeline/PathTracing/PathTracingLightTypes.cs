using System.Runtime.InteropServices;
using UnityEngine;

namespace Illusion.Rendering.PathTracing
{

    internal enum PolymorphicLightType : uint
    {
        Sphere = 0,
        Triangle = 1,
        Directional = 2,
        Environment = 3,
        Point = 4,
        EnvironmentQuad = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PolymorphicLightInfo
    {
        public Vector3 Center;
        public uint ColorTypeAndFlags;
        public uint Direction1;
        public uint Direction2;
        public uint Scalars;
        public uint LogRadiance;

        public static readonly int Stride = Marshal.SizeOf<PolymorphicLightInfo>();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PolymorphicLightInfoEx
    {
        public uint IesProfileIndex;
        public uint PrimaryAxis;
        public uint CosConeAngleAndSoftness;
        public uint UniqueID;

        public static readonly int Stride = Marshal.SizeOf<PolymorphicLightInfoEx>();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingDirectionalLight
    {
        public Vector4 ColorIntensity;
        public Vector3 Direction;
        public float AngularSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LightsBakerConstants
    {
        public float DistantVsLocalRelativeImportance;
        public uint EnvMapImportanceMapMIPCount;
        public uint EnvMapImportanceMapResolution;
        public uint TriangleLightTaskCount;

        public uint FeedbackResolutionX;
        public uint FeedbackResolutionY;
        public uint BlendedFeedbackResolutionX;
        public uint BlendedFeedbackResolutionY;

        public uint MouseCursorPosX;
        public uint MouseCursorPosY;
        public Vector2 PrevOverCurrentViewportSize;

        public int DebugDrawType;
        public uint DebugDrawTileLights;
        public uint UpdateCounter;
        public uint DebugDrawFrustum;

        public float ImportanceBoostIntensityDelta;
        public float ImportanceBoostFrustumMul;
        public float ImportanceBoostFrustumFadeDistance;
        public float Padding3;

        public Vector3 SceneCameraPos;
        public float SceneAverageContentsDistance;

        public float DepthDisocclusionThreshold;
        public uint EnableMotionReprojection;
        public float ReservoirHistoryDropoff;
        public uint Padding0;

        public uint CurrentWeightsBufferOffset;
        public uint HistoricWeightsBufferOffset;
        public uint Padding1;
        public uint Padding2;

        public fixed float FrustumPlanes[6 * 4];
        public fixed float FrustumCorners[8 * 4];

        public PathTracingEnvMapSceneParams EnvMapParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LightingControlData
    {
        public uint TotalLightCount;
        public uint EnvmapQuadNodeCount;
        public uint AnalyticLightCount;
        public uint TriangleLightCount;

        public uint SamplingProxyCount;
        public uint HistoricTotalLightCount;
        public uint LastFrameTemporalFeedbackAvailable;
        public uint LastFrameLocalSamplesAvailable;

        public uint ProxyBuildTaskCount;
        public uint WeightsSumUINT;
        public uint ImportanceSamplingType;
        public uint Padding0;

        public uint TemporalFeedbackRequired;
        public uint TotalMaxFeedbackCount;
        public float GlobalFeedbackUseWeight;
        public float LocalToGlobalSampleRatio;

        public uint TileBufferHeight;
        public float ScreenSpaceVsWorldSpaceThreshold;
        public uint LocalSamplingResolutionX;
        public uint LocalSamplingResolutionY;

        public uint LocalSamplingTileJitterX;
        public uint LocalSamplingTileJitterY;
        public uint LocalSamplingTileJitterPrevX;
        public uint LocalSamplingTileJitterPrevY;

        public uint ValidFeedbackCount;
        public uint Padding1;
        public uint Padding2;
        public uint Padding3;

        public LightsBakerConstants BakerConstants;

        public static readonly int Stride = Marshal.SizeOf<LightingControlData>();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EnvMapImportanceSamplingBakerConstants
    {
        public uint SourceCubeDim;
        public uint SourceCubeMIPCount;
        public uint SampleIndex;
        public uint Padding1;

        public uint ImportanceMapDimX;
        public uint ImportanceMapDimY;
        public uint ImportanceMapDimInSamplesX;
        public uint ImportanceMapDimInSamplesY;

        public uint ImportanceMapNumSamplesX;
        public uint ImportanceMapNumSamplesY;
        public float ImportanceMapInvSamples;
        public uint ImportanceMapBaseMip;

        public static readonly int Stride = Marshal.SizeOf<EnvMapImportanceSamplingBakerConstants>();
    }

    internal static class PathTracingLightingConfig
    {
        public const uint InvalidLightIndex = 0xFFFFFFFF;
        public const int MaxLights = 512 * 1024;
        public const int SamplingProxyRatio = 12;
        public const int MaxSamplingProxies = SamplingProxyRatio * MaxLights;
        public const int WeightsCountHalf = MaxLights + 1;
        public const int ComputeThreads = 128;
        public const int ComputeThreads2D = 8;
        public const int LocalBlockSize = 32;
        public const int PreprocessBlockSizeInner = 14;
        public const int MaxProxiesPerTask = 32;
        public const int MaxProxyProcTasks = MaxLights + (MaxSamplingProxies + MaxProxiesPerTask - 1) / MaxProxiesPerTask;
        public const int ProxyBuildTaskStride = 16;
        public const int SamplingBufferTileSize = 8;
        public const int LocalProxyCount = 128;
        public const int EarlyFeedbackTileSize = 2;
        public const int EnvQuadBaseResolution = 4;
        public const int EnvQuadSubdivisions = 24;
        public const int EnvQuadUnboostedNodeCount = EnvQuadBaseResolution * EnvQuadBaseResolution + 3 * EnvQuadSubdivisions;
        public const int EnvQuadBoostNodesMultiplier = 20 * 3 + 1;
        public const int EnvQuadTotalNodeCount = EnvQuadUnboostedNodeCount * EnvQuadBoostNodesMultiplier;
        public const int ImportanceSamplingNEEAT = 2;
        public const float DistantLightDistance = 100000.0f;
    }
}
