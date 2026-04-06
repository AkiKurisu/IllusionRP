#ifndef HYBRID_LIT_GBUFFER_PASS_INCLUDED
#define HYBRID_LIT_GBUFFER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
// Smoothness sampling always needs UV; depth-normals only conditionally adds UV to Varyings.
#define REQUIRES_UV_INTERPOLATOR
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"

// MRT: SV_Target0 = forward smoothness buffer, SV_Target1 = _CameraNormalsTexture (same packing as DepthNormals pass)

void LitForwardGBufferMRTFragment(
    Varyings input,
    out half4 outSmoothness : SV_Target0,
    out half4 outNormalWS : SV_Target1)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData = (SurfaceData)0;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    BRDFData brdfData = (BRDFData)0;
    InitializeBRDFData(surfaceData, brdfData);
    half s = surfaceData.smoothness;
    outSmoothness = half4(s, s, s, s);

    // Normals: same as DepthNormalsFragment in LitDepthNormalsPass.hlsl (without _WRITE_RENDERING_LAYERS)
#if defined(_ALPHATEST_ON)
    Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
#endif

#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

#if defined(_GBUFFER_NORMALS_OCT)
    float3 normalWS = normalize(input.normalWS);
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
    outNormalWS = half4(packedNormalWS, 0.0);
#else
#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, input.viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

#if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    float3 normalTS = SampleNormal(input.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);

#if defined(_DETAIL)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, input.uv).a;
    float2 detailUv = input.uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    normalTS = ApplyDetailNormal(detailUv, normalTS, detailMask);
#endif

    float3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
#else
    float3 normalWS = input.normalWS;
#endif

    outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
#endif
}

#endif
