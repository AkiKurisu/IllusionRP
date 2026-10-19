#ifndef ILLUSION_PATH_TRACING_INLINE_TRAVERSAL_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_TRAVERSAL_INCLUDED

uint PathTracingInlineLoadGeometry(uint instanceID, uint geometryIndex)
{
    uint first = _PathTracingInlineInstanceGeometries[instanceID];
    if (first == PT_INLINE_NO_EVALUATOR)
        return PT_INLINE_NO_EVALUATOR;
    g_PathTracingInlineGeometry = _PathTracingInlineGeometries[first + geometryIndex];
    g_PathTracingInlineMaterialBase = g_PathTracingInlineGeometry.materialData;
    return g_PathTracingInlineGeometry.evaluator;
}

struct PathTracingInlineHit
{
    uint instanceID;
    uint instanceIndex;
    uint primitiveIndex;
    uint geometryIndex;
    float t;
    float2 barycentrics;
    float3x4 objectToWorld;
    float3x4 worldToObject;
};

void PathTracingInlineSetHit(PathTracingInlineHit hit, RayDesc ray, out AttributeData attributes)
{
    g_PathTracingInline.rayOrigin = ray.Origin;
    g_PathTracingInline.rayDirection = ray.Direction;
    g_PathTracingInline.instanceID = hit.instanceID;
    g_PathTracingInline.instanceIndex = hit.instanceIndex;
    g_PathTracingInline.primitiveIndex = hit.primitiveIndex;
    g_PathTracingInline.geometryIndex = hit.geometryIndex;
    g_PathTracingInline.objectToWorld = hit.objectToWorld;
    g_PathTracingInline.worldToObject = hit.worldToObject;
    g_PathTracingInline.t = hit.t;
    attributes.barycentrics = hit.barycentrics;
}

bool PathTracingInlineTraceHit(RaytracingAccelerationStructure accelerationStructure, uint rayFlags, uint instanceMask,
    RayDesc ray, inout IllusionPathPayload payload, out PathTracingInlineHit hit)
{
    hit = (PathTracingInlineHit)0;
    RayQuery<RAY_FLAG_NONE> query;
    query.TraceRayInline(accelerationStructure, rayFlags, instanceMask, ray);
    g_PathTracingInline.rayOrigin = ray.Origin;
    g_PathTracingInline.rayDirection = ray.Direction;
    while (query.Proceed())
    {
        if (query.CandidateType() != CANDIDATE_NON_OPAQUE_TRIANGLE)
            continue;
        g_PathTracingInline.instanceID = query.CandidateInstanceID();
        g_PathTracingInline.instanceIndex = query.CandidateInstanceIndex();
        g_PathTracingInline.primitiveIndex = query.CandidatePrimitiveIndex();
        g_PathTracingInline.geometryIndex = query.CandidateGeometryIndex();
        g_PathTracingInline.objectToWorld = query.CandidateObjectToWorld3x4();
        g_PathTracingInline.worldToObject = query.CandidateWorldToObject3x4();
        g_PathTracingInline.t = query.CandidateTriangleRayT();
        AttributeData attributes;
        attributes.barycentrics = query.CandidateTriangleBarycentrics();
        uint evaluator = PathTracingInlineLoadGeometry(g_PathTracingInline.instanceID, g_PathTracingInline.geometryIndex);
        bool accepted = evaluator == PT_INLINE_NO_EVALUATOR || PathTracingInlineEvaluateAnyHit(evaluator, payload, attributes);
        if (accepted)
        {
            query.CommitNonOpaqueTriangleHit();
            if ((rayFlags & RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH) != 0)
                query.Abort();
        }
    }

    if (query.CommittedStatus() != COMMITTED_TRIANGLE_HIT)
    {
        payload.hitT = PT_MISS_T;
        return false;
    }
    hit.instanceID = query.CommittedInstanceID();
    hit.instanceIndex = query.CommittedInstanceIndex();
    hit.primitiveIndex = query.CommittedPrimitiveIndex();
    hit.geometryIndex = query.CommittedGeometryIndex();
    hit.t = query.CommittedRayT();
    hit.barycentrics = query.CommittedTriangleBarycentrics();
    hit.objectToWorld = query.CommittedObjectToWorld3x4();
    hit.worldToObject = query.CommittedWorldToObject3x4();
    return true;
}

void PathTracingInlineTraceRay(RaytracingAccelerationStructure accelerationStructure, uint rayFlags, uint instanceMask,
    RayDesc ray, inout IllusionPathPayload payload)
{
    PathTracingInlineHit hit;
    if (!PathTracingInlineTraceHit(accelerationStructure, rayFlags, instanceMask, ray, payload, hit)
        || (rayFlags & RAY_FLAG_SKIP_CLOSEST_HIT_SHADER) != 0)
        return;
    AttributeData attributes;
    PathTracingInlineSetHit(hit, ray, attributes);
    uint evaluator = PathTracingInlineLoadGeometry(hit.instanceID, hit.geometryIndex);
    if (evaluator != PT_INLINE_NO_EVALUATOR)
        PathTracingInlineEvaluateClosestHit(evaluator, payload, attributes);
}

#endif
