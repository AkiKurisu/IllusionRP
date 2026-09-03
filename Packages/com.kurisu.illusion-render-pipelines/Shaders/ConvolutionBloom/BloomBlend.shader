Shader "Hidden/ConvolutionBloom/Blend"
{
    Properties
    {
        _FFT_EXTEND ("FFT EXTEND", Vector) = (0.1, 0.1, 0, 0)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
         Tags { "RenderPipeline" = "UniversalPipeline" }
         
        // No culling or depth
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _FFT_EXTEND;
            float _Intensity;

            half4 frag(Varyings i) : SV_Target
            {
                float2 fft_extend = _FFT_EXTEND.xy;
                float2 uv = (i.texcoord.xy + fft_extend) * (1 - 2 * fft_extend);
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                col.a = _Intensity;
                return col;
            }
            ENDHLSL
        }
    }
}