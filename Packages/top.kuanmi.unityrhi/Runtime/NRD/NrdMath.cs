using System;
using UnityEngine;

namespace UnityRhi.Nrd
{
    /// <summary>
    /// Unity (left-handed, Y-up) to NRD-sample world (right-handed, Z-up)
    /// conversion plus the CPU packing helpers the sample uses when building
    /// GPU buffers: IEEE-754 half conversion (round-to-nearest-even, as
    /// float16_t construction does), Packing::EncodeUnitVector (octahedral)
    /// and the Halton sequence used for TAA jitter.
    ///
    /// The basis change S swaps Y and Z: p' = (x, z, y). S is orthonormal and
    /// involutory (S == S^-1) but mirroring (det == -1), so triangle winding
    /// is re-flipped at index-extraction time and tangent handedness signs
    /// are negated to keep the geometry identical to what Unity renders.
    /// The sample's sky model hardcodes Z as up (GetSkyIntensity uses v.z),
    /// which is why the scene cannot simply stay in Unity coordinates.
    /// </summary>
    public static class NrdMath
    {
        public static Vector3 ToNrd(Vector3 v) => new Vector3(v.x, v.z, v.y);

        /// <summary>S * m * S: re-expresses a Unity world transform in NRD coordinates.</summary>
        public static Matrix4x4 ToNrd(Matrix4x4 m)
        {
            // Conjugating by the Y/Z swap permutes both rows and columns 1<->2.
            var r = Matrix4x4.identity;
            r.m00 = m.m00; r.m01 = m.m02; r.m02 = m.m01; r.m03 = m.m03;
            r.m10 = m.m20; r.m11 = m.m22; r.m12 = m.m21; r.m13 = m.m23;
            r.m20 = m.m10; r.m21 = m.m12; r.m22 = m.m11; r.m23 = m.m13;
            r.m30 = m.m30; r.m31 = m.m32; r.m32 = m.m31; r.m33 = m.m33;
            return r;
        }

        public static float MaxScale(Matrix4x4 m)
        {
            float sx = new Vector3(m.m00, m.m10, m.m20).magnitude;
            float sy = new Vector3(m.m01, m.m11, m.m21).magnitude;
            float sz = new Vector3(m.m02, m.m12, m.m22).magnitude;
            return Mathf.Max(sx, Mathf.Max(sy, sz));
        }

        /// <summary>Rows of a 3x4 (row-major) transform, as DXR instance/geometry descs expect.</summary>
        public static float[] To3x4(Matrix4x4 m) => new[]
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
        };

        public static ushort FloatToHalf(float value)
        {
            uint x = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            uint sign = (x >> 16) & 0x8000u;
            int exp = (int)((x >> 23) & 0xFFu) - 127 + 15;
            uint mant = x & 0x7FFFFFu;

            if (((x >> 23) & 0xFFu) == 0xFFu) // Inf / NaN
                return (ushort)(sign | 0x7C00u | (mant != 0 ? 0x200u : 0u));

            if (exp >= 31) // overflow -> Inf
                return (ushort)(sign | 0x7C00u);

            if (exp <= 0) // subnormal / zero
            {
                if (exp < -10) return (ushort)sign;
                mant |= 0x800000u;
                int shift = 14 - exp;
                uint h = mant >> shift;
                uint remainder = mant & ((1u << shift) - 1u);
                uint halfway = 1u << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (h & 1u) != 0u)) h++;
                return (ushort)(sign | h);
            }

            {
                uint h = ((uint)exp << 10) | (mant >> 13);
                uint remainder = mant & 0x1FFFu;
                if (remainder > 0x1000u || (remainder == 0x1000u && (h & 1u) != 0u)) h++;
                return (ushort)(sign | h); // rounding carry propagates into the exponent naturally
            }
        }

        // Octahedron packing for unit vectors, matching Packing::EncodeUnitVector.
        public static Vector2 EncodeUnitVector(Vector3 v, bool bSigned)
        {
            float norm = Mathf.Abs(v.x) + Mathf.Abs(v.y) + Mathf.Abs(v.z);
            v /= norm;

            Vector2 octWrap = new Vector2(
                (1.0f - Mathf.Abs(v.y)) * Sign(v.x),
                (1.0f - Mathf.Abs(v.x)) * Sign(v.y));

            Vector2 xy = v.z >= 0.0f ? new Vector2(v.x, v.y) : octWrap;
            return bSigned ? xy : 0.5f * xy + new Vector2(0.5f, 0.5f);
        }

        private static float Sign(float x) => x >= 0.0f ? 1.0f : -1.0f;

        /// <summary>Sequence::Halton2D equivalent; returns values in [0, 1).</summary>
        public static Vector2 Halton2D(uint index) =>
            new Vector2(Halton(index + 1, 2), Halton(index + 1, 3));

        private static float Halton(uint index, uint radix)
        {
            float result = 0f;
            float fraction = 1f / radix;
            while (index > 0)
            {
                result += (index % radix) * fraction;
                index /= radix;
                fraction /= radix;
            }
            return result;
        }

        /// <summary>Sun tangent basis, ported from the sample's GetBasis.</summary>
        public static void GetBasis(Vector3 n, out Vector3 t, out Vector3 b)
        {
            float sz = n.z >= 0f ? 1f : -1f;
            float a = 1.0f / (sz + n.z);
            float ya = n.y * a;
            float bb = n.x * ya;
            float c = n.x * sz;
            t = new Vector3(c * n.x * a - 1.0f, sz * bb, c);
            b = new Vector3(bb, n.y * ya - sz, n.y);
        }
    }
}
