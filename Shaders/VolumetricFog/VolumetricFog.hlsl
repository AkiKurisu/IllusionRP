#ifndef VOLUMETRIC_FOG_INCLUDED
#define VOLUMETRIC_FOG_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"

#if UNITY_VERSION >= 202310 && _PROBE_VOLUME_CONTRIBUTION_ENABLED
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        #include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
    #endif
#endif
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/DeclareDownsampledDepthTexture.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ProjectionUtils.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/VolumetricShadows.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/VolumetricFog/ShaderVariablesVolumetricFog.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/EvaluateProbeVolume.hlsl"

// Computes the ray origin, direction, and returns the reconstructed world position for orthographic projection.
float3 ComputeOrthographicParams(float2 uv, float depth, out float3 ro, out float3 rd)
{
    float4x4 viewMatrix = UNITY_MATRIX_V;
    float2 ndc = uv * 2.0 - 1.0;
    
    rd = normalize(-viewMatrix[2].xyz);
    float3 rightOffset = normalize(viewMatrix[0].xyz) * (ndc.x * unity_OrthoParams.x);
    float3 upOffset = normalize(viewMatrix[1].xyz) * (ndc.y * unity_OrthoParams.y);
    float3 fwdOffset = rd * depth;
    
    float3 posWs = GetCameraPositionWS() + fwdOffset + rightOffset + upOffset;
    ro = posWs - fwdOffset;

    return posWs;
}

// Calculates the initial raymarching parameters.
void CalculateRaymarchingParams(float2 uv, uint2 positionSS, out float3 ro, out float3 rd, out float iniOffsetToNearPlane, out float offsetLength, out float3 rdPhase)
{
#if defined(SHADER_STAGE_COMPUTE)
    float depth = LoadDownsampledSceneDepth(positionSS);
#else
    float depth = SampleDownsampledSceneDepth(uv);
#endif
    float3 posWS;
    
    UNITY_BRANCH
    if (unity_OrthoParams.w <= 0)
    {
        ro = GetCameraPositionWS();
#if !UNITY_REVERSED_Z
        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
#endif
        posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
        float3 offset = posWS - ro;
        offsetLength = length(offset);
        rd = offset / offsetLength;
        rdPhase = rd;
        
        // In perspective, ray direction should vary in length depending on which fragment we are at.
        float3 camFwd = normalize(-UNITY_MATRIX_V[2].xyz);
        float cos = dot(camFwd, rd);
        float fragElongation = 1.0 / cos;
        iniOffsetToNearPlane = fragElongation * _ProjectionParams.y;
    }
    else
    {
        depth = LinearEyeDepthOrthographic(depth);
        posWS = ComputeOrthographicParams(uv, depth, ro, rd);
        offsetLength = depth;
        
        // Fake the ray direction that will be used to calculate the phase, so we can still use anisotropy in orthographic mode.
        rdPhase = normalize(posWS - GetCameraPositionWS());
        iniOffsetToNearPlane = _ProjectionParams.y;
    }
}

// Gets the main light phase function.
float GetMainLightPhase(float3 rd)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return 0.0;
#else
    return CornetteShanksPhaseFunction(SampleAnisotropy(_CustomAdditionalLightsCount), dot(rd, GetMainLight().direction));
#endif
}

// Gets the fog density at the given world height.
half GetFogDensity(float posWSy)
{
    half t = half(saturate((posWSy - _BaseHeight) / (_MaximumHeight - _BaseHeight)));
    t = 1.0 - t;
    t = lerp(t, 0.0, posWSy < _GroundHeight);

    return _Density * t;
}

// Gets the GI evaluation from the probe volume at one raymarch step.
half3 GetProbeVolumeEvaluation(float2 uv, float3 posWS, half density)
{
    half3 diffuseGI = half3(0.0, 0.0, 0.0);

#if _PROBE_VOLUME_CONTRIBUTION_ENABLED
    // From Unity APV
    #if (UNITY_VERSION >= 202310) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
        float3 diffuseLighting;
        EvaluateAdaptiveProbeVolume(posWS, uv * _ScreenSize.xy, diffuseLighting);
        diffuseGI = half3(diffuseLighting) * half(_ProbeVolumeContributionWeight) * density;
    #else
        // From IllusionRP PRTGI
        diffuseGI = SampleProbeVolume(posWS, 0, 0) * half(_ProbeVolumeContributionWeight) * density;
    #endif
#endif
 
    return diffuseGI;
}

// Gets the main light color at one raymarch step.
half3 GetStepMainLightColor(float3 currPosWS, float phaseMainLight, half density)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return float3(0.0, 0.0, 0.0);
#endif
    Light mainLight = GetMainLight();
    float4 shadowCoord = TransformWorldToShadowCoord(currPosWS);
    mainLight.shadowAttenuation = VolumetricMainLightRealtimeShadow(shadowCoord);
#if _LIGHT_COOKIES
    mainLight.color *= SampleMainLightCookie(currPosWS);
#endif
    half lightTerm = half(mainLight.shadowAttenuation * phaseMainLight * density * SampleScattering(_CustomAdditionalLightsCount));
    return mainLight.color * _Tint * lightTerm;
}

