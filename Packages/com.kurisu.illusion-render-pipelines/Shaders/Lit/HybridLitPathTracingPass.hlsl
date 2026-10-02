#ifndef ILLUSION_HYBRID_LIT_PATH_TRACING_PASS_INCLUDED
#define ILLUSION_HYBRID_LIT_PATH_TRACING_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingLitSurface.hlsl"

#if defined(_SURFACE_TYPE_TRANSPARENT) && defined(_ALPHAPREMULTIPLY_ON)
#define HYBRID_LIT_PATH_TRACING_FILM
#endif

float2 HybridLitPathTracingUV(PathTracingHitContext hit)
{
    float2 uv = TRANSFORM_TEX(hit.vertex.texCoord0.xy, _BaseMap);
#if defined(_PARALLAXMAP)
    half3 viewDirTS = GetViewDirectionTangentSpace(half4(hit.tangentWS), hit.vertexNormalWS, hit.viewDirWS);
    ApplyPerPixelDisplacement(viewDirTS, uv);
#endif
    return uv;
}

PathTracingLitSurface HybridLitPathTracingSurface(PathTracingHitContext hit)
{
    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(HybridLitPathTracingUV(hit), surfaceData);

    PathTracingLitSurface surface = PathTracingInitLitSurface();
    surface.normalWS = PathTracingTangentToWorld(surfaceData.normalTS, hit);
    surface.albedo = surfaceData.albedo;
    surface.specular = surfaceData.specular;
    surface.metallic = surfaceData.metallic;
    surface.smoothness = surfaceData.smoothness;
    surface.emission = surfaceData.emission;
    surface.alpha = surfaceData.alpha;
    surface.coatMask = surfaceData.clearCoatMask;
#if defined(HYBRID_LIT_PATH_TRACING_FILM)
    surface.specularTransmission = 1.0 - surface.alpha;
    surface.ior = PathTracingIorFromF0(Luminance(PathTracingLitF0(surface)));
    surface.thin = true;
#endif
    return surface;
}

[shader("closesthit")]
void PathTracingClosestHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    PathTracingHitContext hit = PathTracingGetHitContext(attributes, payload);
    PathTracingWriteLitSurface(payload, hit, HybridLitPathTracingSurface(hit));
}

[shader("anyhit")]
void PathTracingAnyHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    if (!PathTracingAcceptShadowCaster(payload))
        return;
#if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)
#if defined(_SURFACE_TYPE_TRANSPARENT)
    if (PathTracingIsCulledFace())
    {
        IgnoreHit();
        return;
    }
#endif
    PathTracingHitContext hit = PathTracingGetHitContext(attributes, payload);
    g_PathTracingClipped = false;
    half alpha = Alpha(SampleAlbedoAlpha(HybridLitPathTracingUV(hit), TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
    if (g_PathTracingClipped)
    {
        IgnoreHit();
        return;
    }
    if (PathTracingGetRayKind(payload) == PT_RAY_VISIBILITY)
    {
#if defined(_SURFACE_TYPE_TRANSPARENT)
        PathTracingAccumulateShadowTransmission(payload, 1.0 - alpha);
#endif
        return;
    }
#if !defined(_SURFACE_TYPE_TRANSPARENT) || defined(HYBRID_LIT_PATH_TRACING_FILM)
    alpha = 1.0;
#endif
    if (!PathTracingAcceptCoverage(payload, alpha))
    {
        IgnoreHit();
        return;
    }

#endif
    PathTracingRecordRandomWalk(payload, attributes);
}

#endif
