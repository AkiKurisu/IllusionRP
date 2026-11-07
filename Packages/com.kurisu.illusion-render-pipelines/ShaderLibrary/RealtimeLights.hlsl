#ifndef ILLUSION_REALTIME_LIGHTS_INCLUDED
#define ILLUSION_REALTIME_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/Shadows.hlsl"

// shadowCoord should already be biased
Light IllusionGetMainLight(float4 shadowCoord)
{
    Light light = GetMainLight();
    light.shadowAttenuation = IllusionMainLightRealtimeShadow(shadowCoord);
    return light;
}

Light IllusionGetMainLight(float4 shadowCoord, float3 positionWS, float3 normalWS, half4 shadowMask)
{
    Light light = GetMainLight();
    light.shadowAttenuation = IllusionMainLightShadow(shadowCoord, positionWS, normalWS, light.direction, shadowMask, _MainLightOcclusionProbes);

#if defined(_LIGHT_COOKIES)
    real3 cookieColor = SampleMainLightCookie(positionWS);
    light.color *= cookieColor;
#endif

    return light;
}

// AO has been separated for diffuse and specular, so it will be applied in lighting.
Light IllusionGetMainLight(InputData inputData, half4 shadowMask)
{
    Light light = IllusionGetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.normalWS, shadowMask);
    return light;
}

#if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
half IllusionAdditionalLightShadow(int lightIndex, float3 positionWS, float3 normalWS,
    half3 lightDirection, half4 shadowMask, half4 occlusionProbeChannels)
{
    half4 shadowParams = GetAdditionalLightShadowParams(lightIndex);
    float4 bias = _AdditionalLightShadowReceiverBiases[lightIndex];
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float3 lightPosition = _AdditionalLightsBuffer[lightIndex].position.xyz;
#else
    float3 lightPosition = _AdditionalLightsPosition[lightIndex].xyz;
#endif
    float3 receiverPosition = positionWS + EvalShadow_NormalBiasProj(
        bias.x, 1.0, normalWS, distance(lightPosition, positionWS));
    half realtimeShadow = AdditionalLightRealtimeShadow(lightIndex, receiverPosition, lightDirection,
        shadowParams, GetAdditionalLightShadowSamplingData(lightIndex));
#ifdef CALCULATE_BAKED_SHADOWS
    half bakedShadow = BakedShadow(shadowMask, occlusionProbeChannels, shadowParams);
#else
    half bakedShadow = half(1.0);
#endif
    return MixRealtimeAndBakedShadows(realtimeShadow, bakedShadow, GetAdditionalLightShadowFade(positionWS));
}
#endif

// AO has been separated for diffuse and specular, so it will be applied in lighting.
Light IllusionGetAdditionalLight(uint i, InputData inputData, half4 shadowMask, bool receivePerObjectShadow)
{
#if APPLY_SHADOW_BIAS_FRAGMENT && defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
#if USE_CLUSTER_LIGHT_LOOP
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    Light light = GetAdditionalPerObjectLight(lightIndex, inputData.positionWS);
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    half4 occlusionProbeChannels = _AdditionalLightsBuffer[lightIndex].occlusionProbeChannels;
#else
    half4 occlusionProbeChannels = _AdditionalLightsOcclusionProbes[lightIndex];
#endif
    light.shadowAttenuation = IllusionAdditionalLightShadow(lightIndex, inputData.positionWS,
        inputData.normalWS, light.direction, shadowMask, occlusionProbeChannels);
#if defined(_LIGHT_COOKIES)
    light.color *= SampleAdditionalLightCookie(lightIndex, inputData.positionWS);
#endif
#else
    Light light = GetAdditionalLight(i, inputData.positionWS, shadowMask);
#endif
#if USE_CLUSTER_LIGHT_LOOP
    if (receivePerObjectShadow &&
        _PerObjSceneShadowSourceMode == PER_OBJECT_SHADOW_SOURCE_ADDITIONAL_DIRECTIONAL &&
        (int)i == _PerObjSceneShadowAdditionalLightIndex)
    {
#if defined(_SURFACE_TYPE_TRANSPARENT)
    #if defined(_TRANSPARENT_PER_OBJECT_SHADOWS)
        float perObjectVisibility = PerObjectDirectionalShadow(
            inputData.positionWS, inputData.normalWS, light.direction);
    #else
        float perObjectVisibility = 1.0;
    #endif
#elif defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS)
        float perObjectVisibility = IllusionAdditionalPerObjectScreenSpaceShadow(inputData.shadowCoord);
#else
        float perObjectVisibility = PerObjectDirectionalShadow(
            inputData.positionWS, inputData.normalWS, light.direction);
#endif
        light.shadowAttenuation = min(light.shadowAttenuation, perObjectVisibility);
    }
#endif
    return light;
}

Light IllusionGetAdditionalLight(uint i, InputData inputData, half4 shadowMask)
{
    return IllusionGetAdditionalLight(i, inputData, shadowMask, true);
}
#endif
