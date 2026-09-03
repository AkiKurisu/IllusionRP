Shader "Hidden/ConvolutionBloom/PsfRemap"
{
    Properties
    {
        _MaxClamp ("KernelMaxClamp", float) = 5
        _MinClamp ("KernelMinClamp", float) = 1
        _FFT_EXTEND ("FFT EXTEND", Vector) = (0.1, 0.1,0,0)
        _Power ("Power", float) = 1
        _Scaler ("Scaler", float) = 1
        _EnableRemap ("EnableRemap", int) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _MaxClamp;
            float _MinClamp;
            float4 _FFT_EXTEND;
            float _Power;
            float _Scaler;
            int _EnableRemap;

            float luminance(float3 col)
            {
                return dot(col, float3(0.299, 0.587, 0.114));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.texcoord.xy;
                uv.x = fmod(uv.x + 0.5, 1.0);
                uv.y = fmod(uv.y + 0.5, 1.0);
                float2 fft_extend = _FFT_EXTEND.xy;
                float2 img_map_size = 1 - 2 * fft_extend;
                float2 kenrel_map_size = sqrt(_ScreenSize.x * _ScreenSize.y) * img_map_size * _ScreenSize.zw;
                uv = (uv - (1 - kenrel_map_size) * 0.5) / kenrel_map_size;
                
                half4 col;
                if(uv.x > 1 || uv.y > 1 || uv.x < 0 || uv.y < 0)
                {
                    col = 0;
                }
                else
                {
                    col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
                }

                float scale = _Scaler;
                col = col * scale;
                return clamp(col, _MinClamp, _MaxClamp);
            }
            ENDHLSL
        }
    }
}
