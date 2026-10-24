#ifndef ILLUSION_SPEEDTREE7_PATH_TRACING_PASS_INCLUDED
#define ILLUSION_SPEEDTREE7_PATH_TRACING_PASS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingLitSurface.hlsl"

#ifdef SPEEDTREE_ALPHATEST
#define PATH_TRACING_STANDARD_COVERAGE PathTracingStandardCoverage(_MainTex, 0, 0, _Color, _Cutoff, 1, 0, 0)
#endif

half2 SpeedTree7PathTracingVertexData(PathTracingHitVertex vertex)
{
    half2 data = 0;
#ifdef EFFECT_HUE_VARIATION
    float3 treePosition = float3(ObjectToWorld3x4()[0].w, ObjectToWorld3x4()[1].w, ObjectToWorld3x4()[2].w);
    half hueVariationAmount = frac(treePosition.x + treePosition.y + treePosition.z);
    hueVariationAmount += frac(vertex.positionOS.x + vertex.normalOS.y + vertex.normalOS.x) * 0.5 - 0.3;
    data.x = saturate(hueVariationAmount * _HueVariation.a);
#endif
#ifdef GEOM_TYPE_BRANCH_DETAIL
    data.y = vertex.color.a == 0 ? vertex.texCoord2.z : 2.5;
#endif
    return data;
}

PathTracingLitSurface SpeedTree7PathTracingSurface(PathTracingHitContext hit)
{
    half2 vertexData = SpeedTree7PathTracingVertexData(PathTracingFetchVertex(hit.indices.x)) * hit.barycentrics.x
        + SpeedTree7PathTracingVertexData(PathTracingFetchVertex(hit.indices.y)) * hit.barycentrics.y
        + SpeedTree7PathTracingVertexData(PathTracingFetchVertex(hit.indices.z)) * hit.barycentrics.z;
    half4 vertexColor = _Color;
    vertexColor.rgb *= hit.vertex.color.r;
    half4 diffuse = SampleSpeedTree7AlbedoAlpha(hit.vertex.texCoord0.xy);
    SurfaceData surfaceData;
    InitializeSpeedTree7Surface(half3(hit.vertex.texCoord0.xy, vertexData.x),
        half3(hit.vertex.texCoord2.xy, vertexData.y), vertexColor, diffuse, surfaceData);

    PathTracingLitSurface surface = PathTracingInitLitSurface();
    surface.normalWS = PathTracingTangentToWorld(surfaceData.normalTS, hit);
    surface.albedo = surfaceData.albedo;
    surface.specular = surfaceData.specular;
    surface.smoothness = surfaceData.smoothness;
    surface.alpha = 1;
    return surface;
}

[shader("closesthit")]
void PathTracingClosestHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    PathTracingHitContext hit = PathTracingGetHitContext(attributes, payload);
    PathTracingWriteLitSurface(payload, hit, SpeedTree7PathTracingSurface(hit));
}

#ifdef SPEEDTREE_ALPHATEST
[shader("anyhit")]
void PathTracingAnyHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    if (!PathTracingAcceptShadowCaster(payload))
        return;
    PathTracingHitContext hit = PathTracingGetCoverageContext(attributes, payload);
    g_PathTracingClipped = false;
    SampleSpeedTree7AlbedoAlpha(hit.vertex.texCoord0.xy);
    if (g_PathTracingClipped)
    {
        IgnoreHit();
        return;
    }
    PathTracingRecordRandomWalk(payload, attributes);
}
#endif

#endif
