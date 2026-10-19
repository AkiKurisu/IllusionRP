#ifndef ILLUSION_PATH_TRACING_REORDERING_INCLUDED
#define ILLUSION_PATH_TRACING_REORDERING_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"

#if defined(ILLUSION_PT_SER)
#undef NV_SHADER_EXTN_SLOT
#define NV_SHADER_EXTN_SLOT u7
#define NV_HITOBJECT_USE_MACRO_API 1
#include "Packages/com.kurisu.unityrhi/ShaderLibrary/NVAPI/nvHLSLExtns.h"
#endif

void PathTracingTraceSceneRay(RaytracingAccelerationStructure accelerationStructure, uint rayFlags, uint instanceMask, RayDesc ray,
    inout IllusionPathPayload payload, bool reorder, uint coherenceHint)
{
#if defined(ILLUSION_PT_SER)
    NvHitObject hit;
    NvTraceRayHitObject(accelerationStructure, rayFlags, instanceMask, 0, 1, 0, ray, payload, hit);
    if (reorder)
        NvReorderThread(hit, coherenceHint, 1);
    NvInvokeHitObject(accelerationStructure, hit, payload);
#else
    TraceRay(accelerationStructure, rayFlags, instanceMask, 0, 1, 0, ray, payload);
#endif
}

#endif
