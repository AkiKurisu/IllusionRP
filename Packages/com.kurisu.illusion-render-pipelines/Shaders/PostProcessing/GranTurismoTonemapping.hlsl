// Modified from https://github.com/yaoling1997/GT-ToneMapping
#ifndef GRAN_TURISMO_TONEMAPPING_INCLUDED
#define GRAN_TURISMO_TONEMAPPING_INCLUDED

float4 _TonemappingParam;

#define _Brightness             _TonemappingParam.x
#define _Contrast               _TonemappingParam.y
#define _LinearStart            _TonemappingParam.z
#define _LinearLength           _TonemappingParam.w

float W_f(float x, float e0, float e1)
{
    if (x <= e0)
        return 0;
    if (x >= e1)
        return 1;
    float a = (x - e0) / (e1 - e0);
    return a * a * (3 - 2 * a);
}

float H_f(float x, float e0, float e1)
{
    if (x <= e0)
        return 0;
    if (x >= e1)
        return 1;
    return (x - e0) / (e1 - e0);
}

float GranTurismoTonemapper(float x)
{
    const float e = 2.71828;
    float P = _Brightness;      // 1
    float a = _Contrast;        // 1
    float m = _LinearStart;     // 0.22
    float l = _LinearLength;    // 0.4
    float c = 1.33f;
    float b = 0;
    float l0 = (P - m) * l / a;
    // float L0 = m - m / a;
    // float L1 = m + (1 - m) / a;
    float L_x = m + a * (x - m);
    float T_x = m * pow(x / m, c) + b;
    float S0 = m + l0;
    float S1 = m + a * l0;
    float C2 = a * P / (P - S1);
    float S_x = P - (P - S1) * pow(e, -(C2 * (x - S0) / P));
    float w0_x = 1 - W_f(x, 0, m);
    float w2_x = H_f(x, m + l0, m + l0);
    float w1_x = 1 - w0_x - w2_x;
    float f_x = T_x * w0_x + L_x * w1_x + S_x * w2_x;
    return f_x;
}
#endif