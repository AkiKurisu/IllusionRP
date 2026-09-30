using UnityEngine;

namespace Illusion.Rendering.PathTracing
{
    internal static class PathTracingLightPacking
    {
        private const int TypeShift = 24;
        private const uint ShapingEnableBit = 1u << 28;
        private const float MinLog2Radiance = -8.0f;
        private const float MaxLog2Radiance = 40.0f;

        public static uint TypeBits(PolymorphicLightType type) => (uint)type << TypeShift;

        public static uint ShapingBit => ShapingEnableBit;

        public static uint Half(float value) => Mathf.FloatToHalf(value);

        public static uint Halves(float low, float high) => Half(low) | (Half(high) << 16);

        public static void PackColor(Vector3 radiance, ref PolymorphicLightInfo info)
        {
            float maxRadiance = Mathf.Max(radiance.x, Mathf.Max(radiance.y, radiance.z));
            if (maxRadiance <= 0.0f)
                return;

            float logRadiance = Mathf.Clamp01((Mathf.Log(maxRadiance, 2.0f) - MinLog2Radiance) / (MaxLog2Radiance - MinLog2Radiance));
            uint packedRadiance = (uint)Mathf.Min(Mathf.CeilToInt(logRadiance * 65534.0f) + 1, 0xFFFF);
            float unpackedRadiance = Mathf.Pow(2.0f, (packedRadiance - 1) / 65534.0f * (MaxLog2Radiance - MinLog2Radiance) + MinLog2Radiance);

            info.ColorTypeAndFlags |= PackUnorm8(radiance.x / unpackedRadiance)
                                      | (PackUnorm8(radiance.y / unpackedRadiance) << 8)
                                      | (PackUnorm8(radiance.z / unpackedRadiance) << 16);
            info.LogRadiance |= packedRadiance;
        }

        public static uint PackDirection(Vector3 direction)
        {
            direction /= Mathf.Abs(direction.x) + Mathf.Abs(direction.y) + Mathf.Abs(direction.z);
            Vector2 p = new Vector2(direction.x, direction.y);
            if (direction.z < 0.0f)
            {
                p = new Vector2((1.0f - Mathf.Abs(direction.y)) * (direction.x >= 0.0f ? 1.0f : -1.0f),
                    (1.0f - Mathf.Abs(direction.x)) * (direction.y >= 0.0f ? 1.0f : -1.0f));
            }
            p = p * 0.5f + new Vector2(0.5f, 0.5f);
            p = new Vector2(Mathf.Clamp01(p.x * 0.5f + 0.5f), Mathf.Clamp01(p.y * 0.5f + 0.5f));
            return (uint)(p.x * 0xFFFE) | ((uint)(p.y * 0xFFFE) << 16);
        }

        private static uint PackUnorm8(float value)
        {
            return (uint)Mathf.FloorToInt(Mathf.Clamp01(value) * 255.0f + 0.5f) & 0xFF;
        }
    }
}
