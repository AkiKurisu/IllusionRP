#ifndef ILLUSION_HD_SHADOWS_INCLUDED
#define ILLUSION_HD_SHADOWS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/ShadowKeywords.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/ShadowSamplingPCSS.hlsl"

// Get ShadowCoord with cascade index
float4 TransformWorldToShadowCoordCascade(float3 positionWS)
{
#ifdef _MAIN_LIGHT_SHADOWS_CASCADE
    half cascadeIndex = ComputeCascadeIndex(positionWS);
#else
    half cascadeIndex = half(0.0);
#endif

    float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));

    return float4(shadowCoord.xyz, cascadeIndex);
}

real SampleShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap),
    float4 shadowCoord, ShadowSamplingData samplingData, half4 shadowParams,
    bool isPerspectiveProjection, float2 screenCoord)
{
    // Compiler will optimize this branch away as long as isPerspectiveProjection is known at compile time
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    real attenuation;
    real shadowStrength = shadowParams.x;

    // Quality levels are only for platforms requiring strict static branches
#if defined(_PCSS_SHADOWS)
    // Only support PCSS in screen space (main light only)
    attenuation = SampleShadowmapPCSS(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingData.shadowmapSize, screenCoord);
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

half IllusionMainLightRealtimeShadow(float4 shadowCoord, float2 screenCoord)
{
#if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    return half(1.0);
#elif defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS)
    return SampleScreenSpaceShadowmap(shadowCoord);
#else
    ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    return SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord,
        shadowSamplingData, shadowParams, false, screenCoord);
#endif
}
#endif