#ifndef ILLUSION_PATH_TRACING_INVISIBLE_PASS_INCLUDED
#define ILLUSION_PATH_TRACING_INVISIBLE_PASS_INCLUDED

// @IllusionRP: raster-only presentation surfaces do not intersect PT rays.

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingHit.hlsl"

[shader("closesthit")]
void PathTracingClosestHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
}

[shader("anyhit")]
void PathTracingAnyHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    IgnoreHit();
}

#endif
