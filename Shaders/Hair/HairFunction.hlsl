#ifndef HAIR_FUNCTION_INCLUDED
#define HAIR_FUNCTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float3 FakeBitangent_float(float3 normal, float sign)
{
    // float3 worldUp = float3(0,1,0);
    float3 worldUp = TransformObjectToWorldDir(float3(0,1,0));
    float3 tangent = cross(worldUp, normal);
    return cross(normal, tangent) * sign;
}
#endif