// Fabric Lighting
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
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PreIntegratedFGD/PreIntegratedFGD.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/LightingData.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Fabric/FabricDefine.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ForwardLightLoop.hlsl"

///////////////////////////////////////////////////////////////////////////////
//                      Lighting Functions                                   //
///////////////////////////////////////////////////////////////////////////////

void SheenScattering(BRDFData brdfData, half NoH, half NoV, half NoL, out half SheenTerm)
{
    // @IllusionRP:
    // Since URP does not multiply INV_PI in Lambert Diffuse for artistic design considerations.
    // Specular term need multiply PI to maintain energy conservation.
#ifdef _SHEEN_VELET
    half D_Sheen = D_AshikhminNoPI(NoH, brdfData.roughness2);
#else
    half D_Sheen = D_CharlieNoPI(NoH, brdfData.roughness);
#endif
        
    half V_Sheen = V_Neubelt(NoL, NoV);
    SheenTerm = D_Sheen * V_Sheen;
#if REAL_IS_HALF
    SheenTerm  = SheenTerm  - HALF_MIN;
    SheenTerm  = clamp(SheenTerm , 0.0, 100.0); // Prevent FP16 overflow on mobiles
#endif
}

half3 AnisoFabricLighting(BRDFData brdfData, half3 lightColor, half3 lightDirectionWS, half lightAttenuation,
                          half3 normalWS, half3 viewDirectionWS, bool specularHighlightsOff,
                          AnisotropyData anisotropyData, SheenData SheenData, BRDFOcclusionFactor aoFactor)
{
    half Alpha = brdfData.roughness2;
    // Anisotropic parameters: ax and ay are the Roughness along the tangent and bitangent
    // Reference: [Kulla 2017, "Revisiting Physically Based Shading at Imageworks"]
    half aT = max(Alpha * (1.0 + anisotropyData.Anisotropy), 0.001f);
    half aB = max(Alpha * (1.0 - anisotropyData.Anisotropy), 0.001f);
    half3 H = normalize(lightDirectionWS + viewDirectionWS);
    half NoH = saturate(dot(normalWS, H));
    half NoV = saturate(abs(dot(normalWS, viewDirectionWS)) + 1e-5);
    half NoL = saturate(dot(normalWS, lightDirectionWS));
    half VoH = saturate(dot(viewDirectionWS, H));
    half ToV = dot(anisotropyData.T, viewDirectionWS);
    half ToL = dot(anisotropyData.T, lightDirectionWS);
    half ToH = dot(anisotropyData.T, H);
    half BoV = dot(anisotropyData.B, viewDirectionWS);
    half BoL = dot(anisotropyData.B, lightDirectionWS);
    half BoH = dot(anisotropyData.B, H);

    half3 Radiance = NoL * lightColor * lightAttenuation;
    
    half3 diffuse = Diffuse_OrenNayar(NoV, brdfData.diffuse, brdfData.roughness);

    // Cheap Subsurface Scattering
#ifdef FABRIC_SUBSURFACE_SCATTERING
    half wrap = 0.5f;
    half3 scatterColor = 0;
    half scatter = saturate(dot(normalWS, lightDirectionWS) + wrap) / (1 + wrap);
    diffuse += scatterColor * scatter;
#endif
    
    half3 brdf = diffuse * aoFactor.directAmbientOcclusion;

    #ifndef _SPECULARHIGHLIGHTS_OFF
    [branch] if (!specularHighlightsOff)
    {
        // Anisotropy Specular
        half DV = DV_SmithJointGGXAniso_Patch(ToH, BoH, NoH, ToV, BoV,NoV, ToL,BoL, NoL, aT, aB);

    #if REAL_IS_HALF
        DV  = DV  - HALF_MIN;
        DV  = clamp(DV , 0.0, 100.0); // Prevent FP16 overflow on mobiles
    #endif

        half3 F = F_Schlick(brdfData.specular, VoH);
        half3 AnisoSpecular = DV * F;

        // Sheen Specular
        // Use smooth normal (without detail)
        NoH = saturate(dot(SheenData.N, H));
        NoV = saturate(abs(dot(SheenData.N, viewDirectionWS)) + 1e-5);
        NoL = saturate(dot(SheenData.N, lightDirectionWS));

        half SheenTerm;
        SheenScattering(brdfData, NoH, NoV, NoL, SheenTerm);
        half3 SheenSpecular = SheenTerm * SheenData.Color;
        
        // lerp specular to get softer visual effect
        half3 LerpSpecular = lerp(AnisoSpecular, SheenSpecular, SheenData.Sheen);
        brdf += LerpSpecular * aoFactor.directSpecularOcclusion;
    }
    #endif // _SPECULARHIGHLIGHTS_OFF


    return brdf * Radiance;
}

