#ifndef PRT_WORLD_LIGHTING_INCLUDED
#define PRT_WORLD_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

struct PRTWorldLight
{
    float4 positionType;
    float4 directionRange;
    float4 colorShadowStrength;
    float4 attenuation;
    uint renderingLayers;
    uint objectLayers;
    uint cacheOffset;
    uint visibilityEpoch;
    uint faceOffset;
    uint faceCount;
    uint lightId;
    uint flags;
};

struct PRTWorldShadowFace
{
    float4x4 worldToShadow;
    float4 atlasRect;
    float4 sphere;
    float4 receiverBias;
    uint source;
    uint slice;
    uint face;
    uint padding;
};

struct PRTWorldShadowEntry
{
    float shadow;
    uint epoch;
    uint lastUpdateFrame;
    uint valid;
};

StructuredBuffer<PRTWorldLight> _PRTWorldLights;
StructuredBuffer<PRTWorldShadowFace> _PRTWorldShadowFaces;
RWStructuredBuffer<PRTWorldShadowEntry> _PRTWorldShadowCache;
RWStructuredBuffer<uint> _PRTWorldLightingStats;
Texture2D<float> _PRTWorldMainShadows;
Texture2D<float> _PRTWorldAdditionalShadows;
uint _PRTWorldLightCount;
uint _PRTWorldSurfelCount;
uint _PRTWorldSceneFrame;
uint _PRTWorldStatsEnabled;
uint _PRTWorldMaxShadowAge;

void PRTWorldStat(uint index)
{
    if (_PRTWorldStatsEnabled != 0)
        InterlockedAdd(_PRTWorldLightingStats[index], 1);
}

bool PRTWorldSampleFace(PRTWorldShadowFace face, float3 positionWS, float3 normalWS, float3 direction, out float visibility)
{
    visibility = 0;
    if (face.sphere.w > 0 && dot(positionWS - face.sphere.xyz, positionWS - face.sphere.xyz) >= face.sphere.w)
        return false;
    if (face.receiverBias.z != 0)
    {
        float scale = -(1 - saturate(dot(direction, normalWS))) * face.receiverBias.y;
        positionWS += -direction * face.receiverBias.x + normalWS * scale;
    }
    float4 projected = mul(face.worldToShadow, float4(positionWS, 1));
    if (!all(isfinite(projected)) || projected.w <= 0) return false;
    float3 coord = projected.xyz / projected.w;
    if (coord.z <= 0 || coord.z >= 1 || any(coord.xy < face.atlasRect.xy) || any(coord.xy > face.atlasRect.zw))
        return false;
    if (face.source == 1)
        visibility = _PRTWorldMainShadows.SampleCmpLevelZero(sampler_LinearClampCompare, coord.xy, coord.z);
    else
        visibility = _PRTWorldAdditionalShadows.SampleCmpLevelZero(sampler_LinearClampCompare, coord.xy, coord.z);
    return isfinite(visibility);
}

float PRTWorldVisibility(PRTWorldLight light, uint surfelIndex, float3 positionWS, float3 normalWS, float3 direction)
{
    PRTWorldStat(0);
    uint index = light.cacheOffset + surfelIndex;
    PRTWorldShadowEntry entry = _PRTWorldShadowCache[index];
    bool epochValid = entry.valid != 0 && entry.epoch == light.visibilityEpoch;
    uint age = _PRTWorldSceneFrame - entry.lastUpdateFrame;
    for (uint i = 0; i < light.faceCount; i++)
    {
        PRTWorldShadowFace face = _PRTWorldShadowFaces[light.faceOffset + i];
        float visibility;
        if (!PRTWorldSampleFace(face, positionWS, normalWS, direction, visibility)) continue;
        entry.shadow = saturate(visibility);
        entry.epoch = light.visibilityEpoch;
        entry.lastUpdateFrame = _PRTWorldSceneFrame;
        entry.valid = 1;
        _PRTWorldShadowCache[index] = entry;
        PRTWorldStat(5);
        return entry.shadow;
    }
    PRTWorldStat(2);
    if (epochValid)
    {
        PRTWorldStat(1);
        PRTWorldStat(6);
        if (age > _PRTWorldMaxShadowAge) PRTWorldStat(4);
        return entry.shadow;
    }
    PRTWorldStat(3);
    PRTWorldStat(7);
    return 1;
}

float3 EvaluatePRTWorldLighting(Surfel surfel, uint surfelIndex)
{
    float3 lighting = 0;
    for (uint i = 0; i < _PRTWorldLightCount; i++)
    {
        PRTWorldLight light = _PRTWorldLights[i];
        if ((surfel.objectLayerMask & light.objectLayers) == 0) continue;
        if ((light.flags & 4u) != 0 && (surfel.renderingLayerMask & light.renderingLayers) == 0) continue;
        bool directional = light.positionType.w == 0;
        float3 lightVector = directional ? light.directionRange.xyz : light.positionType.xyz - surfel.position;
        float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN_SQRT);
        float3 direction = lightVector * rsqrt(distanceSqr);
        float attenuation = directional ? 1 : DistanceAttenuation(distanceSqr, light.attenuation.xy);
        if (light.positionType.w == 2) attenuation *= AngleAttenuation(light.directionRange.xyz, direction, light.attenuation.zw);
        float cosine = saturate(dot(surfel.normal, direction));
        if (cosine <= 0 || attenuation <= 0) continue;
        float visibility = 1;
        if ((light.flags & 1u) != 0)
        {
            visibility = PRTWorldVisibility(light, surfelIndex, surfel.position, surfel.normal, direction);
            visibility = lerp(1, visibility, saturate(light.colorShadowStrength.w));
        }
        lighting += light.colorShadowStrength.rgb * (cosine * attenuation * visibility);
    }
    return lighting;
}

#endif
