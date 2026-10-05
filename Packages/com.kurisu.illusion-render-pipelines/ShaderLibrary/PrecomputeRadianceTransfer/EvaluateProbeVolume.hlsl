#ifndef PRT_EVALUATE_PROBE_VOLUME_INCLUDED
#define PRT_EVALUATE_PROBE_VOLUME_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalHarmonics.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/ProbeVolume.hlsl"

#ifndef _PRT_GLOBAL_ILLUMINATION
#define _PRT_GLOBAL_ILLUMINATION 0
#endif
#define _PRT_GLOBAL_ILLUMINATION_ON (_PRT_GLOBAL_ILLUMINATION && !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON))

#define _coefficientVoxelGridSize (_prtVolumeEnabled != 0u ? _prtGridSpacing : 0.0)
Texture3D<float4> _coefficientVoxel3D;
SamplerState sampler_coefficientVoxel3D;
Texture3D<uint> _validityVoxel3D;

bool TrySampleProbeVolumePair(float3 worldPosition, float3 normal, float3 secondaryNormal,
    out float3 diffuse, out float3 secondaryDiffuse)
{
    diffuse = 0;
    secondaryDiffuse = 0;
    if (_prtVolumeEnabled == 0u || _prtPublicationGeneration == 0u)
        return false;
    int3 fullCell, cell;
    float3 fullRate, rate;
    if (!PRTInterpolationCell(worldPosition, _prtGridMin.xyz, _prtGridCount.xyz, fullCell, fullRate)
        || !PRTInterpolationCell(worldPosition, _prtWindowMin.xyz, _prtWindowCount.xyz, cell, rate))
        return false;
    float basis[9];
    float secondaryBasis[9];
    EvaluateSH9(normal.xzy, basis);
    EvaluateSH9(secondaryNormal.xzy, secondaryBasis);
    int3 baseSlot = cell - _prtWindowMin.xyz;
    float3 local = float3(baseSlot) + rate;
    float3 textureSize = float3(_prtWindowCount.x, _prtWindowCount.z, _prtWindowCount.y * 9);
    float3 coordinate = float3(local.x + 0.5, local.z + 0.5, local.y + 0.5) / textureSize;
    float totalWeight = 0;
    [unroll]
    for (uint coefficient = 0; coefficient < 9u; coefficient++)
    {
        float3 sampleCoordinate = coordinate + float3(0, 0, float(coefficient) / 9.0);
        float4 sample = _coefficientVoxel3D.SampleLevel(sampler_coefficientVoxel3D, sampleCoordinate, 0);
        if (coefficient == 0) totalWeight = sample.a;
        diffuse += sample.rgb * (basis[coefficient] * kClampedCosineCoefs[coefficient]);
        secondaryDiffuse += sample.rgb * (secondaryBasis[coefficient] * kClampedCosineCoefs[coefficient]);
    }
    if (totalWeight <= 1e-6)
        return false;
    diffuse = max(diffuse / totalWeight, 0);
    secondaryDiffuse = max(secondaryDiffuse / totalWeight, 0);
    return true;
}

bool TrySampleProbeVolume(float3 worldPosition, float3 normal, out float3 diffuse)
{
    float3 unused;
    return TrySampleProbeVolumePair(worldPosition, normal, normal, diffuse, unused);
}

real3 SampleProbeVolume(float3 worldPosition, real3 normal, real3 bakedGI)
{
    float3 diffuse = 0;
    return TrySampleProbeVolume(worldPosition, normal, diffuse) ? diffuse : bakedGI;
}
#endif
