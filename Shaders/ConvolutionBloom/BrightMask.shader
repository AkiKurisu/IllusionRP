Shader "Hidden/ConvolutionBloom/BrightMask"
{
    Properties
    {
        _FFT_EXTEND ("FFT EXTEND", Vector) = (0.1, 0.1,0,0)
        _Threshlod ("Threshlod", float) = 10
        _ThresholdKnee ("ThresholdKnee", float) = 0.1
        _MaxClamp ("MaxClamp", float) = 5
        
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        
        // No culling or depth
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile_local _ _USE_RGBM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            
            float4 _FFT_EXTEND;
            float _Threshlod;
            float _ThresholdKnee;
            float _MaxClamp;

            #define Threshold _Threshlod
            #define ThresholdKnee _ThresholdKnee
            #define ClampMax _MaxClamp
            #define TexelSize _TexelSize

            half4 EncodeHDR(half3 color)
            {
                #if _USE_RGBM
                half4 outColor = EncodeRGBM(color);
                #else
                half4 outColor = half4(color, 1.0);
                #endif

                #if UNITY_COLORSPACE_GAMMA
                return half4(sqrt(outColor.xyz), outColor.w); // linear to γ
                #else
                return outColor;
                #endif
            }

            half3 DecodeHDR(half4 color)
            {
                #if UNITY_COLORSPACE_GAMMA
                color.xyz *= color.xyz; // γ to linear
                #endif

                #if _USE_RGBM
                return DecodeRGBM(color);
                #else
                return color.xyz;
                #endif
            }

            float luminance(float3 col)
            {
                return 0.299 * col.x + 0.587 * col.y + 0.114 * col.z;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 fft_extend = _FFT_EXTEND.xy;
                float2 uv = input.texcoord.xy / (1 - 2 * fft_extend) - fft_extend;
                if (uv.x > 1 || uv.y > 1 || uv.x < 0 || uv.y < 0) return 0;


                float2 texelSize = _ScreenSize.zw * 2;
                half4 A = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-1.0, -1.0));
                half4 B = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.0, -1.0));
                half4 C = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(1.0, -1.0));
                half4 D = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-0.5, -0.5));
                half4 E = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.5, -0.5));
                half4 F = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-1.0, 0.0));
                half4 G = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 H = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(1.0, 0.0));
                half4 I = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-0.5, 0.5));
                half4 J = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.5, 0.5));
                half4 K = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-1.0, 1.0));
                half4 L = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.0, 1.0));
                half4 M = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(1.0, 1.0));

                half2 div = (1.0 / 4.0) * half2(0.5, 0.125);

                half4 o = (D + E + I + J) * div.x;
                o += (A + B + G + F) * div.y;
                o += (B + C + H + G) * div.y;
                o += (F + G + L + K) * div.y;
                o += (G + H + M + L) * div.y;

                half3 color = o.xyz;

                // User controlled clamp to limit crazy high broken spec
                color = min(ClampMax, color);

                // Thresholding
                half brightness = Max3(color.r, color.g, color.b);
                half softness = clamp(brightness - Threshold + ThresholdKnee, 0.0, 2.0 * ThresholdKnee);
                softness = (softness * softness) / (4.0 * ThresholdKnee + 1e-4);
                half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
                color *= multiplier;

                // Clamp colors to positive once in prefilter. Encode can have a sqrt, and sqrt(-x) == NaN. Up/Downsample passes would then spread the NaN.
                color = max(color, 0);
                return EncodeHDR(color);
            }
            ENDHLSL
        }
    }
}