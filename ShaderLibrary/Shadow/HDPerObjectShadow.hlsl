#ifndef HD_PER_OBJECT_SHADOW_INCLUDED
#define HD_PER_OBJECT_SHADOW_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/PerObjectShadow.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/PCSS.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/ShaderVariablesPCSS.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// Per-object shadow PCSS parameters
float4 _PerObjShadowPcssParams0[MAX_PER_OBJECT_SHADOW_COUNT];
float4 _PerObjShadowPcssParams1[MAX_PER_OBJECT_SHADOW_COUNT];
float4 _PerObjShadowPcssProjs[MAX_PER_OBJECT_SHADOW_COUNT];

// PCF function for per-object shadows
float PerObjectPCF(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, float samplingFilterSize, int sampleCount, 
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
        if(!(any(sampleCoord.xy < minCoord) || any(sampleCoord.xy > maxCoord)))
        {
            shadowAttenuationSum += SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, sampleCoord.xyz);
            sampleSum += 1.0;
        }
    }

    return shadowAttenuationSum / max(sampleSum, 1.0);
}

float SamplePerObjectShadowmapPCSS(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord,
    ShadowSamplingData samplingData, float2 screenCoord, int shadowIndex, float4 shadowMapRect)
{
    float penumbraMask = SAMPLE_TEXTURE2D(_PenumbraMaskTex, sampler_LinearClamp, screenCoord).r;
    float4 shadowMapSize = samplingData.shadowmapSize;
    UNITY_BRANCH
    if (penumbraMask > Eps_float())
    {
        float receiverDepth = shadowCoord.z;

        float2 minCoord = float2(shadowMapRect.x, shadowMapRect.z);
        float2 maxCoord = float2(shadowMapRect.y, shadowMapRect.w);
        float2 shadowmapInAtlasScale = maxCoord - minCoord;
        float texelSize = shadowMapSize.x / shadowmapInAtlasScale.x;
    
        // Use per-object shadow PCSS parameters
        float4 pcssParams0 = _PerObjShadowPcssParams0[shadowIndex];
        float4 pcssParams1 = _PerObjShadowPcssParams1[shadowIndex];
        float4 pcssProjs = _PerObjShadowPcssProjs[shadowIndex];
    
        float depth2RadialScale = pcssParams0.x;
        float maxBlokcerDistance = pcssParams0.z;
        float maxSamplingDistance = pcssParams0.w;
        float minFilterRadius = texelSize * pcssParams1.x;
        float minFilterRadial2DepthScale = pcssParams1.y;
        float blockerRadial2DepthScale = pcssParams1.z;
        float maxPcssOffset = maxSamplingDistance * abs(pcssProjs.z);
        float maxSampleZDistance = maxBlokcerDistance * abs(pcssProjs.z);
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

        return PerObjectPCF(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingFilterSize, _PcfSampleCount,
            shadowmapInAtlasScale, sampleJitter, minCoord, maxCoord,
            minFilterRadial2DepthScale, filterSize, maxPcssOffset);
    }

    return SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz);
}

real SamplePerObjectShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap),
    float4 shadowCoord, ShadowSamplingData samplingData, half4 shadowParams,
    bool isPerspectiveProjection, float2 screenCoord, int shadowIndex, float4 shadowMapRect)
{
    // Compiler will optimize this branch away as long as isPerspectiveProjection is known at compile time
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    real attenuation;
    real shadowStrength = shadowParams.x;

    // Quality levels are only for platforms requiring strict static branches
#if defined(_PCSS_SHADOWS)
    // Support PCSS for per-object shadows
    attenuation = SamplePerObjectShadowmapPCSS(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord,
        samplingData, screenCoord, shadowIndex, shadowMapRect);
#elif defined(_SHADOWS_SOFT_LOW)
    attenuation = SampleShadowmapFilteredLowQuality(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingData);
#elif defined(_SHADOWS_SOFT_MEDIUM)
    attenuation = SampleShadowmapFilteredMediumQuality(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingData);
#elif defined(_SHADOWS_SOFT_HIGH)
    attenuation = SampleShadowmapFilteredHighQuality(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingData);
#elif defined(_SHADOWS_SOFT)
    if (shadowParams.y > SOFT_SHADOW_QUALITY_OFF)
    {
        attenuation = SampleShadowmapFiltered(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingData);
    }
    else
    {
        attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
    }
#else
    attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
#endif

    attenuation = LerpWhiteTo(attenuation, shadowStrength);

    // Shadow coords that fall out of the light frustum volume must always return attenuation 1.0
    // TODO: We could use branch here to save some perf on some platforms.
    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

float PerObjectShadowHD(
    TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap),
    float4 shadowMapRects,
    float4 shadowCoord,
    ShadowSamplingData shadowSamplingData,
    half4 shadowParams,
    bool isPerspectiveProjection, float2 screenCoord, int shadowIndex)
{
    if (shadowCoord.x < shadowMapRects.x ||
        shadowCoord.x > shadowMapRects.y ||
        shadowCoord.y < shadowMapRects.z ||
        shadowCoord.y > shadowMapRects.w)
    {
        return 1; // Beyond the shadow map range, it is considered as no shadow
    }

    return SamplePerObjectShadowmap(TEXTURE2D_SHADOW_ARGS(shadowMap, sampler_shadowMap),
        shadowCoord, shadowSamplingData, shadowParams, isPerspectiveProjection, screenCoord, shadowIndex, shadowMapRects);
}

float MainLightPerObjectSceneShadow(float3 positionWS, float3 normalWS, half3 lightDir, float2 screenCoord)
{
    ShadowSamplingData shadowSamplingData = GetMainLightPerObjectSceneShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    float shadow = 1;

    for (int i = 0; i < _PerObjSceneShadowCount; i++)
    {
        float3 biasPositionWS = positionWS;
#if APPLY_SHADOW_BIAS_FRAGMENT
        biasPositionWS = ApplyPerObjectShadowBias(positionWS, normalWS, lightDir, i);
#endif
        
        float4 shadowCoord = TransformWorldToPerObjectShadowCoord(_PerObjSceneShadowMatrices[i], biasPositionWS);
        shadow = min(shadow, PerObjectShadowHD(TEXTURE2D_SHADOW_ARGS(_PerObjSceneShadowMap, sampler_PerObjSceneShadowMap),
            _PerObjSceneShadowMapRects[i], shadowCoord, shadowSamplingData, shadowParams, false, screenCoord, i));
    }

    return shadow;
}
#endif
