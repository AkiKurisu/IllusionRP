#ifndef HAIR_FUNCTION_INCLUDED
#define HAIR_FUNCTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ============================================================================
// Unified Hair Tangent for Anime-Style Hair Rendering
// ============================================================================
float3 FakeHairTangent_float(float3 normal, float3 targetDirection)
{
    // Project target direction onto the plane perpendicular to normal
    // Using Gram-Schmidt orthogonalization: T = D - (N·D)N
    float3 tangent = targetDirection - normal * dot(normal, targetDirection);
    
    // Handle degenerate case where normal is (nearly) parallel to target direction
    float lenSq = dot(tangent, tangent);
    if (lenSq < 0.0001)
    {
        // Use a perpendicular fallback direction
        float3 fallback = abs(normal.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
        tangent = cross(fallback, normal);
    }
    
    return normalize(tangent);
}

float3 FakeHairTangentUp_float(float3 normal)
{
    float3 upDirection = TransformObjectToWorldDir(float3(0, 1, 0));
    return FakeHairTangent_float(normal, upDirection);
}
#endif