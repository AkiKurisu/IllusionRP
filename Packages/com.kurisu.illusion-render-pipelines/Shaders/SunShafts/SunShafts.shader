Shader "Hidden/SunShafts"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"

        #define SAMPLES_FLOAT 6.0f
        #define SAMPLES_INT 6

        float4 _SunShaftsSunPosition;
        float4 _SunShaftsBlurRadius4;
        float4 _SunShaftsMaskTexelSize;
        half4 _SunShaftsThreshold;
        half4 _SunShaftsColor;

        half TransformColor(half4 skyboxValue)
        {
            half3 threshold = _SunShaftsThreshold.rgb * GetCurrentExposureMultiplier();
            return dot(max(skyboxValue.rgb - threshold, half3(0.0, 0.0, 0.0)), half3(1.0, 1.0, 1.0));
        }

        half4 FragDepthMask(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;

            // Without a cleared border the radial blur drags edge texels toward the sun.
            float2 border = _SunShaftsMaskTexelSize.xy;
            if (any(uv < border) || any(uv > 1.0 - border))
                return half4(0.0, 0.0, 0.0, 0.0);

            half4 tex = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float depth = Linear01Depth(SampleSceneDepth(uv), _ZBufferParams);

            half2 vec = _SunShaftsSunPosition.xy - uv;
            half dist = saturate(_SunShaftsSunPosition.w - length(vec));

            half4 outColor = half4(0.0, 0.0, 0.0, 0.0);
            if (depth > 0.99)
                outColor = TransformColor(tex) * dist;

            return outColor;
        }

        half4 FragRadialBlur(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float2 blurVector = (_SunShaftsSunPosition.xy - uv) * _SunShaftsBlurRadius4.xy;

            half4 color = half4(0.0, 0.0, 0.0, 0.0);
            for (int i = 0; i < SAMPLES_INT; i++)
            {
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                uv += blurVector;
            }

            return half4(color.xyz / SAMPLES_FLOAT, color.w);
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            half4 shafts = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            return saturate(shafts * _SunShaftsColor);
        }

        ENDHLSL

        Pass
        {
            Name "SunShaftsDepthMask"

            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragDepthMask
            ENDHLSL
        }

        Pass
        {
            Name "SunShaftsRadialBlur"

            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragRadialBlur
            ENDHLSL
        }

        Pass
        {
            Name "SunShaftsScreen"

            Blend One OneMinusSrcColor

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }

        Pass
        {
            Name "SunShaftsAdd"

            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }

    Fallback Off
}
