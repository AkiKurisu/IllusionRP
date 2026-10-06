#ifndef ILLUSION_WET_SURFACE_INCLUDED
#define ILLUSION_WET_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/WetSurfaceResponse.hlsl"

TEXTURE2D_X_FLOAT(_WetSurfaceMask);
TEXTURE2D_X_FLOAT(_WetSurfaceNormals);
TEXTURE2D_X_FLOAT(_WetSurfaceSourceBuffer);
float _WetSurfaceActive;

struct WetSurfaceLightingState
{
    BRDFData originalBRDF;
    half3 originalNormal;
    half ambientFactor;
    half darkness;
    half specularWeight;
    bool hasWet;
};

WetSurfaceLightingState ApplyWetSurface(float2 normalizedScreenUV,
    inout SurfaceData surfaceData, inout InputData inputData, inout BRDFData brdfData,
    float3 materialSpecular)
{
    WetSurfaceLightingState state;
    state.originalBRDF = brdfData;
    state.originalNormal = inputData.normalWS;
    state.ambientFactor = 1;
    state.darkness = 0;
    state.specularWeight = 0;
    state.hasWet = false;
    // Keep a single return to avoid FXC uninitialized-value warnings on the inout paths.
    #if !defined(_SURFACE_TYPE_TRANSPARENT)
    if (_WetSurfaceActive >= 0.5)
    {
        uint2 pixel = uint2(normalizedScreenUV * _ScaledScreenParams.xy);
        float wetness = LOAD_TEXTURE2D_X(_WetSurfaceMask, pixel).r;
        if (wetness >= 0.001)
        {
            float4 source = LOAD_TEXTURE2D_X(_WetSurfaceSourceBuffer, pixel);
            if (IsWetSurfaceForwardDataValid(source))
            {
                state.hasWet = true;

                float3 sourceSpecular = WetSurfaceQuantize8(materialSpecular);
                WetSurfaceResponse response = EvaluateWetSurfaceResponse(sourceSpecular, source.g, wetness);
                float3 wetSpecular = response.specular;
                float wetSmoothness = response.smoothness;

                state.ambientFactor = 1 - 0.35 * response.darkness;
                state.darkness = response.darkness;
                state.specularWeight = response.specularWeight;
                surfaceData.emission *= state.ambientFactor;
                surfaceData.smoothness = wetSmoothness;
                surfaceData.specular = wetSpecular;
                brdfData.diffuse *= 1 - response.darkness;
                brdfData.specular = wetSpecular;
                brdfData.reflectivity = max(max(wetSpecular.r, wetSpecular.g), wetSpecular.b);
                brdfData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(wetSmoothness);
                brdfData.roughness = max(PerceptualRoughnessToRoughness(brdfData.perceptualRoughness), HALF_MIN_SQRT);
                brdfData.roughness2 = max(brdfData.roughness * brdfData.roughness, HALF_MIN);
                brdfData.grazingTerm = saturate(wetSmoothness + brdfData.reflectivity);
                brdfData.normalizationTerm = brdfData.roughness * 4 + 2;
                brdfData.roughness2MinusOne = brdfData.roughness2 - 1;
                inputData.normalWS = normalize(LOAD_TEXTURE2D_X(_WetSurfaceNormals, pixel).xyz);
            }
        }
    }
    #endif
    return state;
}

WetSurfaceLightingState ApplyWetSurface(float2 normalizedScreenUV,
    inout SurfaceData surfaceData, inout InputData inputData, inout BRDFData brdfData)
{
    return ApplyWetSurface(normalizedScreenUV, surfaceData, inputData, brdfData, brdfData.specular);
}

#endif
