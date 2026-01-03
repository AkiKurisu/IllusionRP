#ifndef UNIVERSAL_LIGHTING_INCLUDED
#define UNIVERSAL_LIGHTING_INCLUDED

#define USE_DIFFUSE_LAMBERT_BRDF        0   // Set 1 to use Disney Diffuse model.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateMaterial.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/LightingData.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/GlobalIllumination.hlsl"


///////////////////////////////////////////////////////////////////////////////
//                      Lighting Functions                                   //
///////////////////////////////////////////////////////////////////////////////

half3 LightingSpecular(half3 lightColor, half3 lightDir, half3 normal, half3 viewDir, half4 specular, half smoothness)
{
    float3 halfVec = SafeNormalize(float3(lightDir) + float3(viewDir));
    half NdotH = half(saturate(dot(normal, halfVec)));
    half modifier = pow(NdotH, smoothness);
    // NOTE: In order to fix internal compiler error on mobile platforms, this needs to be float3
    float3 specularReflection = specular.rgb * modifier;
    return lightColor * specularReflection;
}

half3 LightingPhysicallyBased(BRDFData brdfData, BRDFData brdfDataClearCoat,
    half3 lightColor, half3 lightDirectionWS, 
    float lightAttenuation, half occlusion,
    half3 normalWS, half3 viewDirectionWS,
    half clearCoatMask, bool specularHighlightsOff, BRDFOcclusionFactor aoFactor)
{
    float3 h = SafeNormalize(float3(viewDirectionWS) + float3(lightDirectionWS));
    half NdotL = dot(normalWS, lightDirectionWS);
    float hDotV = max(dot(h, viewDirectionWS), 0.0);
    half NdotH = saturate(dot(normalWS, h));
    
    lightAttenuation *= NdotL >= 0.0 ? ComputeMicroShadowing(occlusion, NdotL, _MicroShadowOpacity) : 1.0;
    NdotL = saturate(NdotL);
    
    half3 radiance = lightColor * (lightAttenuation * NdotL);
    float NdotV = dot(normalWS, viewDirectionWS);
    float clampNdotV = ClampNdotV(NdotV);
    float LdotV = dot(lightDirectionWS, viewDirectionWS);

#ifdef _DISNEY_DIFFUSE_BURLEY
    half3 diffuseTerm = DirectBRDFDiffuseTermNoPI(NdotL, clampNdotV, LdotV, brdfData.perceptualRoughness).xxx;
    diffuseTerm *= brdfData.diffuse;
#else
    half3 diffuseTerm = Diffuse_GGX_Rough_NoPI(brdfData.diffuse, brdfData.perceptualRoughness, clampNdotV, NdotL, hDotV, NdotH);
#endif

    half3 brdf = diffuseTerm * aoFactor.directAmbientOcclusion;
    
#ifndef _SPECULARHIGHLIGHTS_OFF
    [branch] if (!specularHighlightsOff)
    {
        brdf +=  LitSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS) * aoFactor.directSpecularOcclusion;

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
        // Clear coat evaluates the specular a second timw and has some common terms with the base specular.
        // We rely on the compiler to merge these and compute them only once.
        half brdfCoat = kDielectricSpec.r * DirectBRDFSpecular(brdfDataClearCoat, normalWS, lightDirectionWS, viewDirectionWS);

        // Mix clear coat and base layer using khronos glTF recommended formula
        // https://github.com/KhronosGroup/glTF/blob/master/extensions/2.0/Khronos/KHR_materials_clearcoat/README.md
        // Use NoV for direct too instead of LoH as an optimization (NoV is light invariant).
        half NoV = saturate(dot(normalWS, viewDirectionWS));
        // Use slightly simpler fresnelTerm (Pow4 vs Pow5) as a small optimization.
        // It is matching fresnel used in the GI/Env, so should produce a consistent clear coat blend (env vs. direct)
        half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * Pow4(1.0 - NoV);

        brdf = brdf * (1.0 - clearCoatMask * coatFresnel) + brdfCoat * clearCoatMask * aoFactor.directSpecularOcclusion;
#endif // _CLEARCOAT
    }
#endif // _SPECULARHIGHLIGHTS_OFF

    return brdf * radiance;
}

half3 LightingPhysicallyBased(BRDFData brdfData, BRDFData brdfDataClearCoat, Light light, 
    InputData inputData, SurfaceData surfaceData,
    bool specularHighlightsOff, BRDFOcclusionFactor aoFactor)
{
    return LightingPhysicallyBased(brdfData, brdfDataClearCoat, light.color, light.direction,
        light.distanceAttenuation * light.shadowAttenuation, surfaceData.occlusion,
        inputData.normalWS,
        inputData.viewDirectionWS, surfaceData.clearCoatMask, specularHighlightsOff, aoFactor);
}

///////////////////////////////////////////////////////////////////////////////
//                      Fragment Functions                                   //
//       Used by ShaderGraph and others builtin renderers                    //
///////////////////////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////////////////////////
/// PBR lighting...
////////////////////////////////////////////////////////////////////////////////
half4 UniversalFragmentPBR(InputData inputData, SurfaceData surfaceData)
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

    // Clear-coat calculation...
    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = IllusionCreateAmbientOcclusionFactor(inputData, surfaceData);
#if EVALUATE_AO_MULTI_BOUNCE
    #ifdef _SPECULAR_SETUP
        half3 brdfDiffuse = brdfData.albedo;
    #else
        half3 brdfDiffuse = ComputeDiffuseColor(brdfData.albedo, surfaceData.metallic);
    #endif
    float NdotV = max(saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 0.00001);
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
    
    lightingData.giColor = HybridGlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, brdfOcclusionFactor, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS,
                                              inputData.normalizedScreenSpaceUV, meshRenderingLayers);

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat, mainLight,
                                                              inputData, surfaceData, 
                                                              specularHighlightsOff, brdfOcclusionFactor);
    }

    #if defined(_ADDITIONAL_LIGHTS)
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
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData, surfaceData, 
                                                                          specularHighlightsOff, brdfOcclusionFactor);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData, surfaceData, 
                                                                          specularHighlightsOff, brdfOcclusionFactor);
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
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
