#ifndef ILLUSION_GLOBAL_ILLUMINATION_INCLUDED
#define ILLUSION_GLOBAL_ILLUMINATION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PreIntegratedFGD/PreIntegratedFGD.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateScreenSpaceReflection.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateMaterial.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ForwardLightLoop.hlsl"

half3 HybridGlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
                               half3 bakedGI, BRDFOcclusionFactor aoFactor, float3 positionWS,
                               half3 normalWS, half3 viewDirectionWS, float2 normalizedScreenSpaceUV,
                               uint renderingLayers)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);

    // ============================ Diffuse Part ================================== //
    half3 indirectDiffuse = EvaluateIndirectDiffuse(positionWS, normalWS, normalizedScreenSpaceUV, bakedGI);
    half normalizationFactor = SampleProbeVolumeReflectionNormalize(positionWS, normalWS, normalizedScreenSpaceUV, bakedGI, reflectVector);
    // ============================ Diffuse Part ================================== //

    // ============================ Specular Part ================================== //
    half3 indirectSpecular = 0;
    half hierarchyOpacity = 0.0f;
    
#if (SURFACE_TYPE_RECEIVE_SSR && _SCREEN_SPACE_REFLECTION)
    half4 reflection = SampleScreenSpaceReflection(normalizedScreenSpaceUV);
    indirectSpecular += reflection.rgb; // accumulate since color is already premultiplied by opacity for SSR
    hierarchyOpacity = reflection.a;
#endif

    if (hierarchyOpacity < 1.0f)
    {
        half3 iblSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness,
        1.0h, normalizedScreenSpaceUV) * (1.0f - hierarchyOpacity);
        // [Reference: Physically Based Rendering in Filament]
        // horizon occlusion with falloff
        float horizon = min(1.0 + dot(reflectVector, normalWS), 1.0);
        iblSpecular *= horizon * horizon * normalizationFactor;
        indirectSpecular += iblSpecular;
    }
    // ============================ Specular Part ================================== //


    // ============================== Composite ==================================== //
#if PRE_INTEGRATED_FGD
    float3 specularFGD;
    float3 diffuseFGD;
    float3 reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(NoV, brdfData.perceptualRoughness, brdfData.specular,
            specularFGD, diffuseFGD, reflectivity);
    #if USE_DIFFUSE_LAMBERT_BRDF
        diffuseFGD = 1;
    #endif
    indirectDiffuse = indirectDiffuse * diffuseFGD * brdfData.diffuse * aoFactor.indirectAmbientOcclusion;
    indirectSpecular = indirectSpecular * specularFGD * aoFactor.indirectSpecularOcclusion;
#else
    indirectDiffuse = indirectDiffuse * brdfData.diffuse * aoFactor.indirectAmbientOcclusion;
    // Reference: BRDF.hlsl EnvironmentBRDF
    indirectSpecular = indirectSpecular * EnvironmentBRDFSpecular(brdfData, fresnelTerm) * aoFactor.indirectSpecularOcclusion;
#endif
    indirectDiffuse *= GetIndirectDiffuseMultiplier(renderingLayers);
    half3 color = indirectDiffuse + indirectSpecular;
    // ============================== Composite ==================================== //

    // ================================ Debug ====================================== //
    if (IsOnlyAOLightingFeatureEnabled())
    {
        color = aoFactor.indirectAmbientOcclusion + aoFactor.indirectSpecularOcclusion;
    }
    // ================================ Debug ====================================== //
    
#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half3 coatIndirectSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfDataClearCoat.perceptualRoughness, 1.0h, normalizedScreenSpaceUV)* normalizationFactor;
    // TODO: "grazing term" causes problems on full roughness
    half3 coatColor = EnvironmentBRDFClearCoat(brdfDataClearCoat, clearCoatMask, coatIndirectSpecular, fresnelTerm);

    // Blend with base layer using khronos glTF recommended way using NoV
    // Smooth surface & "ambiguous" lighting
    // NOTE: fresnelTerm (above) is pow4 instead of pow5, but should be ok as blend weight.
    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * fresnelTerm;
    return color * (1.0 - coatFresnel * clearCoatMask) + coatColor * aoFactor.indirectSpecularOcclusion;
#else
    return color;
#endif
}

#endif