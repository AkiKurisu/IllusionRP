#ifndef ILLUSION_PATH_TRACING_INLINE_VALIDATION_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_VALIDATION_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/Inline/PathTracingInlineTraversal.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/Inline/PathTracingInlineValidationCommon.hlsl"

RaytracingAccelerationStructure SceneBVH;
RWStructuredBuffer<uint4> _PathTracingInlineValidationSamples;
StructuredBuffer<uint4> _PathTracingInlineValidationReference;
StructuredBuffer<uint4> _PathTracingInlineValidationInline;
RWTexture2D<float4> _PathTracingInlineValidationOutput;
RWStructuredBuffer<uint> _PathTracingInlineValidationStats;

[numthreads(8, 8, 1)]
void PathTracingInlineValidate(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= _PathTracingInlineValidationSize.xy))
        return;
    IllusionPathPayload payload = PathTracingInlineValidationPayload(id.xy);
    PathTracingInlineTraceRay(SceneBVH, RAY_FLAG_NONE, PT_INSTANCE_SCENE, PathTracingInlineValidationRay(id.xy), payload);
    PathTracingInlineValidationStore(_PathTracingInlineValidationSamples, id.xy, payload);
}

// Stats layout must match PathTracingInlineValidation.cs.
#define PT_VALIDATION_PIXELS 0
#define PT_VALIDATION_MISSES 1
#define PT_VALIDATION_HIT_MISMATCH 2
#define PT_VALIDATION_INSTANCE_MISMATCH 3
#define PT_VALIDATION_UNCOVERED 4
#define PT_VALIDATION_VALUE_MISMATCH 5
#define PT_VALIDATION_MATCH 6
#define PT_VALIDATION_ALBEDO 7
#define PT_VALIDATION_NORMAL 8
#define PT_VALIDATION_ROUGHNESS 9
#define PT_VALIDATION_EMISSION 10
#define PT_VALIDATION_DISTANCE 11
#define PT_VALIDATION_FAMILY 12
#define PT_VALIDATION_MATERIAL 13

[numthreads(8, 8, 1)]
void PathTracingInlineValidationCompare(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= _PathTracingInlineValidationSize.xy))
        return;
    uint index = (id.y * _PathTracingInlineValidationSize.x + id.x) * 4;
    uint4 r0 = _PathTracingInlineValidationReference[index + 0];
    uint4 r1 = _PathTracingInlineValidationReference[index + 1];
    uint4 r2 = _PathTracingInlineValidationReference[index + 2];
    uint4 i0 = _PathTracingInlineValidationInline[index + 0];
    uint4 i1 = _PathTracingInlineValidationInline[index + 1];
    uint4 i2 = _PathTracingInlineValidationInline[index + 2];

    float refT = asfloat(r0.x);
    float inlineT = asfloat(i0.x);
    float4 refAlbedo = PathTracingUnpackHalf4(r1.xy);
    float4 inlineAlbedo = PathTracingUnpackHalf4(i1.xy);
    float4 refSpecular = PathTracingUnpackHalf4(r1.zw);
    float4 inlineSpecular = PathTracingUnpackHalf4(i1.zw);
    float4 refEmission = PathTracingUnpackHalf4(r2.xy);
    float4 inlineEmission = PathTracingUnpackHalf4(i2.xy);

    uint category;
    uint mismatches = 0;
    bool refHit = refT >= 0.0;
    bool inlineHit = inlineT >= 0.0;
    if (!refHit && !inlineHit && refT == inlineT)
        category = PT_VALIDATION_MISSES;
    else if (inlineT == PT_NO_SURFACE_T && refHit)
        category = PT_VALIDATION_UNCOVERED;
    else if (refHit != inlineHit)
        category = PT_VALIDATION_HIT_MISMATCH;
    else if (r0.w != i0.w || r2.z != i2.z)
        category = PT_VALIDATION_INSTANCE_MISMATCH;
    else
    {
        if (any(abs(refAlbedo - inlineAlbedo) > 1.0 / 64.0)) mismatches |= 1u << 0;
        if (dot(PathTracingUnpackNormal(r0.z), PathTracingUnpackNormal(i0.z)) < 0.999) mismatches |= 1u << 1;
        if (any(abs(refSpecular - inlineSpecular) > 1.0 / 64.0)) mismatches |= 1u << 2;
        if (any(abs(refEmission.rgb - inlineEmission.rgb) > 0.02 * max(max(refEmission.r, refEmission.g), max(refEmission.b, 1.0)))) mismatches |= 1u << 3;
        if (abs(refT - inlineT) > 1e-3 * max(refT, 1.0)) mismatches |= 1u << 4;
        if ((r0.y & PT_FAMILY_MASK) != (i0.y & PT_FAMILY_MASK)) mismatches |= 1u << 5;
        if (r2.w != i2.w) mismatches |= 1u << 6;
        category = mismatches != 0 ? PT_VALIDATION_VALUE_MISMATCH : PT_VALIDATION_MATCH;
    }

    InterlockedAdd(_PathTracingInlineValidationStats[PT_VALIDATION_PIXELS], 1u);
    InterlockedAdd(_PathTracingInlineValidationStats[category], 1u);
    [unroll]
    for (uint bit = 0; bit < 7; bit++)
        if ((mismatches >> bit) & 1u)
            InterlockedAdd(_PathTracingInlineValidationStats[PT_VALIDATION_ALBEDO + bit], 1u);

    float3 color;
    uint view = _PathTracingInlineValidationSize.z;
    if (view == 1u)
        color = inlineHit ? inlineAlbedo.rgb : 0.0;
    else if (view == 2u)
        color = refHit ? refAlbedo.rgb : 0.0;
    else
    {
        float shade = 0.35 + 0.65 * saturate(dot(refAlbedo.rgb, float3(0.3, 0.59, 0.11)));
        switch (category)
        {
            case PT_VALIDATION_MISSES: color = 0.05; break;
            case PT_VALIDATION_UNCOVERED: color = float3(0.1, 0.3, 1.0) * shade; break;
            case PT_VALIDATION_HIT_MISMATCH: color = float3(1.0, 0.0, 1.0); break;
            case PT_VALIDATION_INSTANCE_MISMATCH: color = float3(1.0, 0.0, 0.0); break;
            case PT_VALIDATION_VALUE_MISMATCH: color = float3(1.0, 0.55, 0.0); break;
            default: color = float3(0.1, 0.8, 0.2) * shade; break;
        }
    }
    _PathTracingInlineValidationOutput[id.xy] = float4(saturate(color), 1.0);
}

#endif
