#ifndef PRT_EVALUATE_PROBE_VOLUME_INCLUDED
#define PRT_EVALUATE_PROBE_VOLUME_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalHarmonics.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/ProbeVolume.hlsl"

#ifndef _PRT_GLOBAL_ILLUMINATION
#define _PRT_GLOBAL_ILLUMINATION 0
#endif
#define _PRT_GLOBAL_ILLUMINATION_ON (_PRT_GLOBAL_ILLUMINATION && !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON))

#define _coefficientVoxelGridSize (_prtVolumeEnabled != 0u ? _prtGridSpacing : 0.0)
Texture3D<float4> _coefficientVoxel3D;
SamplerState sampler_PRTLinear_RepeatU_RepeatV_ClampW;

// One-cell fade inside a box tightened by one cell around the camera, along the axes where the window scrolls. The
// weight depends only on the camera position, so window steps do not pop; the window always contains the box while it
// follows the camera. The coarsest cascade does not fade because it has nothing to fade into.
float PRTCascadeBlend(uint cascade, float3 coordinate, int3 minimum, int3 count, uint scrolls)
{
    // A camera outside the baked domain keeps the band on the window's nearest side.
    float3 camera = clamp(PRTCascadeCoordinate(_prtCascadeCenter.xyz, cascade), float3(minimum), float3(minimum + count - 1));
    float weight = 1;
    [unroll]
    for (int axis = 0; axis < 3; axis++)
    {
        if ((scrolls & (1u << axis)) == 0u)
            continue;
        float radius = max(float(count[axis] - 1) * 0.5 - 1.0, 0.0);
        weight = min(weight, saturate(min(coordinate[axis] - (camera[axis] - radius), camera[axis] + radius - coordinate[axis])));
    }
    return weight;
}

bool SamplePRTCascade(uint cascade, float3 worldPosition, float basis[9], float secondaryBasis[9],
    out float3 diffuse, out float3 secondaryDiffuse, out float blend)
{
    diffuse = 0;
    secondaryDiffuse = 0;
    blend = 0;
    int3 minimum = _prtCascadeWindowMin[cascade].xyz;
    int3 count = _prtCascadeWindowCount[cascade].xyz;
    float3 node = PRTCascadeCoordinate(worldPosition, cascade);
    int3 cell;
    float3 rate;
    if (!PRTInterpolationCell(node, minimum, count, cell, rate))
        return false;
    blend = cascade + 1u < _prtCascadeCount
        ? PRTCascadeBlend(cascade, node, minimum, count, uint(_prtCascadeWindowCount[cascade].w)) : 1.0;
    if (blend <= 0)
        return false;
    float depth = float(_prtSlotCount.y * 9 * int(_prtCascadeCount));
    float3 local = float3(PRTPublicationSlot(cell, cascade)) + rate;
    float3 coordinate = float3((local.x + 0.5) / _prtSlotCount.x, (local.z + 0.5) / _prtSlotCount.z,
        (local.y + 0.5 + float(cascade * 9u * uint(_prtSlotCount.y))) / depth);
    float totalWeight = 0;
    [unroll]
    for (uint coefficient = 0; coefficient < 9u; coefficient++)
    {
        float3 sampleCoordinate = coordinate + float3(0, 0, float(coefficient * uint(_prtSlotCount.y)) / depth);
        float4 sample = _coefficientVoxel3D.SampleLevel(sampler_PRTLinear_RepeatU_RepeatV_ClampW, sampleCoordinate, 0);
        if (coefficient == 0) totalWeight = sample.a;
        diffuse += sample.rgb * (basis[coefficient] * kClampedCosineCoefs[coefficient]);
        secondaryDiffuse += sample.rgb * (secondaryBasis[coefficient] * kClampedCosineCoefs[coefficient]);
    }
    if (totalWeight <= 1e-6)
        return false;
    diffuse /= totalWeight;
    secondaryDiffuse /= totalWeight;
    return true;
}

// contributions holds each cascade's share of the result; it sums to 1 when the function succeeds.
bool TrySampleProbeVolumeCascades(float3 worldPosition, float3 normal, float3 secondaryNormal,
    out float3 diffuse, out float3 secondaryDiffuse, out float4 contributions)
{
    diffuse = 0;
    secondaryDiffuse = 0;
    contributions = 0;
    if (_prtVolumeEnabled == 0u || _prtPublicationGeneration == 0u || _prtCascadeCount == 0u)
        return false;
    int3 domainCell;
    float3 domainRate;
    if (!PRTInterpolationCell(PRTCascadeCoordinate(worldPosition, 0), 0, _prtGridCount.xyz, domainCell, domainRate))
        return false;
    float basis[9];
    float secondaryBasis[9];
    EvaluateSH9(normal.xzy, basis);
    EvaluateSH9(secondaryNormal.xzy, secondaryBasis);
    float accumulated = 0;
    for (uint cascade = 0; cascade < _prtCascadeCount && accumulated < 1.0; cascade++)
    {
        float3 cascadeDiffuse, cascadeSecondary;
        float blend;
        if (!SamplePRTCascade(cascade, worldPosition, basis, secondaryBasis, cascadeDiffuse, cascadeSecondary, blend))
            continue;
        float contribution = (1.0 - accumulated) * blend;
        diffuse += contribution * cascadeDiffuse;
        secondaryDiffuse += contribution * cascadeSecondary;
        contributions += contribution * float4(cascade == 0u, cascade == 1u, cascade == 2u, cascade == 3u);
        accumulated += contribution;
    }
    if (accumulated <= 1e-4)
        return false;
    diffuse = max(diffuse / accumulated, 0);
    secondaryDiffuse = max(secondaryDiffuse / accumulated, 0);
    contributions /= accumulated;
    return true;
}

bool TrySampleProbeVolumePair(float3 worldPosition, float3 normal, float3 secondaryNormal,
    out float3 diffuse, out float3 secondaryDiffuse)
{
    float4 contributions;
    return TrySampleProbeVolumeCascades(worldPosition, normal, secondaryNormal, diffuse, secondaryDiffuse, contributions);
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
