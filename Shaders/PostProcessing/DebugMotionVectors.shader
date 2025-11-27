Shader "Hidden/DebugMotionVectors"
{
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
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_MotionVectorTexture);
            SAMPLER(sampler_MotionVectorTexture);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 motion = SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_MotionVectorTexture, IN.uv).xy;

                // direction -> color, length -> intensity
                float mag = length(motion) * 10.0;
                float angle = atan2(motion.y, motion.x) / 3.14159;
                float3 color = 0.5 + 0.5 * cos(float3(0, 2.094, 4.188) + angle * 6.283);
                return half4(color * saturate(mag), 1.0);
            }
            ENDHLSL
        }
    }
}
