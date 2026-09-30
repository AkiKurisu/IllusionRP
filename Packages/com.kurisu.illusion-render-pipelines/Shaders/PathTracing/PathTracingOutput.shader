Shader "Hidden/PathTracingOutput"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"

    TEXTURE2D(_PathTracingDepth);

    float LoadPathTracingDepth(float2 uv)
    {
        return SAMPLE_TEXTURE2D_LOD(_PathTracingDepth, sampler_PointClamp, uv, 0).r;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite On
        Cull Off

        Pass
        {
            Name "PathTracingOutput"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 radiance = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
                depth = LoadPathTracingDepth(input.texcoord);
                return half4(radiance * GetCurrentExposureMultiplier(), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PathTracingDepthTexture"
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float Frag(Varyings input) : SV_Depth
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return LoadPathTracingDepth(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PathTracingDepthTextureColor"
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return LoadPathTracingDepth(input.texcoord);
            }
            ENDHLSL
        }
    }
}
