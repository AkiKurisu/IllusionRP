#ifndef ILLUSION_PATH_TRACING_TEMPLATE_PASS_INCLUDED
#define ILLUSION_PATH_TRACING_TEMPLATE_PASS_INCLUDED


Attributes PathTracingTemplateAttributes(PathTracingHitContext hit)
{
    Attributes input = (Attributes)0;
    input.positionOS = float4(hit.vertex.positionOS, 1.0);
    input.normalOS = hit.vertex.normalOS;
    input.tangentOS = hit.vertex.tangentOS;
    input.texcoord = hit.vertex.texCoord0;
    input.texcoord1 = hit.vertex.texCoord1;
    input.texcoord2 = hit.vertex.texCoord2;
    input.texcoord3 = hit.vertex.texCoord3;
    input.color = hit.vertex.color;
    return input;
}

PATH_TRACING_TEMPLATE_SURFACE PathTracingEvaluateTemplate(AttributeData attributes, IllusionPathPayload payload)
{
    g_PathTracingHit = PathTracingGetHitContext(attributes, payload);
    g_PathTracingScreenPosition = PathTracingPseudoScreenPosition(payload);
    g_PathTracingClipped = false;
    return frag(vert(PathTracingTemplateAttributes(g_PathTracingHit)));
}

[shader("closesthit")]
void PathTracingClosestHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
    PATH_TRACING_TEMPLATE_SURFACE surface = PathTracingEvaluateTemplate(attributes, payload);
    PathTracingWriteTemplateSurface(payload, g_PathTracingHit, surface);
}

[shader("anyhit")]
void PathTracingAnyHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
{
#if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT) || defined(_PATH_TRACING_TRANSMISSION_THIN)
#if (defined(_SURFACE_TYPE_TRANSPARENT) || defined(_PATH_TRACING_TRANSMISSION_THIN)) && !defined(_PATH_TRACING_TRANSMISSION_REFRACTIVE)
    if (PathTracingIsCulledFace())
    {
        IgnoreHit();
        return;
    }
#endif
    PATH_TRACING_TEMPLATE_SURFACE surface = PathTracingEvaluateTemplate(attributes, payload);
    if (g_PathTracingClipped || !PathTracingAcceptCoverage(payload, PathTracingTemplateCoverage(surface)))
    {
        IgnoreHit();
        return;
    }

#endif
    PathTracingRecordRandomWalk(payload, attributes);
}

#endif
