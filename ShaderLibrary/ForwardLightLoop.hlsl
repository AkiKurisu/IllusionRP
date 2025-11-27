#ifndef ILLUSION_FORWARD_LIGHT_LOOP_INCLUDED
#define ILLUSION_FORWARD_LIGHT_LOOP_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateScreenSpaceGlobalIllumination.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/EvaluateProbeVolume.hlsl"

#if _PRT_GLOBAL_ILLUMINATION_ON
    #define SAMPLE_PROBE_VOLUME(worldPos, normal, bakedGI) SampleProbeVolume(worldPos, normal, bakedGI)
#else
    #define SAMPLE_PROBE_VOLUME(worldPos, normal, bakedGI) bakedGI
#endif

#if USE_FORWARD_PLUS
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ReflectionProbeNormalization.hlsl"

half CalculateNormalizationFactorFromReflectionProbes(float3 lightingInReflDir, half3 sampleDir, float3 positionWS, float2 normalizedScreenSpaceUV)
{
    float totalWeight = 0.0f;
    float totalFactor = 0.0f;
    uint probeIndex;
    ClusterIterator it = ClusterInit(normalizedScreenSpaceUV, positionWS, 1);
    [loop] while (ClusterNext(it, probeIndex) && totalWeight < 0.99f)
    {
        probeIndex -= URP_FP_PROBES_BEGIN;

        float weight = CalculateProbeWeight(positionWS, urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
        weight = min(weight, 1.0f - totalWeight);
        // Calculate Normalization Factor
        ReflectionProbeData probeData = _reflectionProbeNormalizationData[probeIndex];
        float factor = 1.0f;
        if (probeData.normalizeWithProbeVolume > 0)
        {
            factor = GetReflectionProbeNormalizationFactor(lightingInReflDir, sampleDir, probeData.L0L1, probeData.L2_1, probeData.L2_2);
        }
        totalFactor += factor * weight;
        totalWeight += weight;
    }
    return totalFactor;
}
#endif

half SampleProbeVolumeReflectionNormalize(float3 worldPos, float3 normal, float2 normalizedScreenSpaceUV,
    float3 bakedGI, float3 reflectionDir)
{
#if USE_FORWARD_PLUS && _PRT_GLOBAL_ILLUMINATION_ON
    UNITY_BRANCH
    if (_coefficientVoxelGridSize == 0)
    {
        return 1.0f;
    }
    
    float3 lightingInReflDir = EvaluateProbeVolumeSH(
                       worldPos, 
                       reflectionDir,
                       bakedGI,
                       _coefficientVoxel3D,
                       _coefficientVoxelGridSize,
                       _coefficientVoxelCorner,
                       _coefficientVoxelSize,
                       _boundingBoxMin,
                       _boundingBoxSize,
                       _originalBoundingBoxMin
                   );
    return CalculateNormalizationFactorFromReflectionProbes(lightingInReflDir, normal, worldPos, normalizedScreenSpaceUV);
#else
    return 1.0f;
#endif
}

half3 EvaluateIndirectDiffuse(float3 positionWS, float3 normalWS, float2 normalizedScreenSpaceUV, float3 bakedGI)
{
    bool replaceBakeDiffuseLighting = false; 
    half3 indirectDiffuse = 0;
    
#if (SURFACE_TYPE_RECEIVE_SSGI && _SCREEN_SPACE_GLOBAL_ILLUMINATION)
    if (_IndirectDiffuseMode != INDIRECTDIFFUSEMODE_OFF)
    {
        indirectDiffuse = SampleScreenSpaceGlobalIllumination(normalizedScreenSpaceUV).rgb;
        replaceBakeDiffuseLighting = true;
    }
#endif

    if (!replaceBakeDiffuseLighting)
    {
        indirectDiffuse = SAMPLE_PROBE_VOLUME(positionWS, normalWS, bakedGI);
    }
    return indirectDiffuse;
}
#endif