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

void PathTracingInlineTraceRay(RaytracingAccelerationStructure accelerationStructure, uint rayFlags, uint instanceMask,
    RayDesc ray, inout IllusionPathPayload payload)
{
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
        return;
    }
    if ((rayFlags & RAY_FLAG_SKIP_CLOSEST_HIT_SHADER) != 0)
        return;

    g_PathTracingInline.instanceID = query.CommittedInstanceID();
    g_PathTracingInline.instanceIndex = query.CommittedInstanceIndex();
    g_PathTracingInline.primitiveIndex = query.CommittedPrimitiveIndex();
    g_PathTracingInline.geometryIndex = query.CommittedGeometryIndex();
    g_PathTracingInline.objectToWorld = query.CommittedObjectToWorld3x4();
    g_PathTracingInline.worldToObject = query.CommittedWorldToObject3x4();
    g_PathTracingInline.t = query.CommittedRayT();
    AttributeData attributes;
    attributes.barycentrics = query.CommittedTriangleBarycentrics();
    uint evaluator = PathTracingInlineLoadGeometry(g_PathTracingInline.instanceID, g_PathTracingInline.geometryIndex);
    if (evaluator != PT_INLINE_NO_EVALUATOR)
        PathTracingInlineEvaluateClosestHit(evaluator, payload, attributes);
}

#endif
