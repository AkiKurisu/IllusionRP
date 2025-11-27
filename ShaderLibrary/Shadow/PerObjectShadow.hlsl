/*
 * StarRailNPRShader - Fan-made shaders for Unity URP attempting to replicate
 * the shading of Honkai: Star Rail.
 * https://github.com/stalomeow/StarRailNPRShader
 *
 * Copyright (C) 2023 Stalo <stalowork@163.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

#ifndef PER_OBJECT_SHADOW_INCLUDED
#define PER_OBJECT_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#define MAX_PER_OBJECT_SHADOW_COUNT 16

TEXTURE2D_SHADOW(_PerObjSceneShadowMap);
SAMPLER_CMP(sampler_PerObjSceneShadowMap);

int _PerObjSceneShadowCount;
float4x4 _PerObjSceneShadowMatrices[MAX_PER_OBJECT_SHADOW_COUNT];
float4 _PerObjSceneShadowMapRects[MAX_PER_OBJECT_SHADOW_COUNT];
float _PerObjSceneShadowCasterIds[MAX_PER_OBJECT_SHADOW_COUNT];
float4 _PerObjShadowBiases[MAX_PER_OBJECT_SHADOW_COUNT];

float4 _PerObjSceneShadowOffset0;
float4 _PerObjSceneShadowOffset1;
float4 _PerObjSceneShadowMapSize;

float4 TransformWorldToPerObjectShadowCoord(float4x4 shadowMatrix, float3 positionWS)
{
    return mul(shadowMatrix, float4(positionWS, 1));
}

real SampleShadowmapOffset(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowMapRect, float4 shadowCoord, float2 dz, float2 uv)
{
    if (shadowCoord.x < shadowMapRect.x ||
        shadowCoord.x > shadowMapRect.y ||
        shadowCoord.y < shadowMapRect.z ||
        shadowCoord.y > shadowMapRect.w)
    {
        return 1; // Beyond the shadow map range, it is considered as no shadow
    }
    
    float zOffset = dot(clamp(dz, -4, 4), uv - shadowCoord.xy);

    float z = saturate(shadowCoord.z + zOffset);
    return SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(uv, z));
}

real SamplePerObjectShadowmapFilteredHighQuality(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowMapRects, float4 shadowCoord, float2 dz, ShadowSamplingData samplingData)
{
    real fetchesWeights[16];
    real2 fetchesUV[16];
    SampleShadow_ComputeSamples_Tent_7x7(samplingData.shadowmapSize, shadowCoord.xy, fetchesWeights, fetchesUV);

    real result = 0;

    UNITY_UNROLL
    for(uint i = 0; i < 16; i++)
    {
        result += fetchesWeights[i] * SampleShadowmapOffset(
            TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowMapRects, shadowCoord, dz ,fetchesUV[i].xy);
    }
    return result;
}

real SamplePerObjectShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowMapRects, float4 shadowCoord, float2 dz, ShadowSamplingData samplingData, half4 shadowParams, bool isPerspectiveProjection = true)
{
    // Compiler will optimize this branch away as long as isPerspectiveProjection is known at compile time
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    real attenuation;
    real shadowStrength = shadowParams.x;

    attenuation = SamplePerObjectShadowmapFilteredHighQuality(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowMapRects, shadowCoord, dz, samplingData);

    attenuation = LerpWhiteTo(attenuation, shadowStrength);

    // Shadow coords that fall out of the light frustum volume must always return attenuation 1.0
    // TODO: We could use branch here to save some perf on some platforms.
    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

float2 ComputeShadowZOffset(float4 shadowCoord)
{
    float3 dx = ddx(shadowCoord.xyz);
    float3 dy = ddy(shadowCoord.xyz);
    return float2(dx.z * dy.y - dy.z * dx.y, dx.x * dy.z - dy.x * dx.z) * rcp(dx.x * dy.y - dy.x * dx.y);
}

float PerObjectShadow(
    TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap),
    float4 shadowMapRects,
    float4 shadowCoord,
    ShadowSamplingData shadowSamplingData,
    half4 shadowParams,
    bool isPerspectiveProjection)
{
    if (shadowCoord.x < shadowMapRects.x ||
        shadowCoord.x > shadowMapRects.y ||
        shadowCoord.y < shadowMapRects.z ||
        shadowCoord.y > shadowMapRects.w)
    {
        return 1; // Beyond the shadow map range, it is considered as no shadow
    }

    // float2 dz = ComputeShadowZOffset(shadowCoord);
    // return SamplePerObjectShadowmap(TEXTURE2D_SHADOW_ARGS(shadowMap, sampler_shadowMap),
    //         shadowMapRects, shadowCoord, dz, shadowSamplingData, shadowParams, isPerspectiveProjection);

    return SampleShadowmap(TEXTURE2D_SHADOW_ARGS(shadowMap, sampler_shadowMap),
        shadowCoord, shadowSamplingData, shadowParams, isPerspectiveProjection);
}

ShadowSamplingData GetMainLightPerObjectSceneShadowSamplingData()
{
    ShadowSamplingData shadowSamplingData;

    // shadowOffsets are used in SampleShadowmapFiltered for low quality soft shadows.
    shadowSamplingData.shadowOffset0 = _PerObjSceneShadowOffset0;
    shadowSamplingData.shadowOffset1 = _PerObjSceneShadowOffset1;

    // shadowmapSize is used in SampleShadowmapFiltered otherwise
    shadowSamplingData.shadowmapSize = _PerObjSceneShadowMapSize;
    shadowSamplingData.softShadowQuality = _MainLightShadowParams.y;

    return shadowSamplingData;
}

// Ref: ApplyShadowBias
float3 ApplyPerObjectShadowBias(float3 positionWS, float3 normalWS, float3 lightDirection, int index)
{
    float4 shadowBias = _PerObjShadowBiases[index];
    
    float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
    float scale = -invNdotL * shadowBias.y;

    // In fragment shadow bias, bias direction is reversed
    positionWS = -lightDirection * shadowBias.xxx + positionWS;
    positionWS = normalWS * scale.xxx + positionWS;
    return positionWS;
}

float MainLightPerObjectSceneShadow(float3 positionWS, float3 normalWS, half3 lightDir)
{
    ShadowSamplingData shadowSamplingData = GetMainLightPerObjectSceneShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    float shadow = 1;

    for (int i = 0; i < _PerObjSceneShadowCount; i++)
    {
        float3 biasPositionWS = positionWS;
#if APPLY_SHADOW_BIAS_FRAGMENT
        biasPositionWS = ApplyPerObjectShadowBias(positionWS, normalWS, lightDir, i);
#endif
        
        float4 shadowCoord = TransformWorldToPerObjectShadowCoord(_PerObjSceneShadowMatrices[i], biasPositionWS);
        shadow = min(shadow, PerObjectShadow(TEXTURE2D_SHADOW_ARGS(_PerObjSceneShadowMap, sampler_PerObjSceneShadowMap),
            _PerObjSceneShadowMapRects[i], shadowCoord, shadowSamplingData, shadowParams, false));
    }

    return shadow;
}

#endif
