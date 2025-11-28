// Skin Lighting
#ifndef UNIVERSAL_LIGHTING_INCLUDED
#define UNIVERSAL_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateMaterial.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Skin/SkinDefine.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Skin/GlobalIllumination.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/LightingData.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/SubsurfaceScattering/ShaderVariablesSubsurface.hlsl"
#if SPHERICAL_GAUSSIAN_SSS
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalGaussian.hlsl"
#endif

///////////////////////////////////////////////////////////////////////////////
//                      Lighting Functions                                   //
///////////////////////////////////////////////////////////////////////////////

// Computes the fraction of light passing through the object.
// Evaluate Int{0, inf}{2 * Pi * r * R(sqrt(r^2 + d^2))}, where R is the diffusion profile.
// Note: 'volumeAlbedo' should be premultiplied by 0.25.
// Ref: Approximate Reflectance Profiles for Efficient Subsurface Scattering by Pixar (BSSRDF only).
float3 ComputeTransmittanceDisney(float3 S, float3 volumeAlbedo, float thickness)
{
    // Thickness and SSS mask are decoupled for artists.
    // In theory, we should modify the thickness by the inverse of the mask scale of the profile.
    // thickness /= subsurfaceMask;

    float3 exp_13 = exp2(((LOG2_E * (-1.0/3.0)) * thickness) * S); // Exp[-S * t / 3]

    // Premultiply & optimize: T = (1/4 * A) * (e^(-S * t) + 3 * e^(-S * t / 3))
    return volumeAlbedo * (exp_13 * (exp_13 * exp_13 + 3));
}

half3 SkinGGXSpecularLobe(BRDFData brdfData, half3 normalWS, half3 lightDirectionWS, half3 viewDirectionWS)
{
    return GGXBRDFSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS);
}

float3 EvaluateTransmittance_Punctual(half NdotL, SkinData SkinData)
{
    float thicknessInUnits       = -NdotL;
    float metersPerUnit          = _WorldScalesAndFilterRadiiAndThicknessRemaps[SkinData.DiffusionProfileIndex].x;
    float thicknessInMeters      = thicknessInUnits * metersPerUnit;
    float thicknessInMillimeters = thicknessInMeters * MILLIMETERS_PER_METER;

    // We need to make sure it's not less than the baked thickness to minimize light leaking.
    float dt = max(0, thicknessInMillimeters - SkinData.Thickness);
    float3 S = _ShapeParamsAndMaxScatterDists[SkinData.DiffusionProfileIndex].rgb;

    float3 exp_13 = exp2(((LOG2_E * (-1.0/3.0)) * dt) * S); // Exp[-S * dt / 3]

    // Approximate the decrease of transmittance by e^(-1/3 * dt * S).
    return SkinData.Transmittance * exp_13;
}

bool ShouldEvaluateThickObjectTransmission(half3 L, half3 normalWS)
{
    // Currently, we don't consider (NdotV < 0) as transmission.
    // TODO: ignore normal map? What about double sided-surfaces with one-sided normals?
    float NdotL = dot(normalWS, L);
    return NdotL < float(0.0);
}

half3 SkinDiffuse(BRDFData brdfData, half3 lightColor, half3 lightDirectionWS, half shadow, half3 normalWS,
    half3 viewDirectionWS, SkinData SkinData, bool isPunctualLight)
{
    float3 h = normalize(viewDirectionWS + lightDirectionWS);
    float hDotV = max(dot(h, viewDirectionWS), 0.0);
    half NdotL = saturate(dot(normalWS, lightDirectionWS));
    
    float NdotV = dot(normalWS, viewDirectionWS);
    float clampNdotV = ClampNdotV(NdotV);
    float LdotV = dot(lightDirectionWS, viewDirectionWS);
    half3 diffuseTerm = DirectBRDFDiffuseTermNoPI(NdotL, clampNdotV, LdotV, brdfData.perceptualRoughness);

    half3 radiance = brdfData.albedo * lightColor * shadow;
    half3 fersnelTerm = diffuseTerm * (1 - F_Schlick(SkinData.F0, hDotV));
    
#if SPHERICAL_GAUSSIAN_SSS
    half3 SG = SGDiffuseLighting(normalWS, lightDirectionWS, SkinData.Scatter);
    half3 DiffuseR = SG * fersnelTerm;
#else
    float clampNdotL = max(saturate(NdotL), 0.00001);
    half3 DiffuseR = clampNdotL * fersnelTerm;
#endif

    DiffuseR *= radiance;

    half3 Transmittance = SkinData.Transmittance;
    half flippedNdotL = ComputeWrappedDiffuseLighting(-NdotL, TRANSMISSION_WRAP_LIGHT);
    [branch] if (isPunctualLight)
    {
        Transmittance = (half3)EvaluateTransmittance_Punctual(NdotL, SkinData);
    }

    half3 DiffuseT = Transmittance * lightColor * brdfData.albedo;

    [branch]
    if (ShouldEvaluateThickObjectTransmission(lightDirectionWS, normalWS))
    {
        DiffuseT *= flippedNdotL;
    }
    else
    {
        DiffuseT *= flippedNdotL * shadow;
    }

    return DiffuseR + DiffuseT;
}

