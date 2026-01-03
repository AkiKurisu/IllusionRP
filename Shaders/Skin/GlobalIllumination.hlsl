#ifndef SKIN_GLOBAL_ILLUMINATION_INCLUDED
#define SKIN_GLOBAL_ILLUMINATION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PreIntegratedFGD/PreIntegratedFGD.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateScreenSpaceReflection.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ForwardLightLoop.hlsl"

// max absolute error 9.0x10^-3
// Eberly's polynomial degree 1 - respect bounds
// 4 VGPR, 12 FR (8 FR, 1 QR), 1 scalar
// input [-1, 1] and output [0, PI]
half acosFast(half inX) 
{
    half x = abs(inX);
    half res = -0.156583f * x + 0.5 * PI;
    res *= sqrt(1.0f - x);
    return inX >= 0 ? res : PI - res;
}

float ApproximateConeConeIntersection(float ArcLength0, float ArcLength1, float AngleBetweenCones)
{
    float AngleDifference = abs(ArcLength0 - ArcLength1);

    float Intersection = smoothstep(0, 1.0, 1.0 - saturate((AngleBetweenCones - AngleDifference) / (ArcLength0 + ArcLength1 - AngleDifference)));

    return Intersection;
}

// Screen Space Bent Normal
float CalculateSpecularOcclusion(float3 N, float Roughness, float AO, float3 V, float3 BentNormalOcclusion)
{
    float ReflectionConeAngle = max(Roughness, .1f) * PI;
    float UnoccludedAngle = AO * PI;
    float3 ReflectionVector = reflect(-V, N);
    float AngleBetween = acosFast(dot(BentNormalOcclusion, ReflectionVector) / max(AO, .001f));
    float SpecularOcclusion = ApproximateConeConeIntersection(ReflectionConeAngle, UnoccludedAngle, AngleBetween);

    // Can't rely on the direction of the bent normal when close to fully occluded, lerp to shadowed
    SpecularOcclusion = lerp(0, SpecularOcclusion, saturate((UnoccludedAngle - .1f) / .2f));
    return SpecularOcclusion;
}

half3 SkinEnvironmentDiffuse(BRDFData brdfData, half3 bakedGI, half3 occlusion, InputData inputData, uint renderingLayers)
{
    half3 indirectDiffuse = EvaluateIndirectDiffuse(inputData.positionWS, inputData.normalWS, inputData.normalizedScreenSpaceUV, bakedGI);
    half3 color = indirectDiffuse * brdfData.diffuse;
    if (IsOnlyAOLightingFeatureEnabled())
    {
        color = half3(1,1,1);
    }

    return color * occlusion * GetIndirectDiffuseMultiplier(renderingLayers);
}

half3 SkinEnvironmentSpecular(BRDFData brdfData, half3 occlusion, InputData inputData, half perceptualRoughness)
{
    half3 viewDirectionWS = inputData.viewDirectionWS;
    float3 normalWS = inputData.normalWS;
    float3 positionWS = inputData.positionWS;
    float2 normalizedScreenSpaceUV = inputData.normalizedScreenSpaceUV;
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half normalizationFactor = SampleProbeVolumeReflectionNormalize(positionWS, normalWS, normalizedScreenSpaceUV, inputData.bakedGI, reflectVector);
    half3 indirectSpecular = GlossyEnvironmentReflection(
            reflectVector,
            positionWS,
            perceptualRoughness,
            1.0h,
            normalizedScreenSpaceUV
        );

    half fresnelTerm = Pow4(1.0 - NoV);
    half3 color = indirectSpecular * EnvironmentBRDFSpecular(brdfData, fresnelTerm);

    if (IsOnlyAOLightingFeatureEnabled())
    {
        color = half3(1,1,1);
    }

    return color * occlusion * normalizationFactor;
}

// Reference: HDRP Lit.PreLightData.GetPreLightData
half3 SkinIBLDiffuse(BRDFData brdfData, half3 bakedGI, half3 occlusion,
    InputData inputData, half perceptualRoughness, uint renderingLayers)
{
    float clampedNdotV = ClampNdotV(dot(inputData.normalWS, inputData.viewDirectionWS));
    
    float3 specularFGD;
    float3 diffuseFGD;
    float3 reflectivity;
    half3 indirectDiffuse = EvaluateIndirectDiffuse(inputData.positionWS, inputData.normalWS, inputData.normalizedScreenSpaceUV, bakedGI);
    indirectDiffuse *= brdfData.albedo;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, perceptualRoughness, 0, specularFGD, diffuseFGD, reflectivity);

    // Fallback to SkinEnvironmentDiffuse actually
#if USE_DIFFUSE_LAMBERT_BRDF
    diffuseFGD = 1;
#endif

    // Disney Diffuse FGD is in range [0.5, 1.5], normalized to Lambert equivalent
    // To avoid over-brightening, we can clamp or normalize it
    half3 result = indirectDiffuse * saturate(diffuseFGD);
    if (IsOnlyAOLightingFeatureEnabled())
    {
        result = half3(1,1,1);
    }
    return result * occlusion * GetIndirectDiffuseMultiplier(renderingLayers);
}

half3 SkinIBLSpecular(BRDFData brdfData, half3 occlusion, InputData inputData, half perceptualRoughness)
{
    float clampedNdotV = ClampNdotV(dot(inputData.normalWS, inputData.viewDirectionWS));

    float3 specularFGD;
    float3 diffuseFGD;
    float3 reflectivity;
    half3 reflectVector = reflect(-inputData.viewDirectionWS, inputData.normalWS);
    half normalizationFactor = SampleProbeVolumeReflectionNormalize(inputData.positionWS, inputData.normalWS, inputData.normalizedScreenSpaceUV, inputData.bakedGI, reflectVector);
    
    half3 indirectSpecular = 0;
    half hierarchyOpacity = 0;
    
#if _SCREEN_SPACE_REFLECTION
    half4 reflection = SampleScreenSpaceReflection(inputData.normalizedScreenSpaceUV);
    indirectSpecular += reflection.rgb; // accumulate since color is already premultiplied by opacity for SSR
    hierarchyOpacity = reflection.a;
#endif

    if (hierarchyOpacity < 1.0f)
    {
        half3 iblSpecular = GlossyEnvironmentReflection(
            reflectVector,
            inputData.positionWS,
            perceptualRoughness,
            1.0h,
            inputData.normalizedScreenSpaceUV
        ) * (1.0f - hierarchyOpacity);
            
        // [Reference: Physically Based Rendering in Filament]
        // horizon occlusion with falloff
        float horizon = min(1.0 + dot(reflectVector, inputData.normalWS), 1.0);
        iblSpecular *= horizon * horizon * normalizationFactor;
        indirectSpecular += iblSpecular;
    }
    half fresnelTerm = Pow4(1.0 - clampedNdotV);
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, perceptualRoughness, brdfData.specular * fresnelTerm,
        specularFGD, diffuseFGD, reflectivity);

    half3 result = indirectSpecular * specularFGD;
    
    if (IsOnlyAOLightingFeatureEnabled())
    {
        result = half3(1,1,1);
    }
    
    return result * occlusion;
}

#endif