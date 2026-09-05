#ifndef HD_SHADOW_ALGORITHMS_INCLUDED
#define HD_SHADOW_ALGORITHMS_INCLUDED
// Configure which shadow algorithms to use per shadow level quality

// Since we use slope-scale bias, the constant bias is for now set as a small fixed value
#define FIXED_UNIFORM_BIAS (1.0f / 65536.0f)

// For non-fragment shaders we might skip the variant with the quality as shadows might not be used, if that's the case define something just to make the compiler happy in case the quality is not defined.
#ifndef SHADER_STAGE_FRAGMENT
    #if !defined(AREA_SHADOW_LOW) && !defined(AREA_SHADOW_MEDIUM) && !defined(AREA_SHADOW_HIGH) // low come from volumetricLighting.compute
        #define AREA_SHADOW_MEDIUM
    #endif
#endif

// WARNINGS: Keep in sync with both HDShadowManager::GetDirectionalShadowAlgorithm() and GetPunctualFilterWidthInTexels() in C#
// Note: currently quality settings for PCSS needs to be exposed in UI and is control in HDLightUI.cs file IsShadowSettings

#ifdef AREA_SHADOW_LOW
#define AREA_FILTER_ALGORITHM(sd, posSS, posTC, tex, samp, bias) SampleShadow_EVSM_1tap(posTC, sd.shadowFilterParams0.y, sd.shadowFilterParams0.z, sd.shadowFilterParams0.xx, false, tex, s_linear_clamp_sampler)
#define AREA_LIGHT_SHADOW_IS_EVSM 1
#elif defined(AREA_SHADOW_MEDIUM)
#define AREA_FILTER_ALGORITHM(sd, posSS, posTC, tex, samp, bias) SampleShadow_EVSM_1tap(posTC, sd.shadowFilterParams0.y, sd.shadowFilterParams0.z, sd.shadowFilterParams0.xx, false, tex, s_linear_clamp_sampler)
#define AREA_LIGHT_SHADOW_IS_EVSM 1
// Note: currently quality settings for PCSS need to be expose in UI and is control in HDLightUI.cs file IsShadowSettings
#elif defined(AREA_SHADOW_HIGH)
#define AREA_FILTER_ALGORITHM(sd, posSS, posTC, tex, samp, bias) SampleShadow_PCSS_Area(posTC, positionSS, sd.shadowMapSize.xy * texelSize, sd.atlasOffset, sd.shadowFilterParams0.x, sd.shadowFilterParams0.w, asint(sd.shadowFilterParams0.y), asint(sd.shadowFilterParams0.z), tex, s_linear_clamp_compare_sampler, s_point_clamp_sampler, FIXED_UNIFORM_BIAS, sd.zBufferParam, true, (sd.isInCachedAtlas ? _CachedAreaShadowAtlasSize.xz : _AreaShadowAtlasSize.xz))
#endif

// @IllusionRP: the off variant of the AREA_SHADOW_* keyword pair disables area lights, keep the compiler happy there.
#ifndef AREA_FILTER_ALGORITHM
#define AREA_FILTER_ALGORITHM(sd, posSS, posTC, tex, samp, bias) 1.0f
#endif

float4 EvalShadow_WorldToShadow(HDShadowData sd, float3 positionWS, bool perspProj)
{
    // Note: Due to high VGRP load we can't use the whole view projection matrix, instead we reconstruct it from
    // rotation, position and projection vectors (projection and position are stored in SGPR)
#if 0
    return mul(viewProjection, float4(positionWS, 1));
#else

    if(perspProj)
    {
        positionWS = positionWS - sd.pos;
        float3x3 view = { sd.rot0, sd.rot1, sd.rot2 };
        positionWS = mul(view, positionWS);
    }
    else
    {
        float3x4 view;
        view[0] = float4(sd.rot0, sd.pos.x);
        view[1] = float4(sd.rot1, sd.pos.y);
        view[2] = float4(sd.rot2, sd.pos.z);
        positionWS = mul(view, float4(positionWS, 1.0)).xyz;
    }

    float4x4 proj;
    proj = 0.0;
    proj._m00 = sd.proj[0];
    proj._m11 = sd.proj[1];
    proj._m22 = sd.proj[2];
    proj._m23 = sd.proj[3];
    if(perspProj)
        proj._m32 = -1.0;
    else
        proj._m33 = 1.0;

    return mul(proj, float4(positionWS, 1.0));
#endif
}

