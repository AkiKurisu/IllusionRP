#ifndef ILLUSION_PATH_TRACING_INLINE_VALIDATION_COMMON_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_VALIDATION_COMMON_INCLUDED

// Must match PathTracingInlineValidation.cs.
float4x4 _PathTracingInlineValidationPixelToWorld;
float4 _PathTracingInlineValidationCamera;
uint4 _PathTracingInlineValidationSize;

RayDesc PathTracingInlineValidationRay(uint2 pixel)
{
    float4 world = mul(_PathTracingInlineValidationPixelToWorld, float4(pixel + 0.5, 1.0, 1.0));
    RayDesc ray;
    ray.Origin = _PathTracingInlineValidationCamera.xyz;
    ray.Direction = normalize(world.xyz / world.w - _PathTracingInlineValidationCamera.xyz);
    ray.TMin = 0.0;
    ray.TMax = 1e20;
    return ray;
}

IllusionPathPayload PathTracingInlineValidationPayload(uint2 pixel)
{
    return PathTracingCreatePayload(PT_RAY_SCATTER, pixel.x * 0x8DA6B343u ^ pixel.y * 0xD8163841u, 0.0, _PathTracingInlineValidationCamera.w);
}

void PathTracingInlineValidationStore(RWStructuredBuffer<uint4> samples, uint2 pixel, IllusionPathPayload payload)
{
    uint index = (pixel.y * _PathTracingInlineValidationSize.x + pixel.x) * 4;
    samples[index + 0] = uint4(asuint(payload.hitT), payload.familyAndFlags, payload.normalShading, payload.instanceID);
    samples[index + 1] = uint4(payload.diffuseOpacity, payload.specularRoughness);
    samples[index + 2] = uint4(payload.emissionMetallic, payload.triangleIndex, payload.familyParameters.w);
    samples[index + 3] = uint4(payload.normalFace, payload.parameters.x, payload.parameters.y, 0);
}

#endif
