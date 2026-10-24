#ifndef ILLUSION_PATH_TRACING_HIT_RULES_INCLUDED
#define ILLUSION_PATH_TRACING_HIT_RULES_INCLUDED

uint PathTracingHash(uint h)
{
    h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
    return h;
}

uint PathTracingHitHash(IllusionPathPayload payload, uint salt)
{
    return PathTracingHash((payload.rayKindAndSeed >> PT_RAY_SEED_SHIFT) ^ (PrimitiveIndex() * 0x9E3779B9u) ^ (InstanceIndex() * 0x85EBCA6Bu) ^ salt);
}

bool PathTracingAcceptCoverage(IllusionPathPayload payload, float alpha)
{
    return float(PathTracingHitHash(payload, 0u) & 0xFFFFFF) / 16777216.0 < alpha;
}

#define PT_CULL_BACK    1u
#define PT_CULL_FRONT   2u

bool PathTracingIsCulledFace()
{
    uint range = _PathTracingInstanceData[InstanceID()].culledSubMeshes;
    uint first = range >> 8u;
    uint end = first + (range & 0xFFu);
    uint indexStart = GetMeshInfo().indexStart;
    uint culled = 0u;
    for (uint i = first; i < end; i++)
    {
        uint2 subMesh = _PathTracingCulledSubMeshes[i];
        if (subMesh.x == indexStart)
        {
            culled = subMesh.y;
            break;
        }
    }
    if (culled == 0u)
        return false;
    uint3 indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 p0 = UnityRayTracingFetchVertexAttribute3(indices.x, kVertexAttributePosition);
    float3 p1 = UnityRayTracingFetchVertexAttribute3(indices.y, kVertexAttributePosition);
    float3 p2 = UnityRayTracingFetchVertexAttribute3(indices.z, kVertexAttributePosition);
    float3 faceNormalWS = mul(cross(p1 - p0, p2 - p0), (float3x3)WorldToObject3x4());
    bool frontFacing = dot(faceNormalWS, WorldRayDirection()) < 0.0;
    return (culled & (frontFacing ? PT_CULL_FRONT : PT_CULL_BACK)) != 0u;
}

// Shadow rays accept a caster only when it shares a shadow layer with the light; payload.parameters.x holds those layers.
bool PathTracingAcceptShadowCaster(IllusionPathPayload payload)
{
    if (PathTracingGetRayKind(payload) != PT_RAY_VISIBILITY
        || (_PathTracingInstanceData[InstanceID()].renderingLayers & payload.parameters.x) != 0)
        return true;
    IgnoreHit();
    return false;
}

// Shadow rays carry their RGB transmission in diffuseOpacity and pass through surfaces that still transmit light.
void PathTracingAccumulateShadowTransmission(inout IllusionPathPayload payload, float3 transmittance)
{
    float3 value = PathTracingUnpackHalf4(payload.diffuseOpacity).rgb * saturate(transmittance);
    payload.diffuseOpacity = PathTracingPackHalf4(float4(value, 0.0));
    if (any(value > 0.0))
        IgnoreHit();
}

// Random walks exit through the nearest surface of their own scattering group; payload.parameters.w holds the group.
bool PathTracingRandomWalkRecords(IllusionPathPayload payload)
{
    if (PathTracingGetRayKind(payload) != PT_RAY_RANDOM_WALK)
        return false;
    if (_PathTracingInstanceData[InstanceID()].scatteringGroup != payload.parameters.w)
    {
        IgnoreHit();
        return false;
    }
    return RayTCurrent() < payload.hitT;
}

#endif
