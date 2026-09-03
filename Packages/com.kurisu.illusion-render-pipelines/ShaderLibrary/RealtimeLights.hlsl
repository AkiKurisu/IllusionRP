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

// AO has been separated for diffuse and specular, so it will be applied in lighting.
Light IllusionGetAdditionalLight(uint i, InputData inputData, half4 shadowMask, bool receivePerObjectShadow)
{
    Light light = GetAdditionalLight(i, inputData.positionWS, shadowMask);
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
