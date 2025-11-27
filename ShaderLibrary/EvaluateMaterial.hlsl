// From HDRP EvaluateMaterial.hlsl
#ifndef EVALUATE_MATERIAL_INCLUDED
#define EVALUATE_MATERIAL_INCLUDED

// Includes
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

// Set value when enable RTAO
#define _SpecularOcclusionBlend         half(1.0)

// Per-shader ao intensity factor
#ifndef _AmbientOcclusionIntensity
    #define _AmbientOcclusionIntensity  half(1.0)
#endif

// Per-shader ao toggle
#ifndef _NOT_RECEIVE_OCCLUSION
    #define _NOT_RECEIVE_OCCLUSION      0
#endif

#define TRANSPARENT_RECEIVE_OCCLUSION   (defined(_SURFACE_TYPE_TRANSPARENT) && defined(_TRANSPARENT_WRITE_DEPTH))

#define SURFACE_TYPE_RECEIVE_OCCLUSION  (!defined(_SURFACE_TYPE_TRANSPARENT) || TRANSPARENT_RECEIVE_OCCLUSION)

struct BRDFOcclusionFactor
{
    half3 indirectAmbientOcclusion;
    half3 directAmbientOcclusion;
    half3 indirectSpecularOcclusion;
    half3 directSpecularOcclusion;
};

// Use GTAOMultiBounce approximation for ambient occlusion (allow to get a tint from the fresnel0)
void SpecularOcclusionMultiBounce(inout BRDFOcclusionFactor aoFactor, float NdotV,
    float perceptualRoughness, float specularOcclusionFromData, float3 fresnel0)
{
    half roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    // This specular occlusion formulation make sense only with SSAO. When we use Raytracing AO we support different range (local, medium, sky). When using medium or
    // sky occlusion, the result on specular occlusion can be a disaster (all is black). Thus, we use _SpecularOcclusionBlend when using RTAO to disable this trick.
    half indirectSpecularOcclusion = lerp(half(1.0), GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(NdotV), aoFactor.indirectAmbientOcclusion.x, roughness), _SpecularOcclusionBlend);
    half directSpecularOcclusion = lerp(half(1.0), indirectSpecularOcclusion, _AmbientOcclusionParam.w);

    aoFactor.indirectSpecularOcclusion = GTAOMultiBounce(min(specularOcclusionFromData, indirectSpecularOcclusion), fresnel0);
    // Note: when affecting direct lighting we don't used the fake bounce.
    aoFactor.directSpecularOcclusion = directSpecularOcclusion.xxx;
}

// Use GTAOMultiBounce approximation for ambient occlusion (allow to get a tint from the diffuseColor)
void DiffuseOcclusionMultiBounce(inout BRDFOcclusionFactor aoFactor,
    float ambientOcclusionFromData, float3 diffuseColor)
{
    aoFactor.indirectAmbientOcclusion = GTAOMultiBounce(min(ambientOcclusionFromData, aoFactor.indirectAmbientOcclusion.x), diffuseColor);
    aoFactor.directAmbientOcclusion = aoFactor.directAmbientOcclusion.xxx;
}

BRDFOcclusionFactor CreateBRDFOcclusionFactor(AmbientOcclusionFactor aoFactor)
{
    BRDFOcclusionFactor brdfOcclusionFactor = (BRDFOcclusionFactor)0;
    brdfOcclusionFactor.directAmbientOcclusion = aoFactor.directAmbientOcclusion;
    brdfOcclusionFactor.indirectAmbientOcclusion = aoFactor.indirectAmbientOcclusion;
    brdfOcclusionFactor.directSpecularOcclusion = aoFactor.directAmbientOcclusion;
    brdfOcclusionFactor.indirectSpecularOcclusion = aoFactor.indirectAmbientOcclusion;
    if (!IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        brdfOcclusionFactor.directAmbientOcclusion = half3(1.0f, 1.0f, 1.0f);
        brdfOcclusionFactor.indirectAmbientOcclusion = half3(1.0f, 1.0f, 1.0f);
        brdfOcclusionFactor.directSpecularOcclusion = half3(1.0f, 1.0f, 1.0f);
        brdfOcclusionFactor.indirectSpecularOcclusion = half3(1.0f, 1.0f, 1.0f);
    }
    return brdfOcclusionFactor;
}

