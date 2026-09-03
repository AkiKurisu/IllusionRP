#ifndef REFLECTION_PROBE_NORMALIZATION_INCLUDED
#define REFLECTION_PROBE_NORMALIZATION_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// Normalization factor parameters
float4 _reflectionProbeNormalizationFactor;
#define _ReflProbeNormalizationWeight _reflectionProbeNormalizationFactor.w
#define _MinReflProbeNormalizationFactor _reflectionProbeNormalizationFactor.x
#define _MaxReflProbeNormalizationFactor _reflectionProbeNormalizationFactor.y

struct ReflectionProbeData
{
    float4 L0L1;
    float4 L2_1;
    float L2_2;
    int normalizeWithProbeVolume;
    float2 padding;
};

StructuredBuffer<ReflectionProbeData> _reflectionProbeNormalizationData;

// -------------------------------------------------------------
// Reflection Probe Normalization functions
// -------------------------------------------------------------
// Same idea as in Rendering of COD:IW [Drobot 2017]

float IllusionEvaluateReflectionProbeSH(float3 sampleDir, float4 reflProbeSHL0L1, float4 reflProbeSHL2_1, float reflProbeSHL2_2)
{
    float outFactor = 0;
    float L0 = reflProbeSHL0L1.x;
    float L1 = dot(reflProbeSHL0L1.yzw, sampleDir);

    outFactor = L0 + L1;


    // IMPORTANT: The encoding is unravelled C# side before being sent
    float4 vB = sampleDir.xyzz * sampleDir.yzzx;
    // First 4 coeff.
    float L2 = dot(reflProbeSHL2_1, vB);
    float vC = sampleDir.x * sampleDir.x - sampleDir.y * sampleDir.y;
    L2 += reflProbeSHL2_2 * vC;

    outFactor += L2;

    return outFactor;
}

float IllusionGetReflectionProbeNormalizationFactor(float3 lightingInReflDir, float3 sampleDir, float4 reflProbeSHL0L1, float4 reflProbeSHL2_1, float reflProbeSHL2_2)
{
    float refProbeNormalization = IllusionEvaluateReflectionProbeSH(sampleDir, reflProbeSHL0L1, reflProbeSHL2_1, reflProbeSHL2_2);

    float localNormalization = Luminance(lightingInReflDir);
    return lerp(1.f, clamp(SafeDiv(localNormalization, refProbeNormalization), _MinReflProbeNormalizationFactor, _MaxReflProbeNormalizationFactor), _ReflProbeNormalizationWeight);
}

#endif