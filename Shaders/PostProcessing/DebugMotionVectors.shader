Shader "Hidden/DebugMotionVectors"
{
    HLSLINCLUDE
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    #pragma vertex Vert
    #pragma fragment Frag

    TEXTURE2D_X(_MotionVectorTexture);
    SAMPLER(sampler_MotionVectorTexture);

    float4 _DebugMotionVectorsParams;
    #define _DebugMotionVectorValid _DebugMotionVectorsParams.x

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetNormalizedFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                if (_DebugMotionVectorValid < 0.5f)
                    return half4(0.8, 0.0, 0.8, 1.0);

                float2 motion = SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_MotionVectorTexture, input.texcoord).xy;

                float mag = length(motion) * 10.0f;
                float angle = atan2(motion.y, motion.x) / 3.14159f;
                float3 color = 0.5f + 0.5f * cos(float3(0, 2.094f, 4.188f) + angle * 6.283f);
                return half4(color * saturate(mag), 1.0);
            }
            ENDHLSL
        }
    }
}