// Create multi bound occlusion factor for diffuse and specular
BRDFOcclusionFactor CreateBRDFOcclusionFactorMultiBounce(AmbientOcclusionFactor aoFactor, float NdotV,
    float perceptualRoughness, float ambientOcclusionFromData /* occlusion in URP */, float3 diffuseColor,
    float specularOcclusionFromData /* occlusion in URP */, float3 fresnel0)
{
    BRDFOcclusionFactor brdfOcclusionFactor = CreateBRDFOcclusionFactor(aoFactor);
    SpecularOcclusionMultiBounce(brdfOcclusionFactor, NdotV, perceptualRoughness, specularOcclusionFromData, fresnel0);
    DiffuseOcclusionMultiBounce(brdfOcclusionFactor, ambientOcclusionFromData, diffuseColor);
    if (!IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        brdfOcclusionFactor.directAmbientOcclusion = half3(1.0f, 1.0f, 1.0f);
        brdfOcclusionFactor.indirectAmbientOcclusion = half3(1.0f, 1.0f, 1.0f);
        brdfOcclusionFactor.directSpecularOcclusion = half3(1.0f, 1.0f, 1.0f);
        brdfOcclusionFactor.indirectSpecularOcclusion = half3(1.0f, 1.0f, 1.0f);
    }
    return brdfOcclusionFactor;
}

half3 SampleScreenSpaceBentNormal(float2 normalizedScreenSpaceUV)
{
    float2 uv = UnityStereoTransformScreenSpaceTex(normalizedScreenSpaceUV);
    half4 packedAO = half4(SAMPLE_TEXTURE2D_X(_ScreenSpaceOcclusionTexture, sampler_ScreenSpaceOcclusionTexture, uv));
    return packedAO.gba * half(2.0f) - half(1.0f);
}

// Transparent with depth post pass can also receive ambient occlusion
AmbientOcclusionFactor IllusionGetScreenSpaceAmbientOcclusion(float2 normalizedScreenSpaceUV)
{
    AmbientOcclusionFactor aoFactor;

#if defined(_SCREEN_SPACE_OCCLUSION) && SURFACE_TYPE_RECEIVE_OCCLUSION && !_NOT_RECEIVE_OCCLUSION
    float ssao = saturate(SampleAmbientOcclusion(normalizedScreenSpaceUV) + (1.0 - _AmbientOcclusionParam.x));
    aoFactor.indirectAmbientOcclusion = ssao;
    aoFactor.directAmbientOcclusion = lerp(half(1.0), ssao, _AmbientOcclusionParam.w * _AmbientOcclusionIntensity);
#else
    aoFactor.directAmbientOcclusion = half(1.0);
    aoFactor.indirectAmbientOcclusion = half(1.0);
#endif

#if defined(DEBUG_DISPLAY)
    switch(_DebugLightingMode)
    {
    case DEBUGLIGHTINGMODE_LIGHTING_WITHOUT_NORMAL_MAPS:
        aoFactor.directAmbientOcclusion = 0.5;
        aoFactor.indirectAmbientOcclusion = 0.5;
        break;

    case DEBUGLIGHTINGMODE_LIGHTING_WITH_NORMAL_MAPS:
        aoFactor.directAmbientOcclusion *= 0.5;
        aoFactor.indirectAmbientOcclusion *= 0.5;
        break;
    }
#endif

    return aoFactor;
}

AmbientOcclusionFactor IllusionCreateAmbientOcclusionFactor(InputData inputData, SurfaceData surfaceData)
{
    AmbientOcclusionFactor aoFactor = IllusionGetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
    aoFactor.indirectAmbientOcclusion = min(aoFactor.indirectAmbientOcclusion, surfaceData.occlusion);
    return aoFactor;
}
#endif