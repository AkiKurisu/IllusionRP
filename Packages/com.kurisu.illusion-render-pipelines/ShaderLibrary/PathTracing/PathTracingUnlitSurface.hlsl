#ifndef ILLUSION_PATH_TRACING_UNLIT_SURFACE_INCLUDED
#define ILLUSION_PATH_TRACING_UNLIT_SURFACE_INCLUDED

#define PATH_TRACING_SURFACE_FAMILY PT_FAMILY_UNLIT

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingScreenInputs.hlsl"

struct PathTracingUnlitSurface
{
    float3 color;
    float3 bakedEmission;
    float alpha;
};

PathTracingUnlitSurface PathTracingInitUnlitSurface()
{
    PathTracingUnlitSurface s;
    s.color = 0.5;
    s.bakedEmission = 0.0;
    s.alpha = 1.0;
    return s;
}

void PathTracingWriteUnlitSurface(inout IllusionPathPayload payload, PathTracingHitContext hit, PathTracingUnlitSurface s)
{
    PathTracingWriteGeometry(payload, hit, PT_FAMILY_UNLIT, 0, hit.vertexNormalWS, hit.tangentWS);
    payload.diffuseOpacity = PathTracingPackHalf4(float4(s.color, s.alpha));
    payload.specularRoughness = PathTracingPackHalf4(float4(0.0, 0.0, 0.0, 1.0));
    payload.emissionMetallic = PathTracingPackHalf4(float4(s.bakedEmission, 0.0));
    payload.parameters = uint4(0u, 0u, PathTracingPackHalf2(1.0, 1.0), PathTracingPackHalf2(1.0, 1.0));
}

#endif
