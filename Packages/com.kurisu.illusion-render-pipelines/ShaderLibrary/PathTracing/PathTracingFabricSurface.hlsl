#ifndef ILLUSION_PATH_TRACING_FABRIC_SURFACE_INCLUDED
#define ILLUSION_PATH_TRACING_FABRIC_SURFACE_INCLUDED
#define PATH_TRACING_SURFACE_FAMILY PT_FAMILY_FABRIC
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingScreenInputs.hlsl"
struct PathTracingFabricSurface
{
    float3 normalWS;
    float3 albedo;
    float3 specular;
    float metallic;
    float smoothness;
    float3 emission;
    float alpha;
    float3 sheen;
    float anisotropy;
    float3 transmission;
    bool silk;
};
PathTracingFabricSurface PathTracingInitFabricSurface()
{
    PathTracingFabricSurface s = (PathTracingFabricSurface)0;
    s.normalWS = float3(0, 0, 1);
    s.albedo = 0.5;
    s.specular = 0.04;
    s.smoothness = 0.5;
    s.alpha = 1.0;
    return s;
}
void PathTracingWriteFabricSurface(inout IllusionPathPayload payload, PathTracingHitContext hit, PathTracingFabricSurface s)
{
#if defined(_SPECULAR_SETUP)
    float3 diffuse = s.albedo;
    float3 f0 = s.specular;
#else
    float3 diffuse = s.albedo * (1.0 - s.metallic);
    float3 f0 = lerp(0.04, s.albedo, s.metallic);
#endif
    PathTracingWriteGeometry(payload, hit, PT_FAMILY_FABRIC, PT_SURFACE_THIN, s.normalWS, hit.tangentWS);
    payload.diffuseOpacity = PathTracingPackHalf4(float4(diffuse, s.alpha));
    payload.specularRoughness = PathTracingPackHalf4(float4(s.silk ? f0 : s.sheen, 1.0 - s.smoothness));
    payload.emissionMetallic = PathTracingPackHalf4(float4(s.emission, s.metallic));
    payload.parameters = uint4(s.silk ? 1u : 0u, PathTracingPackHalf2(s.anisotropy, s.transmission.r), PathTracingPackHalf2(s.transmission.g, s.transmission.b), 0u);
}
#endif
