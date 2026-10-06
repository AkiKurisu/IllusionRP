using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PRTWorldLightGpu
    {
        internal const int Stride = 96;
        internal Vector4 PositionType;
        internal Vector4 DirectionRange;
        internal Vector4 ColorShadowStrength;
        internal Vector4 Attenuation;
        internal uint RenderingLayers;
        internal uint ObjectLayers;
        internal uint CacheOffset;
        internal uint VisibilityEpoch;
        internal uint FaceOffset;
        internal uint FaceCount;
        internal uint LightId;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PRTShadowFaceGpu
    {
        internal const int Stride = 128;
        internal Matrix4x4 WorldToShadow;
        internal Vector4 AtlasRect;
        internal Vector4 Sphere;
        internal Vector4 ReceiverBias;
        internal uint Source;
        internal uint Slice;
        internal uint Face;
        internal uint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    // unorm8 visibility | 24-bit light visibility epoch << 8; epoch 0 marks an empty entry.
    internal struct PRTWorldShadowCacheEntry
    {
        internal const int Stride = 4;
        internal uint Packed;
    }

    internal struct PRTWorldLightSnapshot
    {
        internal Light Source;
        internal PRTWorldLightGpu Gpu;
        internal Matrix4x4 LocalToWorld;
        internal uint ShadowLayers;
        internal float ShadowNear;
        internal float DepthBias;
        internal float NormalBias;
        internal float SpotAngle;
        internal bool CastsShadows;
    }

    internal sealed class PRTRelightLightingSnapshot
    {
        internal readonly float SceneTime;
        internal readonly PRTWorldLightSnapshot[] Lights;

        internal PRTRelightLightingSnapshot(float sceneTime, PRTWorldLightSnapshot[] lights)
        {
            SceneTime = sceneTime;
            Lights = lights;
        }
    }

    internal struct PRTWorldLightingResources
    {
        internal GraphicsBuffer LightBuffer;
        internal GraphicsBuffer ShadowCacheBuffer;
        internal GraphicsBuffer ShadowFaceBuffer;
        internal GraphicsBuffer StatsBuffer;
        internal BufferHandle LightHandle;
        internal BufferHandle ShadowCacheHandle;
        internal BufferHandle ShadowFaceHandle;
        internal BufferHandle StatsHandle;
        internal TextureHandle MainShadowTexture;
        internal TextureHandle AdditionalShadowTexture;
        internal uint LightCount;
        internal uint SurfelCount;
        internal uint PreviewCacheOffset;
        internal bool CollectStats;
    }
}
