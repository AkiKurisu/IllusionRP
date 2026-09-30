#ifndef ILLUSION_HDRP_SKIN_SAMPLING_INCLUDED
#define ILLUSION_HDRP_SKIN_SAMPLING_INCLUDED
#include "../PathTracingPayload.hlsl"
#include "../RTXPT/PathTracer/Utils/SampleGenerators.hlsli"
RaytracingAccelerationStructure SceneBVH;

// @IllusionRP: HDRP's random walk uses RTXPT's per-vertex sample generator and Unity's material payload.
float4 RTXPTSample4D(uint2 pixel, uint index, uint dimension)
{
    return SampleSequenceGenerator::Generate(4, SampleGeneratorVertexBase::make(pixel, g_HDRPVertexIndex, index), (SampleGeneratorEffectSeed)dimension);
}
void HDRPTraceRandomWalk(RayDesc ray, out float distance, out float3 normal)
{
    IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_RANDOM_WALK, 0, 0, 0);
    payload.hitT = 3.402823466e+38;
    TraceRay(SceneBVH, RAY_FLAG_FORCE_NON_OPAQUE | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | RAY_FLAG_CULL_FRONT_FACING_TRIANGLES, 0xFF, 0, 1, 0, ray, payload);
    distance = payload.hitT < 0.0 ? 3.402823466e+38 : payload.hitT;
    normal = PathTracingUnpackNormal(payload.normalVertex);
}
#endif
