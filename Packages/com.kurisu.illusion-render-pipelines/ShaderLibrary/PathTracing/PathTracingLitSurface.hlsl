#ifndef ILLUSION_PATH_TRACING_LIT_SURFACE_INCLUDED
#define ILLUSION_PATH_TRACING_LIT_SURFACE_INCLUDED

#define PATH_TRACING_SURFACE_FAMILY PT_FAMILY_LIT

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingScreenInputs.hlsl"

struct PathTracingLitSurface
{
    float3 normalWS;
    float3 albedo;
    float3 specular;
    float metallic;
    float smoothness;
    float3 emission;
    float alpha;
    float specularTransmission;
    float diffuseTransmission;
    float3 transmissionTint;
    float ior;
    bool thin;
    float coatMask;
};

PathTracingLitSurface PathTracingInitLitSurface()
{
    PathTracingLitSurface s;
    s.normalWS = float3(0, 0, 1);
    s.albedo = 0.5;
    s.specular = 0.04;
    s.metallic = 0.0;
    s.smoothness = 0.5;
    s.emission = 0.0;
    s.alpha = 1.0;
    s.specularTransmission = 0.0;
    s.diffuseTransmission = 0.0;
    s.transmissionTint = 1.0;
    s.ior = 1.5;
    s.thin = false;
    s.coatMask = 0.0;
    return s;
}

float3 PathTracingLitF0(PathTracingLitSurface s)
{
#if defined(_SPECULAR_SETUP)
    return s.specular;
#else
    return lerp(0.04, s.albedo, s.metallic);
#endif
}

void PathTracingWriteLitSurface(inout IllusionPathPayload payload, PathTracingHitContext hit, PathTracingLitSurface s)
{
    uint flags = s.thin ? PT_SURFACE_THIN : 0u;
#if defined(_SPECULAR_SETUP)
    flags |= PT_SURFACE_SPECULAR_WORKFLOW;
    float3 diffuse = s.albedo;
#else
    float3 diffuse = s.albedo * (1.0 - s.metallic);
#endif
    float3 f0 = PathTracingLitF0(s);

    PathTracingWriteGeometry(payload, hit, PT_FAMILY_LIT, flags, s.normalWS, hit.tangentWS);
    payload.diffuseOpacity = PathTracingPackHalf4(float4(diffuse, s.alpha));
    payload.specularRoughness = PathTracingPackHalf4(float4(f0, 1.0 - s.smoothness));
    payload.emissionMetallic = PathTracingPackHalf4(float4(s.emission, s.metallic));
    payload.parameters = uint4(
        PathTracingPackHalf2(s.coatMask, 0.0),
        PathTracingPackHalf2(s.specularTransmission, s.diffuseTransmission),
        PathTracingPackHalf2(s.ior, s.transmissionTint.r),
        PathTracingPackHalf2(s.transmissionTint.g, s.transmissionTint.b));
}

#endif
