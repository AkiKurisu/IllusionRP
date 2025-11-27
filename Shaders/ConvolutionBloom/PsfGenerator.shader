Shader "Hidden/ConvolutionBloom/PsfGenerator"
{
    Properties
    {
        _FFT_EXTEND ("FFT EXTEND", Vector) = (0.1, 0.1, 0, 0)
        _ScreenX ("Screen X", Int) = 0
        _ScreenY ("Screen Y", Int) = 0
        _EnableRemap ("EnableRemap", int) = 0
    }
    SubShader
    {
        // No culling or depth
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

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
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 _FFT_EXTEND;
            int _ScreenX;
            int _ScreenY;
            int _EnableRemap;


            static const float PI = 3.14159265358979323846f;

            float gaussian(float2 xy, float sigma)
            {
                float r2 = dot(xy, xy); // 距离平方
                return exp(-r2 / (2 * sigma * sigma));
            }


            float streak(float2 xy, float angle, float sharpness)
            {
                // 将offset旋转到条纹方向
                float2 dir;
                sincos(angle, dir.y, dir.x);
                float projected = dot(xy, dir); // 投影到条纹方向
                return pow(saturate(1 - abs(projected) * sharpness), 10); // 尖锐条纹
            }

            // 多方向条纹（如光圈叶片导致的星芒）
            float streaks(float2 xy, float theta, int bladeCount, float sharpness)
            {
                float sum = 0;
                for (int i = 0; i < bladeCount; i++)
                {
                    float angle = PI * i / (float)bladeCount + theta;
                    sum += streak(xy, angle, sharpness);
                }
                return sum;
            }

            float lorentzian(float2 xy, float radius, float gamma)
            {
                float r = length(xy);
                return 1 / (1 + pow(r / radius, gamma));
            }

            float power_law_decay(float2 xy, float F, float alpha, float gamma = 0.01f)
            {
                float r = length(xy);
                float res = F * pow(r + gamma, -alpha);
                return res;
            }

            float power_law_decay_anamorphic(float2 xy, float F, float alpha, float beta)
            {
                xy = abs(xy);
                float res = F * pow(xy.x + 0.0001f, -alpha) * pow(xy.y + 0.0001f, -beta);
                return res;
            }

            float4 PSF1(float2 xy)
            {
                xy *= 4;
                float o = power_law_decay(xy, 0.2, 2.0, 0.01f);
                o = min(o, 100);
                return float4(o.xxx, 1);
            }

            float angle_projection(float2 xy,
                                    int blade_count,
                                    float rotate,
                                    float c1,
                                    float gamma,
                                    float c2 = 0.02,
                                    float c3 = 0.75)
            {
                float sum = 0;
                for (int i = 0; i < blade_count; i++)
                {
                    float angle = PI * i / (float)blade_count + rotate;
                    float2 dir;
                    sincos(angle, dir.y, dir.x);
                    float r = length(xy);
                    float t = abs(dot(xy, dir)) / r;
                    sum += pow(max(abs(t) - c2, 0.001) * c1, gamma) - c3;
                }
                return sum;
            }

            float4 PSF2(float2 xy)
            {
                xy *= 10;
                int blade_count = 3;
                float prj = angle_projection(xy, blade_count, 0, 2, 0.5);
                xy *= prj;
                float o = power_law_decay(xy, 1, 2, 0.01f);
                o = min(o, 100);
                return float4(o.xxx, 1);
            }

            float4 PSF3(float2 xy)
            {
                xy *= 4;
                float o = lorentzian(xy, 0.2, 2.0) * 10;
                return float4(o.xxx, 1);
            }

            float4 PSF4(float2 xy)
            {
                xy *= 4;
                float o = gaussian(xy, 0.3) * 10;
                return float4(o.xxx, 1);
            }

            float4 PSF5(float2 xy)
            {
                xy *= 10;
                int blade_count = 2;
                float prj = angle_projection(xy, blade_count, 0, 1, 0.5, 0.01, 0.3);
                xy *= prj;
                float o = power_law_decay(xy, 1, 2.2, 0.01f);
                o = min(o, 100);
                return float4(o.xxx, 1);
            }

            float4 PSF6(float2 xy)
            {
                xy *= 10;
                int blade_count = 1;
                float prj = angle_projection(xy, blade_count, PI/2, 3, 0.2, 0.04, 0.01);
                xy *= prj;
                float o = power_law_decay(xy, 1, 2.2, 0.01f);
                o = min(o, 100);
                return float4(o.xxx, 1);
            }

            float4 PSF7(float2 xy)
            {
                xy *= 10;
                float o = power_law_decay(xy, 5, 4, 0.001f);
                return float4(o.xxx, 1);
            }

            float2 UV_Convert(float2 uv)
            {
                uv.x = fmod(uv.x + 0.5, 1.0);
                uv.y = fmod(uv.y + 0.5, 1.0);
                float2 fft_extend = _FFT_EXTEND.xy;
                float2 img_map_size = 1 - 2 * fft_extend;
                float2 screen_size = float2(_ScreenX, _ScreenY);
                float2 kenrel_map_size = sqrt(_ScreenX * _ScreenY) * img_map_size / screen_size;

                uv = (uv - (1 - kenrel_map_size) * 0.5) / kenrel_map_size;
                return uv;
            }
            
            float luminance(float3 col)
            {
                return dot(col, float3(0.299, 0.587, 0.114));
            }
            

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                if (_EnableRemap) uv = UV_Convert(i.uv);
                float2 offset = (uv * 2 - 1);
                float4 psf = PSF2(offset);
                return psf;
            }
            ENDCG
        }
    }
}