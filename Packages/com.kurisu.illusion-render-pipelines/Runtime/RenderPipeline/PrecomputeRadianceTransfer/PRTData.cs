using System;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public enum SurfelDirection { PosX, NegX, PosY, NegY, PosZ, NegZ }

    [Serializable]
    public struct Surfel
    {
        public const int Stride = 56;
        public const uint SkyMiss = 1;
        public Vector3 position, normal, albedo;
        public uint flags, renderingLayerMask, objectLayerMask, materialKey;
        public int nearestProbe;
    }

    [Serializable]
    public struct SurfelIndices
    {
        public const int Stride = 8;
        public int start, count;
    }

    [Serializable]
    public struct FactorIndices
    {
        public const int Stride = 8;
        public int start, count;
        public FactorIndices(int start, int count) { this.start = start; this.count = count; }
    }

    [Serializable]
    public struct BrickFactor
    {
        public const int Stride = 40;
        public int brickIndex;
        public float sh0, sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8;

        public void AddDirection(Vector4 sample)
        {
            float x = sample.x, y = sample.z, z = sample.y, w = sample.w;
            sh0 += 0.2820947918f * w;
            sh1 -= 0.4886025119f * y * w;
            sh2 += 0.4886025119f * z * w;
            sh3 -= 0.4886025119f * x * w;
            sh4 += 1.0925484306f * x * y * w;
            sh5 -= 1.0925484306f * y * z * w;
            sh6 += 0.3153915653f * (3f * z * z - 1f) * w;
            sh7 -= 1.0925484306f * x * z * w;
            sh8 += 0.5462742153f * (x * x - y * y) * w;
        }

        public bool IsFinite => float.IsFinite(sh0) && float.IsFinite(sh1) && float.IsFinite(sh2) &&
            float.IsFinite(sh3) && float.IsFinite(sh4) && float.IsFinite(sh5) && float.IsFinite(sh6) &&
            float.IsFinite(sh7) && float.IsFinite(sh8);
    }

    [Serializable]
    public struct PRTSkySample
    {
        public const int Stride = 16;
        public Vector3 direction;
        public float weight;
    }

    [Serializable]
    public struct PRTProbeData
    {
        public const int Stride = 32;
        public int factorStart, factorCount, skyStart, skyCount;
        public Vector3 captureOffset;
        public uint validity;
    }

    [Serializable]
    public struct PRTProbeGrid : IEquatable<PRTProbeGrid>
    {
        public Vector3 origin;
        public Vector3Int min, count;
        public float spacing;
        public int ProbeCount => checked(count.x * count.y * count.z);
        public Vector3 GetPosition(int index)
        {
            int x = index / (count.y * count.z), y = index / count.z % count.y, z = index % count.z;
            return origin + (Vector3)(min + new Vector3Int(x, y, z)) * spacing;
        }
        public bool Equals(PRTProbeGrid other) => origin.Equals(other.origin) && min == other.min &&
            count == other.count && spacing.Equals(other.spacing);
        public override bool Equals(object obj) => obj is PRTProbeGrid other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(origin, min, count, spacing);
    }

    [Serializable]
    public struct PRTBakeSignature
    {
        public Hash128 geometry, materials, settings, authoringInputs;
        public string backend;
        public int sampleCount;
        public uint seed;
        public float sceneTime;
    }

    public static class PRTProbeValidity
    {
        public static uint Pack(float intensity, float validity)
        {
            if (!float.IsFinite(intensity) || !float.IsFinite(validity))
                return 0;
            return (uint)(Mathf.Clamp01(intensity / 5f) * 16777215f) | ((uint)(Mathf.Clamp01(validity) * 255f) << 24);
        }
        public static Vector2 Unpack(uint value) => new(
            (value & 0x00FFFFFFu) * (5f / 16777215f), (value >> 24) * (1f / 255f));
    }

    [Serializable]
    public class PRTSectorData
    {
        public Vector2Int coordinate;
        public Bounds surfelBounds;
        public int[] probeIds = Array.Empty<int>();
        public Surfel[] surfels = Array.Empty<Surfel>();
        public SurfelIndices[] bricks = Array.Empty<SurfelIndices>();
        public BrickFactor[] factors = Array.Empty<BrickFactor>();
        public PRTProbeData[] probes = Array.Empty<PRTProbeData>();
        public PRTSkySample[] skySamples = Array.Empty<PRTSkySample>();
    }

    [Serializable]
    public struct PRTProbeMetadata
    {
        public Vector3 captureOffset;
        public uint validity;
    }

    public static class PRTBakeSampling
    {
        public static Vector4[] GenerateDirections(int count, uint seed)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var samples = new Vector4[count];
            float weight = 4f * Mathf.PI / count;
            for (uint i = 0; i < count; i++)
            {
                uint bits = i ^ seed;
                bits = (bits << 16) | (bits >> 16);
                bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
                bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
                bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
                bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
                // ref: Unreal Engine 4, MonteCarlo.ush
                float phi = 2f * Mathf.PI * (bits * 2.3283064365386963e-10f);
                float z = 1f - 2f * ((i + 0.5f) / count);
                float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                samples[i] = new Vector4(radius * Mathf.Cos(phi), radius * Mathf.Sin(phi), z, weight);
            }
            return samples;
        }
    }
}