void DualLobeSmoothness(in half Smoothness, half Smoothness1, half Smoothness2, out half Lobe1Smoothness, out half Lobe2Smoothness)
{
    Lobe1Smoothness = saturate(Smoothness * Smoothness1);
    Lobe2Smoothness = saturate(Smoothness * Smoothness2);
}

half3 SkinSpecular(BRDFData brdfData, half3 lightColor, half3 lightDirectionWS, half lightAttenuation,
    half3 normalWS, half3 viewDirectionWS, SkinData SkinData)
{
    half clampedNdotL = max(saturate(dot(normalWS, lightDirectionWS)), 0.00001);
    half3 radiance = lightColor * (lightAttenuation * clampedNdotL);
    half3 brdf = half3(0, 0, 0);

    // Primary Lobe
    half3 SpecularLobe1 = SkinGGXSpecularLobe(brdfData, normalWS, lightDirectionWS, viewDirectionWS);

    // Secondary Lobe
    BRDFData brdfData2 = brdfData;
    brdfData2.roughness = max(PerceptualRoughnessToRoughness(SkinData.PerceptualRoughness), HALF_MIN_SQRT);
    brdfData2.roughness2 = brdfData2.roughness * brdfData2.roughness;
    half3 SpecularLobe2 = SkinGGXSpecularLobe(brdfData2, normalWS, lightDirectionWS, viewDirectionWS);

    // Dual Lobe Mix
    half3 SpecularLobeTerm = lerp(SpecularLobe1, SpecularLobe2, SkinData.LobeWeight);

#if REAL_IS_HALF
    SpecularLobeTerm = SpecularLobeTerm - HALF_MIN;
    SpecularLobeTerm = clamp(SpecularLobeTerm, 0.0, 1000.0); // Prevent FP16 overflow on mobiles
#endif
    
    brdf += SpecularLobeTerm * radiance;
    brdf = -min(-brdf, 0);
    return brdf;
}

half3 SkinDiffuse(BRDFData brdfData, Light light, InputData inputData, SkinData SkinData, BRDFOcclusionFactor aoFactor)
{
    return SkinDiffuse(brdfData, light.color * light.distanceAttenuation, light.direction,
        light.shadowAttenuation,
        inputData.normalWS, inputData.viewDirectionWS, SkinData, light.distanceAttenuation < float(1.0)) * aoFactor.directAmbientOcclusion;
}

half3 SkinSpecular(BRDFData brdfData, Light light, InputData inputData, SkinData SkinData, BRDFOcclusionFactor aoFactor)
{
    return SkinSpecular(brdfData, light.color, light.direction,
        light.distanceAttenuation * light.shadowAttenuation,
        inputData.normalWS, inputData.viewDirectionWS,
        SkinData) * aoFactor.directSpecularOcclusion;
}

half3 CalculateDiffuseLightingColor(LightingData lightingData, half3 albedo)
{
    half3 lightingColor = 0;

    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_GLOBAL_ILLUMINATION))
    {
        lightingColor += lightingData.giColor;
    }
    
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_MAIN_LIGHT))
    {
        lightingColor += lightingData.mainLightColor;
    }

    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_ADDITIONAL_LIGHTS))
    {
        lightingColor += lightingData.additionalLightsColor;
    }
    
    lightingColor *= albedo;
    return lightingColor;
}

