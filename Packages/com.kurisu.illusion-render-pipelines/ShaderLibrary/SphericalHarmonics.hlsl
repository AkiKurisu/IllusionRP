#ifndef ILLUSION_SPHERICAL_HARMONICS_INCLUDED
#define ILLUSION_SPHERICAL_HARMONICS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"

// Vectorized SH basis evaluation
// Input dir should use right-handed (Z up) coordinate
void EvaluateSH9(in float3 dir, out float sh[9])
{
    float x = dir.x;
    float y = dir.y;
    float z = dir.z;
    
    // L0 (constant)
    sh[0] = 1.0; // Constant term (will be multiplied by kSHBasisCoef[0])
    
    // L1 (linear)
    sh[1] = y;    // Y_1_-1
    sh[2] = z;    // Y_1_0
    sh[3] = x;    // Y_1_1
    
    // L2 (quadratic)
    sh[4] = x * y;                          // Y_2_-2
    sh[5] = y * z;                          // Y_2_-1
    sh[6] = 3.0 * z * z - 1.0;              // Y_2_0  (Equals 2.0 * z * z - x * x - y * y)
    sh[7] = x * z;                          // Y_2_1
    sh[8] = x * x - y * y;                  // Y_2_2
    
    // Apply kSHBasisCoef to get the final SH basis values
    [unroll]
    for (int i = 0; i < 9; ++i)
    {
        sh[i] = sh[i] * kSHBasisCoef[i];
    }
}

// Vectorized irradiance evaluation
float3 IrradianceSH9(in float3 c[9], in float3 dir)
{
    float sh[9];
    EvaluateSH9(dir, sh);
    
    float3 irradiance = float3(0, 0, 0);
    
    // Vectorized accumulation
    [unroll]
    for (int i = 0; i < 9; ++i)
    {
        irradiance += sh[i] * c[i] * kClampedCosineCoefs[i];
    }
    
    return max(float3(0, 0, 0), irradiance);
}

// Helper function to convert coefficients to Peter-Pike Sloan's format
void PackSHCoefficients(in float3 c[9],
    out float4 shAr, out float4 shAg, out float4 shAb,
    out float4 shBr, out float4 shBg, out float4 shBb,
    out float4 shC)
{
    // Separate constant and variable terms
    shAr = float4(c[3].r, c[1].r, c[2].r, c[0].r - c[6].r);
    shAg = float4(c[3].g, c[1].g, c[2].g, c[0].g - c[6].g);
    shAb = float4(c[3].b, c[1].b, c[2].b, c[0].b - c[6].b);
    
    shBr = float4(c[4].r, c[5].r, c[6].r * 3.0f, c[7].r);
    shBg = float4(c[4].g, c[5].g, c[6].g * 3.0f, c[7].g);
    shBb = float4(c[4].b, c[5].b, c[6].b * 3.0f, c[7].b);
    
    shC = float4(c[8].r, c[8].g, c[8].b, 1.0f);
}

#endif // defined(PRT_SH_INCLUDED)