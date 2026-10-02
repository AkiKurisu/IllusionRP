#ifndef ILLUSION_PATH_TRACING_HAIR_SURFACE_INCLUDED
#define ILLUSION_PATH_TRACING_HAIR_SURFACE_INCLUDED
#define PATH_TRACING_SURFACE_FAMILY PT_FAMILY_HAIR
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingScreenInputs.hlsl"
struct PathTracingHairSurface
{
    float3 normalWS;
    float3 tangentWS;
    float3 baseColor;
    float smoothness;
    float specular;
    float alpha;
    float coverage;
};
PathTracingHairSurface PathTracingInitHairSurface()
{
    PathTracingHairSurface s = (PathTracingHairSurface)0;
    s.normalWS = float3(0, 0, 1);
    s.tangentWS = float3(0, 1, 0);
    s.baseColor = 0.5;
    s.smoothness = 0.5;
    s.specular = 0.5;
    s.alpha = s.coverage = 1.0;
    return s;
}
void PathTracingWriteHairSurface(inout IllusionPathPayload payload, PathTracingHitContext hit, PathTracingHairSurface s)
{
    PathTracingWriteGeometry(payload, hit, PT_FAMILY_HAIR, PT_SURFACE_THIN, s.normalWS, float4(s.tangentWS, hit.tangentWS.w));
    payload.diffuseOpacity = PathTracingPackHalf4(float4(s.baseColor, s.alpha));
    // As in UE's path tracer, the fiber reflects 0.08 * specular at normal incidence.
    payload.specularRoughness = PathTracingPackHalf4(float4((0.08 * saturate(s.specular)).xxx, 1.0 - s.smoothness));
    payload.emissionMetallic = 0;
    payload.parameters = 0;
}
#endif
