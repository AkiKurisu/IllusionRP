#ifndef ILLUSION_PATH_TRACING_INLINE_STANDARD_COVERAGE_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_STANDARD_COVERAGE_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingHitRules.hlsl"

// Descriptor: x = flags with the texture coordinate set in bits 4-5, y = texture slot,
// z = color offset | texture scale-offset offset << 16 (0xFFFF when none), w = cutoff offset; offsets are bytes into the material record.
#define PT_STANDARD_COVERAGE        0x80000000u
#define PT_STANDARD_COVERAGE_TEST   1u
#define PT_STANDARD_COVERAGE_BLEND  2u
#define PT_STANDARD_COVERAGE_FULL   4u

float PathTracingInlineStandardAlpha(uint4 coverage, AttributeData attributes)
{
    uint3 indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    uint attribute = kVertexAttributeTexCoord0 + ((coverage.x >> 4) & 3u);
    float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);
    float2 uv = UnityRayTracingFetchVertexAttribute2(indices.x, attribute) * bary.x
        + UnityRayTracingFetchVertexAttribute2(indices.y, attribute) * bary.y
        + UnityRayTracingFetchVertexAttribute2(indices.z, attribute) * bary.z;

    uint base = g_PathTracingInlineMaterialBase;
    float4 scaleOffset = float4(1.0, 1.0, 0.0, 0.0);
    if ((coverage.z >> 16) != 0xFFFFu)
        scaleOffset = asfloat(_PathTracingInlineMaterialData.Load4(base + (coverage.z >> 16)));
    float colorAlpha = asfloat(_PathTracingInlineMaterialData.Load(base + (coverage.z & 0xFFFFu) + 12u));
    float textureAlpha = PathTracingInlineSampleGrad(coverage.y, PT_INLINE_SAMPLER_DEFAULT, uv * scaleOffset.xy + scaleOffset.zw, 0.0, 0.0).a;
    return textureAlpha * colorAlpha;
}

bool PathTracingInlineStandardCoverage(uint4 coverage, inout IllusionPathPayload payload, AttributeData attributes)
{
    g_PathTracingInlineIgnored = false;
    if (!PathTracingAcceptShadowCaster(payload))
        return false;
    bool blend = (coverage.x & PT_STANDARD_COVERAGE_BLEND) != 0u;
    if ((coverage.x & (PT_STANDARD_COVERAGE_TEST | PT_STANDARD_COVERAGE_BLEND)) != 0u)
    {
        if (blend && (PathTracingSkipsTransmission(payload) || PathTracingIsCulledFace()))
            return false;
        float alpha = PathTracingInlineStandardAlpha(coverage, attributes);
        if ((coverage.x & PT_STANDARD_COVERAGE_TEST) != 0u
            && alpha < asfloat(_PathTracingInlineMaterialData.Load(g_PathTracingInlineMaterialBase + coverage.w)))
            return false;
        if (PathTracingGetRayKind(payload) == PT_RAY_VISIBILITY)
        {
            if (blend)
                PathTracingAccumulateShadowTransmission(payload, 1.0 - alpha);
            return !g_PathTracingInlineIgnored;
        }
        if (!PathTracingAcceptCoverage(payload, blend && (coverage.x & PT_STANDARD_COVERAGE_FULL) == 0u ? alpha : 1.0))
            return false;
    }
    if (PathTracingRandomWalkRecords(payload))
    {
        uint3 indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
        float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);
        float3 normalOS = UnityRayTracingFetchVertexAttribute3(indices.x, kVertexAttributeNormal) * bary.x
            + UnityRayTracingFetchVertexAttribute3(indices.y, kVertexAttributeNormal) * bary.y
            + UnityRayTracingFetchVertexAttribute3(indices.z, kVertexAttributeNormal) * bary.z;
        float3 normalWS = mul(normalOS, (float3x3)WorldToObject3x4());
        payload.hitT = RayTCurrent();
        payload.normalVertex = PathTracingPackNormal(normalWS * rsqrt(max(dot(normalWS, normalWS), 1.175494351e-38)));
        payload.instanceID = InstanceID();
    }
    return !g_PathTracingInlineIgnored;
}

#endif
