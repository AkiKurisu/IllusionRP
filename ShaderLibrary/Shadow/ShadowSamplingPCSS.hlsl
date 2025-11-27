#ifndef SHADOWS_SAMPLING_PCSS_INCLUDED
#define SHADOWS_SAMPLING_PCSS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/PCSS.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/ShaderVariablesPCSS.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

float PCF(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, float samplingFilterSize, int sampleCount, 
                                float2 shadowmapInAtlasScale, float2 sampleJitter, float2 minCoord, float2 maxCoord,
                                float radial2DepthScale, float filterSize, float maxPcssOffset)
{
    float shadowAttenuationSum = 0.0;
    float sampleSum = 0.0;
    
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;
    
    for(int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; ++i)
    {
        float zOffset;
        float2 offset = ComputePcfSampleOffset(filterSize, samplingFilterSize, i, sampleCountInverse,
            sampleCountBias, sampleJitter, shadowmapInAtlasScale, radial2DepthScale,
            maxPcssOffset, zOffset);
        
        // Cone-based Z offset for receiver to reduce self-shadowing
        float3 sampleCoord = shadowCoord.xyz + float3(offset, zOffset);
        
        // Only sample if sampleCoord is within the tile bounds
        if(!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)))
        {
            shadowAttenuationSum += SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, sampleCoord.xyz);
            sampleSum += 1.0;
        }
    }

    return shadowAttenuationSum / sampleSum;
}

float SampleShadowmapPCSS(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord,
    float4 shadowMapSize, float2 screenCoord)
{
    float penumbraMask = SAMPLE_TEXTURE2D(_PenumbraMaskTex, sampler_LinearClamp, screenCoord).r;

    UNITY_BRANCH
    if (penumbraMask > Eps_float())
    {
        int cascadeIndex = (int)shadowCoord.w;
        float receiverDepth = shadowCoord.z;
        

        float2 shadowmapInAtlasOffset = _CascadeOffsetScales[cascadeIndex].xy;
        float2 shadowmapInAtlasScale = _CascadeOffsetScales[cascadeIndex].zw;
        float2 minCoord = shadowmapInAtlasOffset;
        float2 maxCoord = shadowmapInAtlasOffset + shadowmapInAtlasScale;
        float texelSize = shadowMapSize.x / shadowmapInAtlasScale.x;
        
        float depth2RadialScale = _DirLightPcssParams0[cascadeIndex].x;
        float maxBlokcerDistance = _DirLightPcssParams0[cascadeIndex].z;
        float maxSamplingDistance = _DirLightPcssParams0[cascadeIndex].w;
        float minFilterRadius = texelSize * _DirLightPcssParams1[cascadeIndex].x;
        float minFilterRadial2DepthScale = _DirLightPcssParams1[cascadeIndex].y;
        float blockerRadial2DepthScale = _DirLightPcssParams1[cascadeIndex].z;
        float maxPcssOffset = maxSamplingDistance * abs(_DirLightPcssProjs[cascadeIndex].z);
        float maxSampleZDistance = maxBlokcerDistance * abs(_DirLightPcssProjs[cascadeIndex].z);
        float2 posSS = screenCoord * _ScreenSize.xy;
        float2 sampleJitter = ComputePcfSampleJitter(posSS, (uint)_TaaFrameInfo.z);
        
        float blockerSearchRadius = BlockerSearchRadius(receiverDepth, depth2RadialScale, maxSamplingDistance, minFilterRadius);
        float avgBlockerDepth = FindBlocker(TEXTURE2D_ARGS(ShadowMap, sampler_LinearClamp), shadowCoord.xy,
            receiverDepth, blockerSearchRadius,
            minCoord, maxCoord, _FindBlockerSampleCount,
            shadowmapInAtlasScale, sampleJitter, minFilterRadius, minFilterRadial2DepthScale,
            blockerRadial2DepthScale);

        float filterSize, blockerDistance;
        float samplingFilterSize = EstimatePenumbra(receiverDepth, avgBlockerDepth, depth2RadialScale, maxSampleZDistance,
            minFilterRadius, filterSize, blockerDistance);
        maxPcssOffset = min(maxPcssOffset, blockerDistance * 0.25f);

        return PCF(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingFilterSize, _PcfSampleCount,
            shadowmapInAtlasScale, sampleJitter, minCoord, maxCoord,
            minFilterRadial2DepthScale, filterSize, maxPcssOffset);
    }
    
    return SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz);
}
#endif