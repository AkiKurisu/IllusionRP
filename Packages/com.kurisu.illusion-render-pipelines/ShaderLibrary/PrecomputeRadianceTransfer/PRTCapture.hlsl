#ifndef ILLUSION_PRT_CAPTURE_INCLUDED
#define ILLUSION_PRT_CAPTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"

float4 _PRTMetadata;
int _PRTCaptureMode;

float3 PRTDiffuseReflectance(float3 albedo, float metallic, float3 specular)
{
#if defined(_SPECULAR_SETUP)
    float reflectivity = max(specular.r, max(specular.g, specular.b));
    return albedo * saturate(1.0 - reflectivity);
#else
    return albedo * OneMinusReflectivityMetallic(saturate(metallic));
#endif
}

float4 PRTCaptureOutput(float3 positionWS, float3 normalWS, float3 diffuse)
{
    if (_PRTCaptureMode == 0) return float4(positionWS, 1.0);
    if (_PRTCaptureMode == 1) return float4(normalize(normalWS), 1.0);
    if (_PRTCaptureMode == 3) return _PRTMetadata;
    return float4(diffuse, 1.0);
}

float3 PRTCaptureNormalWS(float3 normal, float3 vertexNormalWS, float3 tangentWS,
    float3 bitangentWS, float faceSign)
{
    float3 normalWS = vertexNormalWS;
#if defined(_NORMALMAP)
    #if _NORMAL_DROPOFF_TS
        normalWS = TransformTangentToWorld(normal, float3x3(tangentWS, bitangentWS, vertexNormalWS));
    #elif _NORMAL_DROPOFF_OS
        normalWS = TransformObjectToWorldNormal(normal);
    #elif _NORMAL_DROPOFF_WS
        normalWS = normal;
    #endif
#endif
    if (dot(normalWS, vertexNormalWS) * faceSign < 0) normalWS = -normalWS;
    return normalize(normalWS);
}

#endif
