#ifndef PRT_SOLVER_PROBE_SAMPLING_INCLUDED
#define PRT_SOLVER_PROBE_SAMPLING_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalHarmonics.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/ProbeVolume.hlsl"

StructuredBuffer<float4> _prtPreviousSH;
StructuredBuffer<uint> _validityMasks;
StructuredBuffer<uint> _prtReady;

float3 SamplePRTSolverProbe(int index, float3 normal)
{
    if (index < 0) return 0;
    if (_prtReady[index] == 0u) return 0;
    float intensity, validity;
    UnpackIntensityValidity(_validityMasks[index], intensity, validity);
    if (validity <= 0) return 0;
    float basis[9];
    EvaluateSH9(normal.xzy, basis);
    float3 irradiance = 0;
    [unroll]
    for (uint coefficient = 0; coefficient < 9u; coefficient++)
        irradiance += _prtPreviousSH[index * 9u + coefficient].rgb *
            (basis[coefficient] * kClampedCosineCoefs[coefficient]);
    return max(irradiance, 0) * intensity;
}
#endif