half3 FabricLighting(BRDFData brdfData, half3 lightColor, half3 lightDirectionWS, half lightAttenuation,
                    half3 normalWS, half3 viewDirectionWS,
                    bool specularHighlightsOff, SheenData SheenData, BRDFOcclusionFactor aoFactor)
{
    half3 H = normalize(lightDirectionWS + viewDirectionWS);
    half NoL = saturate(dot(normalWS, lightDirectionWS));
    half3 radiance = lightColor * (lightAttenuation * NoL);
    
    half NoV = saturate(abs(dot(normalWS, viewDirectionWS)) + 1e-5);
    half3 diffuse = Diffuse_OrenNayar(NoV, brdfData.diffuse, brdfData.roughness);

    // Cheap Subsurface Scattering
#ifdef FABRIC_SUBSURFACE_SCATTERING
    half wrap = 0.5f;
    half3 scatterColor = 0;
    half scatter = saturate(dot(normalWS, lightDirectionWS) + wrap) / (1 + wrap);
    diffuse += scatterColor * scatter;
#endif
    
    half3 brdf = diffuse * aoFactor.directAmbientOcclusion;

    #ifndef _SPECULARHIGHLIGHTS_OFF
    [branch] if (!specularHighlightsOff)
    {
        half3 DefaultSpecular = LitSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS);

        // Sheen Specular
        // Use smooth normal (without detail)
        half NoH = saturate(dot(SheenData.N, H));
        NoV = saturate(abs(dot(SheenData.N, viewDirectionWS)) + 1e-5);
        NoL = saturate(dot(SheenData.N, lightDirectionWS));

        half SheenTerm;
        SheenScattering(brdfData, NoH, NoV, NoL, SheenTerm);
        half3 SheenSpecular = SheenTerm * SheenData.Color;

        // lerp specular to get softer visual effect
        half3 LerpSpecular = lerp(DefaultSpecular, SheenSpecular, SheenData.Sheen);
        brdf += LerpSpecular * aoFactor.directSpecularOcclusion;
    }
    #endif // _SPECULARHIGHLIGHTS_OFF

    return brdf * radiance;
}


half3 FabricLighting(BRDFData brdfData, Light light, half3 normalWS,
                    half3 viewDirectionWS, bool specularHighlightsOff, SheenData SheenData, BRDFOcclusionFactor aoFactor)
{
    return FabricLighting(brdfData, light.color, light.direction,
                         light.distanceAttenuation * light.shadowAttenuation, normalWS, viewDirectionWS,
                         specularHighlightsOff, SheenData, aoFactor);
}

half3 FabricGlobalIllumination(BRDFData brdfData, half3 bakedGI,
    BRDFOcclusionFactor aoFactor, float3 positionWS,
    half3 normalWS, half3 viewDirectionWS, float2 normalizedScreenSpaceUV, uint renderingLayers)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);
    
    // ============================ Diffuse Part ================================== //
    half3 indirectDiffuse = EvaluateIndirectDiffuse(positionWS, normalWS, normalizedScreenSpaceUV, bakedGI);
    half normalizationFactor = SampleProbeVolumeReflectionNormalize(positionWS, normalWS, normalizedScreenSpaceUV, bakedGI, reflectVector);
    // ============================ Diffuse Part ================================== //
    
    half3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness,
        1.0h, normalizedScreenSpaceUV) * normalizationFactor;

#ifdef PRE_INTEGRATED_FGD
    float3 specularFGD;
    float3 diffuseFGD;
    float3 reflectivity;
    #ifdef _SHEEN_VELET
        GetPreIntegratedFGDGGXAndDisneyDiffuse(NoV, brdfData.perceptualRoughness, brdfData.specular,
            specularFGD, diffuseFGD, reflectivity);
        #if USE_DIFFUSE_LAMBERT_BRDF
            diffuseFGD = 1;
        #endif
    #else
        GetPreIntegratedFGDCharlieAndFabricLambert(NoV, brdfData.perceptualRoughness, brdfData.specular,
            specularFGD, diffuseFGD, reflectivity);
    #endif
    indirectDiffuse *= diffuseFGD * brdfData.diffuse * aoFactor.indirectAmbientOcclusion;
    indirectSpecular *= specularFGD * aoFactor.indirectSpecularOcclusion;
#else
    indirectDiffuse *= diffuseFGD * brdfData.diffuse * aoFactor.indirectAmbientOcclusion;
    // Reference: BRDF.hlsl EnvironmentBRDF
    indirectSpecular *= EnvironmentBRDFSpecular(brdfData, fresnelTerm) * aoFactor.indirectSpecularOcclusion;