// Gets the accumulated color from additional lights at one raymarch step.
half3 GetStepAdditionalLightsColor(float2 uv, float3 currPosWS, float3 rd, half density)
{
#if _ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED
    return half3(0.0, 0.0, 0.0);
#endif
#if _FORWARD_PLUS
    // Forward+ rendering path needs this data before the light loop.
    InputData inputData = (InputData)0;
    inputData.normalizedScreenSpaceUV = uv;
    inputData.positionWS = currPosWS;
#endif
    half3 additionalLightsColor = half3(0.0, 0.0, 0.0);
                
    // Loop differently through lights in Forward+ while considering Forward and Deferred too.
    LIGHT_LOOP_BEGIN(_CustomAdditionalLightsCount)
        UNITY_BRANCH
        if (SampleScattering(lightIndex) <= 0.0)
            continue;

        Light additionalLight = GetAdditionalPerObjectLight(lightIndex, currPosWS);
        additionalLight.shadowAttenuation = VolumetricAdditionalLightRealtimeShadow(lightIndex, currPosWS, additionalLight.direction);
#if _LIGHT_COOKIES
        additionalLight.color *= SampleAdditionalLightCookie(lightIndex, currPosWS);
#endif
        // See universal\ShaderLibrary\RealtimeLights.hlsl - GetAdditionalPerObjectLight.
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 additionalLightPos = _AdditionalLightsBuffer[lightIndex].position;
#else
        float4 additionalLightPos = _AdditionalLightsPosition[lightIndex];
#endif
        // This is useful for both spotlights and pointlights. For the latter it is specially true when the point light is inside some geometry and casts shadows.
        // Gradually reduce additional lights scattering to zero at their origin to try to avoid flicker-aliasing.
        float3 distToPos = additionalLightPos.xyz - currPosWS;
        float distToPosMagnitudeSq = dot(distToPos, distToPos);
        float newScattering = smoothstep(0.0, SampleRadiiSq(lightIndex), distToPosMagnitudeSq) ;
        newScattering *= newScattering;
        newScattering *= SampleScattering(lightIndex);

        // If directional lights are also considered as additional lights when more than 1 is used, ignore the previous code when it is a directional light.
        // They store direction in additionalLightPos.xyz and have .w set to 0, while point and spotlights have it set to 1.
        // newScattering = lerp(1.0, newScattering, additionalLightPos.w);
    
        float phase = CornetteShanksPhaseFunction(SampleAnisotropy(lightIndex), dot(rd, additionalLight.direction));
        half lightTerm = half(additionalLight.shadowAttenuation * additionalLight.distanceAttenuation * phase * density * newScattering);
        additionalLightsColor += additionalLight.color * lightTerm;
    LIGHT_LOOP_END

    return additionalLightsColor;
}

// Calculates the volumetric fog. Returns the color in the RGB channels and transmittance in alpha.
half4 VolumetricFog(float2 uv, float2 positionCS) // positionCS is actually positionSS when using ComputeShader.
{
    float3 ro;
    float3 rd;
    float iniOffsetToNearPlane;
    float offsetLength;
    float3 rdPhase;

    CalculateRaymarchingParams(uv, (uint2)positionCS, ro, rd, iniOffsetToNearPlane, offsetLength, rdPhase);

    offsetLength -= iniOffsetToNearPlane;
    float3 roNearPlane = ro + rd * iniOffsetToNearPlane;
    float stepLength = (_Distance - iniOffsetToNearPlane) / (float)_MaxSteps;
    float jitter = stepLength * InterleavedGradientNoise(positionCS, _FrameCount);

    float phaseMainLight = GetMainLightPhase(rdPhase);
    float minusStepLengthTimesAbsortion = -stepLength * _Absortion;
                
    half3 volumetricFogColor = float3(0.0, 0.0, 0.0);
    half transmittance = 1.0;

    UNITY_LOOP
    for (int i = 0; i < _MaxSteps; ++i)
    {
        float dist = jitter + i * stepLength;
        
        UNITY_BRANCH
        if (dist >= offsetLength)
            break;

        // We are making the space between the camera position and the near plane "non existant", as if fog did not exist there.
        // However, it removes a lot of noise when in closed environments with an attenuation that makes the scene darker
        // and certain combinations of field of view, raymarching resolution and camera near plane.
        // In those edge cases, it looks so much better, specially when near plane is higher than the minimum (0.01) allowed.
        float3 currPosWS = roNearPlane + rd * dist;
        half density = GetFogDensity(currPosWS.y);
                    
        UNITY_BRANCH
        if (density <= 0.0)
            continue;

        half stepAttenuation = exp(minusStepLengthTimesAbsortion * density);
        transmittance *= stepAttenuation;

        half3 apvColor = GetProbeVolumeEvaluation(uv, currPosWS, density);
        half3 mainLightColor = GetStepMainLightColor(currPosWS, phaseMainLight, density);
        half3 additionalLightsColor = GetStepAdditionalLightsColor(uv, currPosWS, rd, density);
        
        // TODO: Additional contributions? Reflection probes, etc...
        half3 stepColor = apvColor + mainLightColor + additionalLightsColor;
        volumetricFogColor += (stepColor * (transmittance * stepLength));
        
        // Early exit: break out when transmittance reaches low threshold
        UNITY_BRANCH
        if (transmittance < _TransmittanceThreshold)
        {
            // Remap transmittance to avoid sudden cutoff artifacts
            // Smoothly transition from threshold to 0 over remaining distance
            half remainingSteps = (float)_MaxSteps - (float)i - 1.0;
            half remapFactor = remainingSteps / (float)_MaxSteps;
            transmittance = lerp(0.0, _TransmittanceThreshold, remapFactor);
            break;
        }
    }

    volumetricFogColor *= GetCurrentExposureMultiplier();
    return half4(volumetricFogColor, transmittance);
}

#endif