#ifndef PRE_INTEGRATED_FGD_INCLUDED
#define PRE_INTEGRATED_FGD_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PreIntegratedFGD/PreIntegratedFGDDefine.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

TEXTURE2D(_PreIntegratedFGD_GGXDisneyDiffuse);

// HDRP.PreIntegratedFGD
void GetPreIntegratedFGDGGXAndDisneyDiffuse(float NdotV, float perceptualRoughness, float3 fresnel0,
    out float3 specularFGD, out float3 diffuseFGD, out float3 reflectivity)
{
    // We want the LUT to contain the entire [0, 1] range, without losing half a texel at each side.
    float2 coordLUT = Remap01ToHalfTexelCoord(float2(sqrt(NdotV), perceptualRoughness), FGDTEXTURE_RESOLUTION);

    float3 preFGD = SAMPLE_TEXTURE2D_LOD(_PreIntegratedFGD_GGXDisneyDiffuse, sampler_LinearClamp, coordLUT, 0).xyz;

    // Pre-integrate GGX FGD
    // Integral{BSDF * <N,L> dw} =
    // Integral{(F0 + (1 - F0) * (1 - <V,H>)^5) * (BSDF / F) * <N,L> dw} =
    // (1 - F0) * Integral{(1 - <V,H>)^5 * (BSDF / F) * <N,L> dw} + F0 * Integral{(BSDF / F) * <N,L> dw}=
    // (1 - F0) * x + F0 * y = lerp(x, y, F0)
    specularFGD = lerp(preFGD.xxx, preFGD.yyy, fresnel0);

    // Pre integrate DisneyDiffuse FGD:
    // z = DisneyDiffuse
    // Remap from the [0, 1] to the [0.5, 1.5] range.
    diffuseFGD = preFGD.z + 0.5;

    reflectivity = preFGD.yyy;
}

TEXTURE2D(_PreIntegratedFGD_CharlieAndFabric);

void GetPreIntegratedFGDCharlieAndFabricLambert(float NdotV, float perceptualRoughness, float3 fresnel0,
    out float3 specularFGD, out float3 diffuseFGD, out float3 reflectivity)
{
    // Read the texture
    float3 preFGD = SAMPLE_TEXTURE2D_LOD(_PreIntegratedFGD_CharlieAndFabric, sampler_LinearClamp, float2(NdotV, perceptualRoughness), 0).xyz;

    specularFGD = lerp(preFGD.xxx, preFGD.yyy, fresnel0) * 2.0f * PI;

    // z = FabricLambert
    diffuseFGD = preFGD.z;

    reflectivity = preFGD.yyy;
}
#endif