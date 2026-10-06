using System;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public enum SurfelDirection { PosX, NegX, PosY, NegY, PosZ, NegZ }

    public struct PRTCaptureSample
    {
        public const int Stride = 56;
        public const uint SkyMiss = 1;
        public Vector3 position, normal, albedo;
        public uint flags, renderingLayerMask, objectLayerMask, materialKey;
        public int nearestProbe;
    }

    // Layout shared with Surfel in ProbeVolume.hlsl. Positions are unorm16 within the sector's surfel bounds, normals
    // octahedral unorm12 and albedo sRGB unorm8.
    [Serializable]
    public struct Surfel
    {
        public const int Stride = 16;
        public uint positionXY, positionZAlbedoRG, normalAlbedoB;
        public int nearestProbe;

        public static Surfel Create(Vector3 position, Vector3 normal, Vector3 albedo, Bounds bounds, int nearestProbe)
        {
            Vector3 size = bounds.size, min = bounds.min;
            uint Axis(int axis) => size[axis] > 0 ? (uint)Mathf.RoundToInt(Mathf.Clamp01((position[axis] - min[axis]) / size[axis]) * 65535f) : 0u;
            uint Color(float value) => (uint)Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(value)) * 255f);
            Vector2 e = EncodeNormal(normal);
            uint nx = (uint)Mathf.RoundToInt((e.x * 0.5f + 0.5f) * 4095f), ny = (uint)Mathf.RoundToInt((e.y * 0.5f + 0.5f) * 4095f);
            return new Surfel
            {
                positionXY = Axis(0) | Axis(1) << 16,
                positionZAlbedoRG = Axis(2) | Color(albedo.x) << 16 | Color(albedo.y) << 24,
                normalAlbedoB = nx | ny << 12 | Color(albedo.z) << 24,
                nearestProbe = nearestProbe
            };
        }

        public Vector3 Position(Bounds bounds)
        {
            var q = new Vector3(positionXY & 0xFFFF, positionXY >> 16, positionZAlbedoRG & 0xFFFF) / 65535f;
            return bounds.min + Vector3.Scale(q, bounds.size);
        }

        public Vector3 Normal
        {
            get
            {
                var e = new Vector2((normalAlbedoB & 0xFFF) / 4095f * 2f - 1f, (normalAlbedoB >> 12 & 0xFFF) / 4095f * 2f - 1f);
                var n = new Vector3(e.x, e.y, 1f - Mathf.Abs(e.x) - Mathf.Abs(e.y));
                float t = Mathf.Clamp01(-n.z);
                n.x += n.x >= 0 ? -t : t;
                n.y += n.y >= 0 ? -t : t;
                return n.normalized;
            }
        }

        public Vector3 Albedo => new(Mathf.GammaToLinearSpace((positionZAlbedoRG >> 16 & 0xFF) / 255f),
            Mathf.GammaToLinearSpace((positionZAlbedoRG >> 24) / 255f), Mathf.GammaToLinearSpace((normalAlbedoB >> 24) / 255f));

        private static Vector2 EncodeNormal(Vector3 n)
        {
            n /= Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            var e = new Vector2(n.x, n.y);
            if (n.z < 0)
                e = new Vector2((1f - Mathf.Abs(n.y)) * (n.x >= 0 ? 1 : -1), (1f - Mathf.Abs(n.x)) * (n.y >= 0 ? 1 : -1));
            return new Vector2(Mathf.Clamp(e.x, -1, 1), Mathf.Clamp(e.y, -1, 1));
        }
    }

    [Serializable]
    public struct SurfelIndices
    {
        public const int Stride = 16;
        public int start, count;
        public uint renderingLayerMask, objectLayerMask;
    }

    [Serializable]
    public struct FactorIndices
    {
        public const int Stride = 8;
        public int start, count;
        public FactorIndices(int start, int count) { this.start = start; this.count = count; }
    }

    // Layout shared with BrickFactor in ProbeVolume.hlsl: a sector-local brick index and nine half coefficients.
    [Serializable]
    public struct BrickFactor
    {
        public const int Stride = 20;
        public const int MaxBricks = 65536;
        public uint brickSh0, sh12, sh34, sh56, sh78;

        public int BrickIndex => (int)(brickSh0 & 0xFFFF);

        public float this[int coefficient] => coefficient == 0
            ? Mathf.HalfToFloat((ushort)(brickSh0 >> 16))
            : Mathf.HalfToFloat((ushort)(Word(coefficient) >> ((coefficient - 1) % 2 * 16)));

        private uint Word(int coefficient) => ((coefficient - 1) / 2) switch { 0 => sh12, 1 => sh34, 2 => sh56, _ => sh78 };

        public bool IsFinite
        {
            get
            {
                for (int i = 0; i < 9; i++) if (!float.IsFinite(this[i])) return false;
                return true;
            }
        }
    }

    // Bake-time accumulator of a probe's transfer from one brick.
    public struct BrickTransfer
    {
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

        public BrickFactor Encode()
        {
            static uint Pair(float a, float b) => (uint)Mathf.FloatToHalf(a) | (uint)Mathf.FloatToHalf(b) << 16;
            return new BrickFactor
            {
                brickSh0 = (uint)brickIndex | (uint)Mathf.FloatToHalf(sh0) << 16,
                sh12 = Pair(sh1, sh2), sh34 = Pair(sh3, sh4), sh56 = Pair(sh5, sh6), sh78 = Pair(sh7, sh8)
            };
        }
    }

    [Serializable]
    public struct PRTProbeData
    {
        public const int Stride = 24;
        public int factorStart, factorCount;
        public Vector3 captureOffset;
        public uint validity;
    }

    // Sky visibility is one bit per bake direction; the directions are regenerated from the bake signature.
    public static class PRTSkyVisibility
    {
        public static int Words(int sampleCount) => (sampleCount + 31) / 32;
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
        public uint[] skyVisibility = Array.Empty<uint>();
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
