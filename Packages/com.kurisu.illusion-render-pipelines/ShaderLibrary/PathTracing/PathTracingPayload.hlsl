#ifndef ILLUSION_PATH_TRACING_PAYLOAD_INCLUDED
#define ILLUSION_PATH_TRACING_PAYLOAD_INCLUDED


#define PT_RAY_SCATTER                  0u
#define PT_RAY_RANDOM_WALK             2u
#define PT_RAY_VISIBILITY               1u
#define PT_RAY_KIND_MASK                0x3u
#define PT_RAY_SEED_SHIFT               4u

#define PT_FAMILY_LIT                   0u
#define PT_FAMILY_HAIR                  5u
#define PT_FAMILY_SKIN                  4u
#define PT_FAMILY_FABRIC                3u
#define PT_FAMILY_UNLIT                 1u
#define PT_FAMILY_DIAGNOSTIC            15u
#define PT_FAMILY_MASK                  0xFu

#define PT_SURFACE_FRONT_FACING         (1u << 4)
#define PT_SURFACE_THIN                 (1u << 5)
#define PT_SURFACE_BITANGENT_FLIP       (1u << 6)
#define PT_SURFACE_SPECULAR_WORKFLOW    (1u << 7)


#define PT_NO_POSITION_HISTORY          0xFFFFFFFFu

#define PT_MISS_T                       (-1.0)
#define PT_NO_SURFACE_T                 (-2.0)
#define PT_DIAGNOSTIC_RADIANCE          float3(1.0, 0.0, 1.0)

struct IllusionPathPayload
{
    uint    rayKindAndSeed;
    float   coneWidth;
    float   coneSpread;
    float   hitT;
    uint    familyAndFlags;
    uint    normalShading;
    uint    tangentShading;
    uint    normalVertex;
    uint    normalFace;
    uint2   diffuseOpacity;
    uint2   specularRoughness;
    uint2   emissionMetallic;
    uint4   parameters;
    uint4   familyParameters;
    uint    instanceID;
    uint    triangleIndex;
    uint2   motion;
};

struct PathTracingMaterialData
{
    float3 AttenuationColor;
    float AttenuationDistance;
    float IoR;
    uint Flags;
    uint2 Padding;
};
StructuredBuffer<PathTracingMaterialData> _PathTracingMaterials;
uint _PathTracingMaterialCount;

float PathTracingMaterialIoR(uint materialIndex)
{
    if (materialIndex >= _PathTracingMaterialCount)
        return 1.0;
    return _PathTracingMaterials[materialIndex].IoR;
}

uint PathTracingPackHalf2(float a, float b)
{
    return f32tof16(a) | (f32tof16(b) << 16);
}

float2 PathTracingUnpackHalf2(uint v)
{
    return float2(f16tof32(v & 0xFFFF), f16tof32(v >> 16));
}

uint2 PathTracingPackHalf4(float4 v)
{
    return uint2(PathTracingPackHalf2(v.x, v.y), PathTracingPackHalf2(v.z, v.w));
}

float4 PathTracingUnpackHalf4(uint2 v)
{
    return float4(PathTracingUnpackHalf2(v.x), PathTracingUnpackHalf2(v.y));
}

float2 PathTracingOctWrap(float2 v)
{
    return (1.0 - abs(v.yx)) * (v.xy >= 0.0 ? 1.0 : -1.0);
}

uint PathTracingPackNormal(float3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    float2 e = n.z >= 0.0 ? n.xy : PathTracingOctWrap(n.xy);
    e = saturate(e * 0.5 + 0.5);
    uint2 q = uint2(round(e * 65535.0));
    return q.x | (q.y << 16);
}

float3 PathTracingUnpackNormal(uint packed)
{
    float2 e = float2(packed & 0xFFFF, packed >> 16) / 65535.0 * 2.0 - 1.0;
    float3 n = float3(e, 1.0 - abs(e.x) - abs(e.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t : t;
    return normalize(n);
}

IllusionPathPayload PathTracingCreatePayload(uint rayKind, uint seed, float coneWidth, float coneSpread)
{
    IllusionPathPayload payload = (IllusionPathPayload)0;
    payload.rayKindAndSeed = (rayKind & PT_RAY_KIND_MASK) | (seed << PT_RAY_SEED_SHIFT);
    payload.coneWidth = coneWidth;
    payload.coneSpread = coneSpread;
    payload.hitT = PT_NO_SURFACE_T;
    return payload;
}

uint PathTracingGetRayKind(IllusionPathPayload payload)
{
    return payload.rayKindAndSeed & PT_RAY_KIND_MASK;
}

uint PathTracingGetFamily(IllusionPathPayload payload)
{
    return payload.familyAndFlags & PT_FAMILY_MASK;
}

bool PathTracingHasSurfaceFlag(IllusionPathPayload payload, uint flag)
{
    return (payload.familyAndFlags & flag) != 0;
}

#endif
