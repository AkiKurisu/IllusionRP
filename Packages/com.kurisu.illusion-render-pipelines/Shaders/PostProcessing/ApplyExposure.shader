Shader "Hidden/ApplyExposure"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
        
        half4 Fragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 fragColor = FragBlit(input, sampler_LinearClamp);
            fragColor.rgb *= GetCurrentExposureMultiplier();
            return fragColor;
        }
        ENDHLSL

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment Fragment
            ENDHLSL
        }
    }
}