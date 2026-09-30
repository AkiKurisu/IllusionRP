#ifndef ILLUSION_PATH_TRACING_SKIN_SURFACE_INCLUDED
#define ILLUSION_PATH_TRACING_SKIN_SURFACE_INCLUDED
#define PATH_TRACING_SURFACE_FAMILY PT_FAMILY_SKIN
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingScreenInputs.hlsl"
struct PathTracingSkinSurface
{
    float3 normalWS;
    float3 albedo;
    float3 f0;
    float smoothness;
    float metallic;
    float3 emission;
    float alpha;
    float subsurfaceMask;
    uint diffusionProfileIndex;
};
PathTracingSkinSurface PathTracingInitSkinSurface()
{
    PathTracingSkinSurface s = (PathTracingSkinSurface)0;
    s.normalWS = float3(0, 0, 1);
    s.albedo = 0.5;
    s.f0 = 0.028;
    s.smoothness = 0.5;
    s.alpha = 1.0;
    s.subsurfaceMask = 1.0;
    return s;
}
void PathTracingWriteSkinSurface(inout IllusionPathPayload payload, PathTracingHitContext hit, PathTracingSkinSurface s)
{
    PathTracingWriteGeometry(payload, hit, PT_FAMILY_SKIN, 0, s.normalWS, hit.tangentWS);
    payload.diffuseOpacity = PathTracingPackHalf4(float4(s.albedo, s.alpha));
    payload.specularRoughness = PathTracingPackHalf4(float4(s.f0, 1.0 - s.smoothness));
    payload.emissionMetallic = PathTracingPackHalf4(float4(s.emission, s.metallic));
    payload.parameters = uint4(s.diffusionProfileIndex, PathTracingPackHalf2(s.subsurfaceMask, 0.0), 0u, 0u);
}
#endif
