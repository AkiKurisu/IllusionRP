#ifndef ILLUSION_PATH_TRACING_MULTIPLY_OVERLAY_PASS_INCLUDED
#define ILLUSION_PATH_TRACING_MULTIPLY_OVERLAY_PASS_INCLUDED

// A layer that raster multiplies over the surfaces behind it. Camera chains take its factor on front faces and continue
// behind it; every other ray passes through. The including pass defines float3 PathTracingOverlayFactor(PathTracingHitContext hit).

[shader("closesthit")]
void PathTracingClosestHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    PathTracingHitContext hit = PathTracingGetHitContext(attributes, payload);
    float3 factor = hit.frontFacing ? PathTracingOverlayFactor(hit) : 1.0;
    PathTracingWriteGeometry(payload, hit, PT_FAMILY_OVERLAY, PT_SURFACE_THIN, hit.vertexNormalWS, hit.tangentWS);
    payload.diffuseOpacity = PathTracingPackHalf4(float4(factor, 1.0));
}

[shader("anyhit")]
void PathTracingAnyHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    if (PathTracingGetRayKind(payload) != PT_RAY_SCATTER || !PathTracingIsCameraChain(payload))
        IgnoreHit();
}

#endif
