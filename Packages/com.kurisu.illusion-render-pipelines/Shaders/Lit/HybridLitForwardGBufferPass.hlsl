#ifndef HYBRID_LIT_GBUFFER_PASS_INCLUDED
#define HYBRID_LIT_GBUFFER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
// Smoothness sampling always needs UV; depth-normals only conditionally adds UV to Varyings.
#define REQUIRES_UV_INTERPOLATOR
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"

// MRT: SV_Target0 = forward smoothness buffer, SV_Target1 = _CameraNormalsTexture (same packing as DepthNormals pass)

// Samples only what alpha clip, normal and smoothness need; the full surface fetch is not required here.
void InitializeLitForwardGBufferData(float2 uv, out half alpha, out half3 normalTS, out half smoothness)
{
#if defined(_ALPHATEST_ON) || defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    half albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a;
#else
    half albedoAlpha = half(1.0);
#endif
    alpha = Alpha(albedoAlpha, _BaseColor, _Cutoff);
    smoothness = SampleMetallicSpecGloss(uv, albedoAlpha).a;

#if defined(_NORMALMAP) || defined(_DETAIL)
    normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    #if defined(_DETAIL)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv).a;
    float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    normalTS = ApplyDetailNormal(detailUv, normalTS, detailMask);
    #endif
#else
    normalTS = half3(0.0, 0.0, 1.0);
#endif
}

void LitForwardGBufferMRTFragment(
    Varyings input,
    out half4 outSmoothness : SV_Target0,
    out half4 outNormalWS : SV_Target1)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, input.viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    half alpha;
    half3 normalTS;
    half smoothness;
    InitializeLitForwardGBufferData(input.uv, alpha, normalTS, smoothness);

#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

    outSmoothness = half4(smoothness, smoothness, smoothness, smoothness);

#if defined(_NORMALMAP) || defined(_DETAIL)
    half sgn = input.tangentWS.w;
    half3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    float3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
#else
    float3 normalWS = input.normalWS;
#endif

    outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
}

#endif
