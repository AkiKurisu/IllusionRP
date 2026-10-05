#ifndef HYBRID_LIT_GBUFFER_PASS_INCLUDED
#define HYBRID_LIT_GBUFFER_PASS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/WetSurfaceResponse.hlsl"
float _WetSurfacePackedEnabled;

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif
#if defined(_DETAIL_MULX2) || defined(_DETAIL_SCALED)
#define _DETAIL
#endif
#if defined(_PRT_CAPTURE)
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/PRTCapture.hlsl"
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float3 normal : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
#if defined(_NORMALMAP) || defined(_DETAIL) || defined(_PARALLAXMAP)
    half4 tangentWS : TEXCOORD4;
#endif
#if defined(_PARALLAXMAP)
    half3 viewDirTS : TEXCOORD8;
#endif
#if defined(_PRT_CAPTURE)
    float3 positionWS : TEXCOORD9;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings LitForwardGBufferVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normal = GetVertexNormalInputs(input.normal, input.tangentOS);
    output.positionCS = position.positionCS;
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.normalWS = normal.normalWS;
#if defined(_NORMALMAP) || defined(_DETAIL) || defined(_PARALLAXMAP)
    output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
#endif
#if defined(_PARALLAXMAP)
    output.viewDirTS = GetViewDirectionTangentSpace(output.tangentWS, output.normalWS,
        GetWorldSpaceNormalizeViewDir(position.positionWS));
#endif
#if defined(_PRT_CAPTURE)
    output.positionWS = position.positionWS;
#endif
    return output;
}

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
#if defined(_PRT_CAPTURE)
    FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC,
    out float4 outCapture : SV_Target0)
#else
    out half4 outSmoothness : SV_Target0,
    out half4 outNormalWS : SV_Target1)
#endif
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
    half3 viewDirTS = input.viewDirTS;
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

#if defined(_PRT_CAPTURE)
    SurfaceData surface;
    InitializeStandardLitSurfaceData(input.uv, surface);
    float3 normalWS = normalize(input.normalWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float3 bitangent = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
    normalWS = TransformTangentToWorld(surface.normalTS,
        float3x3(input.tangentWS.xyz, bitangent, input.normalWS));
#endif
    normalWS *= IS_FRONT_VFACE(facing, 1.0, -1.0);
    outCapture = PRTCaptureOutput(input.positionWS, normalWS,
        PRTDiffuseReflectance(surface.albedo, surface.metallic, surface.specular));
#else
    half3 normalTS;
    half smoothness;

    if (_WetSurfacePackedEnabled > 0.5)
    {
        SurfaceData wetSource;
        InitializeStandardLitSurfaceData(input.uv, wetSource);
        normalTS = wetSource.normalTS;
        smoothness = wetSource.smoothness;
        #ifdef _SPECULAR_SETUP
            half3 sourceSpecular = wetSource.specular;
        #else
            half3 sourceSpecular = lerp(half3(0.04h, 0.04h, 0.04h),
                wetSource.albedo, wetSource.metallic);
        #endif
        outSmoothness = PackWetSurfaceForwardData(smoothness, wetSource.smoothness, sourceSpecular);
    }
    else
    {
        half alpha;
        InitializeLitForwardGBufferData(input.uv, alpha, normalTS, smoothness);
        outSmoothness = half4(smoothness, smoothness, smoothness, smoothness);
    }

#if defined(_NORMALMAP) || defined(_DETAIL)
    half sgn = input.tangentWS.w;
    half3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    float3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
#else
    float3 normalWS = input.normalWS;
#endif

    outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
#endif
}

#endif