// function called by spot, point, area and directional eval routines to calculate shadow coordinates
float3 EvalShadow_GetTexcoordsAtlas(HDShadowData sd, float2 atlasSizeRcp, float3 positionWS, out float3 posNDC, bool perspProj)
{
    float4 posCS = EvalShadow_WorldToShadow(sd, positionWS, perspProj);
    // Avoid (0 / 0 = NaN).
    posNDC = (perspProj && posCS.w != 0) ? (posCS.xyz / posCS.w) : posCS.xyz;

    // calc TCs
    float3 posTC = float3(saturate(posNDC.xy * 0.5 + 0.5), posNDC.z);
    posTC.xy = posTC.xy * sd.shadowMapSize.xy * atlasSizeRcp + sd.atlasOffset;

    return posTC;
}

float3 EvalShadow_GetTexcoordsAtlas(HDShadowData sd, float2 atlasSizeRcp, float3 positionWS, bool perspProj)
{
    float3 ndc;
    return EvalShadow_GetTexcoordsAtlas(sd, atlasSizeRcp, positionWS, ndc, perspProj);
}

float3 EvalShadow_NormalBiasOrtho(float worldTexelSize, float normalBiasFactor, float3 normalWS)
{
    return normalWS * normalBiasFactor * worldTexelSize;
}

float3 EvalShadow_NormalBiasProj(float worldTexelSize, float normalBiasFactor, float3 normalWS, float L_dist)
{
    return EvalShadow_NormalBiasOrtho(worldTexelSize, normalBiasFactor, normalWS) * L_dist;
}

void EvalShadow_MinMaxCoords(HDShadowData sd, float2 texelSize, out float2 minCoord, out float2 maxCoord, int blurPassesScale = 1)
{
    maxCoord = (sd.shadowMapSize.xy - 0.5f * blurPassesScale) * texelSize + sd.atlasOffset;
    minCoord = sd.atlasOffset + texelSize * blurPassesScale;
}

bool EvalShadow_PositionTC(HDShadowData sd, float2 texelSize, float3 positionWS, float3 normalBias, bool perspective, out float3 posTC, int blurPassesScale = 1)
{
    positionWS += sd.cacheTranslationDelta.xyz;
    positionWS += normalBias;

    posTC = EvalShadow_GetTexcoordsAtlas(sd, texelSize, positionWS, perspective);

    float2 minCoord, maxCoord;
    EvalShadow_MinMaxCoords(sd, texelSize, minCoord, maxCoord, blurPassesScale);
    return !(any(posTC.xy > maxCoord) || any(posTC.xy < minCoord));
}

//
//  Area light shadows
//

float EvalShadow_AreaDepth(HDShadowData sd, Texture2D tex, float2 positionSS, float3 positionWS, float3 normalWS, float3 L, float L_dist, bool perspective)
{
#ifdef AREA_LIGHT_SHADOW_IS_EVSM
    // Needed as blurring might cause some leaks. It might be overclipping, but empirically is a good value.
    int blurPassesScale = (1 + min(4, sd.shadowFilterParams0.w) * 4.0f);
    float3 normalBias = 0;
#else
    int blurPassesScale = 1;
    float3 normalBias = EvalShadow_NormalBiasProj(sd.worldTexelSize, sd.normalBias, normalWS, L_dist);
#endif

    float2 texelSize = sd.isInCachedAtlas ? _CachedAreaShadowAtlasSize.zw : _AreaShadowAtlasSize.zw;
    float3 posTC;
    return EvalShadow_PositionTC(sd, texelSize, positionWS, normalBias, perspective, posTC, blurPassesScale) ? AREA_FILTER_ALGORITHM(sd, positionSS, posTC, tex, s_linear_clamp_compare_sampler, FIXED_UNIFORM_BIAS) : 1.0f;
}

#endif // HD_SHADOW_ALGORITHMS_INCLUDED