half4 CalculateFinalDiffuseColor(LightingData lightingData, half alpha)
{
    half3 finalColor = CalculateDiffuseLightingColor(lightingData, 1);

    return half4(finalColor, alpha);
}

half4 SkinDiffuse(InputData inputData, SurfaceData surfaceData, SkinData skinData)
{
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

#if defined(DEBUG_DISPLAY)
    half4 debugColor;

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
#endif

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = IllusionCreateAmbientOcclusionFactor(inputData, surfaceData);
    BRDFOcclusionFactor brdfAOFactor = CreateBRDFOcclusionFactor(aoFactor);
#if EVALUATE_AO_MULTI_BOUNCE
    DiffuseOcclusionMultiBounce(brdfAOFactor, surfaceData.occlusion, brdfData.albedo);
#endif
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = IllusionGetMainLight(inputData, shadowMask);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    
    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    
#if PRE_INTEGRATED_FGD && !USE_DIFFUSE_LAMBERT_BRDF
    lightingData.giColor = SkinIBLDiffuse(brdfData, inputData.bakedGI, brdfAOFactor.indirectAmbientOcclusion,
        inputData, skinData.PerceptualRoughnessMix, meshRenderingLayers);
#else
    lightingData.giColor = SkinEnvironmentDiffuse(brdfData, inputData.bakedGI, brdfAOFactor.indirectAmbientOcclusion,
        inputData, meshRenderingLayers);
#endif

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = SkinDiffuse(brdfData, mainLight, inputData, skinData, brdfAOFactor);
    }
    
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += SkinDiffuse(brdfData, light, inputData, skinData, brdfAOFactor);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += SkinDiffuse(brdfData, light, inputData, skinData, brdfAOFactor);
        }
    LIGHT_LOOP_END
    

#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    return min(CalculateFinalDiffuseColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalDiffuseColor(lightingData, surfaceData.alpha);
#endif
}

half4 SkinSpecular(InputData inputData, SurfaceData surfaceData, SkinData SkinData)
{
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

#if defined(DEBUG_DISPLAY)
    half4 debugColor;

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
#endif
    
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = IllusionCreateAmbientOcclusionFactor(inputData, surfaceData);
    BRDFOcclusionFactor brdfAOFactor = CreateBRDFOcclusionFactor(aoFactor);
#if EVALUATE_AO_MULTI_BOUNCE
    float NdotV = max(saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 0.00001);
    SpecularOcclusionMultiBounce(brdfAOFactor, NdotV, brdfData.perceptualRoughness, surfaceData.occlusion, brdfData.specular);
#endif
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = IllusionGetMainLight(inputData, shadowMask);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    
    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    
#if PRE_INTEGRATED_FGD
    lightingData.giColor = SkinIBLSpecular(brdfData, brdfAOFactor.indirectSpecularOcclusion, inputData, SkinData.PerceptualRoughnessMix);
#else
    lightingData.giColor = SkinEnvironmentSpecular(brdfData, brdfAOFactor.indirectSpecularOcclusion, inputData, SkinData.PerceptualRoughnessMix);
#endif


#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = SkinSpecular(brdfData, mainLight, inputData, SkinData, brdfAOFactor);
    }
    
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += SkinSpecular(brdfData, light, inputData, SkinData, brdfAOFactor);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += SkinSpecular(brdfData, light, inputData, SkinData, brdfAOFactor);
        }
    LIGHT_LOOP_END
    

#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

half3 PreModifySubsurfaceScatteringAlbedo(in half3 albedo, in half3 subsurfaceAlbedo)
{
    half3 modifiedAlbedo = 1;
#if PRE_POST_SCATTER
    #ifdef _CUSTOM_SUBSURFACE_ALBEDO
        modifiedAlbedo = subsurfaceAlbedo;
    #else
        modifiedAlbedo = sqrt(albedo);
    #endif
#endif
    return modifiedAlbedo;
}

half3 PostModifySubsurfaceScatteringAlbedo(in half3 albedo)
{
#if PRE_POST_SCATTER
    return sqrt(albedo);
#else
    return albedo;
#endif
}

half4 CompositeSkinLighting(half4 diffuseLighting, half4 specularLighting)
{
    return half4(diffuseLighting.rgb * GetCurrentExposureMultiplier() + specularLighting.rgb, specularLighting.a);
}
#endif
