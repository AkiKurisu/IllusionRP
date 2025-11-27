#ifndef ILLUSION_SHADOWS_INCLUDED
#define ILLUSION_SHADOWS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/ShadowKeywords.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/PerObjectShadow.hlsl"

// Override URP GetShadowCoord.
#define GetShadowCoord IllusionGetShadowCoord

// Override URP ApplyShadowBias.
#if APPLY_SHADOW_BIAS_FRAGMENT
    #define ApplyShadowBias(positionWS, normalWS, lightDirection) positionWS
#endif

// Helper Macro for ASE Transmission and Transparency, use it within GetMainLight(shadowCoord) => GetMainLight(MAIN_LIGHT_SHADOW_COORD(shadowCoord)).
#define MAIN_LIGHT_SHADOW_COORD(shadowCoord) IllusionGetShadowCoord(shadowCoord, inputData.positionWS, inputData.normalWS, GetMainLight().direction)

// Properties
float4 _MainLightShadowCascadeBiases[MAX_SHADOW_CASCADES + 1];

// Ref: ApplyShadowBias
// Should be used in fragment shader instead of vertex shader
float3 IllusionApplyShadowBias(float3 positionWS, float3 normalWS, float3 lightDirection)
{
#ifdef _MAIN_LIGHT_SHADOWS_CASCADE
    half cascadeIndex = ComputeCascadeIndex(positionWS);
#else
    half cascadeIndex = half(0.0);
#endif

    float4 shadowBias = _MainLightShadowCascadeBiases[cascadeIndex];
    
    float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
    float scale = -invNdotL * shadowBias.y;

    // In fragment shadow bias, bias direction is reversed
    positionWS = -lightDirection * shadowBias.xxx + positionWS;
    positionWS = normalWS * scale.xxx + positionWS;
    return positionWS;
}

// Vertex shader
float4 IllusionGetShadowCoord(VertexPositionInputs vertexInput)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS)
    return ComputeScreenPos(vertexInput.positionCS);
#else
    #if APPLY_SHADOW_BIAS_FRAGMENT
        return float4(vertexInput.positionWS, 1.0); // No need to calculate in vertex shader
    #else
        return TransformWorldToShadowCoord(vertexInput.positionWS);
    #endif
#endif
}

// Fragment shader in ASE template
float4 IllusionGetShadowCoord(float4 shadowCoord, float3 positionWS, float3 normalWS, half3 lightDir)
{
#if APPLY_SHADOW_BIAS_FRAGMENT
    #if !(defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS))
        float3 biasPositionWS = IllusionApplyShadowBias(positionWS, normalWS, lightDir);
        shadowCoord = TransformWorldToShadowCoord(biasPositionWS);
    #endif
#endif
    return shadowCoord;
}

// Fragment shader, shadowCoord should already be biased
half IllusionMainLightRealtimeShadow(float4 shadowCoord)
{
#if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    return half(1.0);
#elif defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS)
    return SampleScreenSpaceShadowmap(shadowCoord);
#else
    ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    return SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord,
        shadowSamplingData, shadowParams, false);
#endif
}

// Fragment shader lighting pass
half IllusionMainLightShadow(float4 shadowCoord, float3 positionWS, float3 normalWS, half3 lightDir, half4 shadowMask, half4 occlusionProbeChannels)
{
    float3 biasPositionWS = positionWS;
#if APPLY_SHADOW_BIAS_FRAGMENT
    // Recalculate shadowCoord when using shadow bias in fragment shader
    biasPositionWS = IllusionApplyShadowBias(positionWS, normalWS, lightDir);
    #if !(defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS))
        shadowCoord = TransformWorldToShadowCoord(biasPositionWS);
    #endif
#endif
    
    half realtimeShadow = IllusionMainLightRealtimeShadow(shadowCoord);

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (!SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS) && defined(_TRANSPARENT_PER_OBJECT_SHADOWS)
        realtimeShadow = min(realtimeShadow, MainLightPerObjectSceneShadow(positionWS, normalWS, lightDir));
#endif

#ifdef CALCULATE_BAKED_SHADOWS
    half bakedShadow = BakedShadow(shadowMask, occlusionProbeChannels);
#else
    half bakedShadow = half(1.0);
#endif

#ifdef MAIN_LIGHT_CALCULATE_SHADOWS
    half shadowFade = GetMainLightShadowFade(biasPositionWS);
#else
    half shadowFade = half(1.0);
#endif

    return MixRealtimeAndBakedShadows(realtimeShadow, bakedShadow, shadowFade);
}
#endif