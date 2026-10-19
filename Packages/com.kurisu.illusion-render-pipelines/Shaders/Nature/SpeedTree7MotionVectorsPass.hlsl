#ifndef ILLUSION_SPEEDTREE7_MOTION_VECTORS_PASS_INCLUDED
#define ILLUSION_SPEEDTREE7_MOTION_VECTORS_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

struct SpeedTreeMotionVectorOutput
{
    float4 positionCS : SV_POSITION;
    float4 positionCSNoJitter : POSITION_CS_NO_JITTER;
    float4 previousPositionCSNoJitter : PREV_POSITION_CS_NO_JITTER;
    half2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

SpeedTreeMotionVectorOutput SpeedTree7MotionVectorVert(SpeedTreeVertexInput input)
{
    SpeedTreeMotionVectorOutput output = (SpeedTreeMotionVectorOutput)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#ifdef UNIVERSAL_SPEEDTREE7BILLBOARD_PASSES_INCLUDED
    half hueVariation;
    InitializeData(input, output.uv, hueVariation);
#else
    InitializeData(input, unity_LODFade.x);
    output.uv = input.texcoord.xy;
#endif
    output.positionCS = TransformObjectToHClip(input.vertex.xyz);
    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.vertex));
    // Previous deformation is unavailable; reuse current geometry for rigid motion.
    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, input.vertex));
    return output;
}

float4 SpeedTree7MotionVectorFrag(SpeedTreeMotionVectorOutput input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#ifdef SPEEDTREE_ALPHATEST
    AlphaDiscard(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_MainTex, sampler_MainTex)).a * _Color.a, _Cutoff);
#endif
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    return float4(CalcNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 0, 0);
}

#endif
