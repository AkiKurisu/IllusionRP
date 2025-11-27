Shader "Hidden/ConvolutionBloom/Blend"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FFT_EXTEND ("FFT EXTEND", Vector) = (0.1, 0.1,0,0)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        // No culling or depth
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            float4 _FFT_EXTEND;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            Texture2D _MainTex;
            
            half4 frag(v2f i) : SV_Target
            {
                float2 fft_extend = _FFT_EXTEND.xy;
                float2 uv = (i.uv + fft_extend) * (1 - 2 * fft_extend);
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv);
                col.a = _Intensity;
                return col;
            }
            ENDHLSL
        }
    }
}