#endif

    half3 color = indirectDiffuse * GetIndirectDiffuseMultiplier(renderingLayers) + indirectSpecular;

    if (IsOnlyAOLightingFeatureEnabled())
    {
        color = aoFactor.indirectAmbientOcclusion + aoFactor.indirectSpecularOcclusion;
    }
    
    return color;
}

///////////////////////////////////////////////////////////////////////////////
//                      Fragment Functions                                   //
//       Used by ASE                                                         //
///////////////////////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////////////////////////
/// IllusionRP Fabric lighting...
////////////////////////////////////////////////////////////////////////////////
half4 FabricFragmentPBR(InputData inputData, SurfaceData surfaceData, AnisotropyData AnisotropyData, SheenData SheenData)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
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
#if EVALUATE_AO_MULTI_BOUNCE
    float NdotV = max(saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 0.00001);
    #ifdef _SPECULAR_SETUP
        half3 brdfDiffuse = brdfData.albedo;
    #else
        half3 brdfDiffuse = ComputeDiffuseColor(brdfData.albedo, surfaceData.metallic);
    #endif
    BRDFOcclusionFactor brdfOcclusionFactor = CreateBRDFOcclusionFactorMultiBounce(aoFactor, NdotV, brdfData.perceptualRoughness,
        surfaceData.occlusion, brdfDiffuse, surfaceData.occlusion, brdfData.specular);
#else
    BRDFOcclusionFactor brdfOcclusionFactor = CreateBRDFOcclusionFactor(aoFactor);
#endif
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = IllusionGetMainLight(inputData, shadowMask);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = FabricGlobalIllumination(brdfData, inputData.bakedGI,
                                              brdfOcclusionFactor, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS,
                                              inputData.normalizedScreenSpaceUV, meshRenderingLayers);
    
#ifdef _ANISOTROPY_ON
    // Calculate anisotropy bent normal
    half3 anisotropicDirection = AnisotropyData.Anisotropy >= 0.0 ? AnisotropyData.B : AnisotropyData.T;
    half3 anisotropicTangent = cross(anisotropicDirection, inputData.viewDirectionWS);
    half3 anisotropicNormal = cross(anisotropicTangent, anisotropicDirection);
    half3 bentNormal = normalize(lerp(inputData.normalWS, anisotropicNormal, abs(AnisotropyData.Anisotropy)));
#endif


    #ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
    #endif
    {
        // Since we use sheen light, clear coat is no need in cloth shader
        #ifdef _ANISOTROPY_ON
        lightingData.mainLightColor = AnisoFabricLighting(brdfData,mainLight.color, mainLight.direction,
                                                              mainLight.distanceAttenuation * mainLight.shadowAttenuation,
                                                              bentNormal, inputData.viewDirectionWS,
                                                              specularHighlightsOff, AnisotropyData, SheenData, brdfOcclusionFactor);
        #else
        lightingData.mainLightColor = FabricLighting(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS,
                                                    specularHighlightsOff, SheenData, brdfOcclusionFactor);
        #endif
    }
    
    #if _ADDITIONAL_LIGHTS
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
    #ifdef _ANISOTROPY_ON
            lightingData.additionalLightsColor += AnisoFabricLighting(brdfData, light.color, light.direction,
                                                                  light.distanceAttenuation * light.shadowAttenuation,
                                                                  bentNormal, inputData.viewDirectionWS, specularHighlightsOff,
                                                                  AnisotropyData, SheenData, brdfOcclusionFactor);
    #else
            lightingData.additionalLightsColor += FabricLighting(brdfData, light, inputData.normalWS, inputData.viewDirectionWS,
                                                                          specularHighlightsOff, SheenData, brdfOcclusionFactor);
    #endif
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
    #ifdef _ANISOTROPY_ON
            lightingData.additionalLightsColor += AnisoFabricLighting(brdfData, light.color, light.direction,
                                                                  light.distanceAttenuation * light.shadowAttenuation,
                                                                  bentNormal, inputData.viewDirectionWS, specularHighlightsOff,
                                                                  AnisotropyData, SheenData, brdfOcclusionFactor);
    #else
            lightingData.additionalLightsColor += FabricLighting(brdfData, light, inputData.normalWS, inputData.viewDirectionWS,
                                                                          specularHighlightsOff, SheenData, brdfOcclusionFactor);
    #endif
        }
    LIGHT_LOOP_END
    #endif

    #if _ADDITIONAL_LIGHTS_VERTEX
        lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

    #if REAL_IS_HALF
        // Clamp any half.inf+ to HALF_MAX
        return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
    #else
        return CalculateFinalColor(lightingData, surfaceData.alpha);
    #endif
}
#endif
