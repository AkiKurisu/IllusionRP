#ifndef ILLUSION_HDRP_SKIN_SAMPLING_INCLUDED
#define ILLUSION_HDRP_SKIN_SAMPLING_INCLUDED
#include "../PathTracingPayload.hlsl"
RaytracingAccelerationStructure SceneBVH;
static uint g_HDRPScatteringGroup;

void HDRPTraceRandomWalk(RayDesc ray, out float distance, out float3 normal)
{
    IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_RANDOM_WALK, 0, 0, 0);
    payload.hitT = asfloat(0x7F800000);
    payload.parameters.w = g_HDRPScatteringGroup;
    TraceRay(SceneBVH, RAY_FLAG_FORCE_NON_OPAQUE | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_CULL_FRONT_FACING_TRIANGLES, 0xFF, 0, 1, 0, ray, payload);
    distance = payload.hitT < 0.0 ? asfloat(0x7F800000) : payload.hitT;
    normal = PathTracingUnpackNormal(payload.normalVertex);
}
#endif
