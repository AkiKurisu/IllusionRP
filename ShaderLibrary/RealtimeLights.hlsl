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
Light IllusionGetAdditionalLight(uint i, InputData inputData, half4 shadowMask)
{
    Light light = GetAdditionalLight(i, inputData.positionWS, shadowMask);
    return light;
}
#endif