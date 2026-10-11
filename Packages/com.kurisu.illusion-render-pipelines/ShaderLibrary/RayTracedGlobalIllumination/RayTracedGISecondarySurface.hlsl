#ifndef ILLUSION_RAY_TRACED_GI_SECONDARY_SURFACE_INCLUDED
#define ILLUSION_RAY_TRACED_GI_SECONDARY_SURFACE_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/GI/ReSTIRGIParameters.h"

#define RTXDI_GI_PI 3.14159265358979

static const uint kSecondaryGBuffer_IsSpecularRay = 1;
static const uint kSecondaryGBuffer_IsDeltaSurface = 2;
static const uint kSecondaryGBuffer_IsEnvironmentMap = 4;

// The secondary surface record of the RTXDI sample. The emission field holds the radiance leaving the secondary surface
// toward the receiver, which final shading reuses as the initial sample.
struct SecondaryGBufferData
{
    float3 worldPos;
    uint normal;
    uint2 throughputAndFlags;
    uint diffuseAlbedo;
    uint specularAndRoughness;
    float3 emission;
    float pdf;
};

RWStructuredBuffer<SecondaryGBufferData> u_SecondaryGBuffer;
RWStructuredBuffer<RTXDI_PackedGIReservoir> u_GIReservoirs;

uint2 Pack_R16G16B16A16_FLOAT(float4 value)
{
    return uint2(f32tof16(value.x) | (f32tof16(value.y) << 16), f32tof16(value.z) | (f32tof16(value.w) << 16));
}

float4 Unpack_R16G16B16A16_FLOAT(uint2 value)
{
    return float4(f16tof32(value.x), f16tof32(value.x >> 16), f16tof32(value.y), f16tof32(value.y >> 16));
}

float2 RayTracedGIOctWrap(float2 v)
{
    return (1.0 - abs(v.yx)) * float2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

uint ndirToOctUnorm32(float3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    float2 p = n.z >= 0.0 ? n.xy : RayTracedGIOctWrap(n.xy);
    p = saturate(p * 0.5 + 0.5);
    return uint(p.x * 0xFFFE) | (uint(p.y * 0xFFFE) << 16);
}

float3 octToNdirUnorm32(uint packed)
{
    float2 p = float2(packed & 0xFFFF, packed >> 16) / 0xFFFE * 2.0 - 1.0;
    float3 n = float3(p, 1.0 - abs(p.x) - abs(p.y));
    float t = saturate(-n.z);
    n.xy += float2(n.x >= 0.0 ? -t : t, n.y >= 0.0 ? -t : t);
    return normalize(n);
}

#endif
