#pragma once
#include "complex_math.hlsl"

struct decode_out
{
	float4 X1;
	float4 Y1;
	float4 X2;
	float4 Y2;
};

decode_out real_fft_freq_decode(float4 z1, float4 z2)
{
	float4 cz1 = cconj(z1);
	float4 cz2 = cconj(z2);
	decode_out o;
	o.X1 = 0.5 * float4(z1.xy + cz2.xy, cmuli(-(z1.xy - cz2.xy)));
	o.Y1 = 0.5 * float4(z1.zw + cz2.zw, cmuli(-(z1.zw - cz2.zw)));
	o.X2 = 0.5 * float4(z2.xy + cz1.xy, cmuli(-(z2.xy - cz1.xy)));
	o.Y2 = 0.5 * float4(z2.zw + cz1.zw, cmuli(-(z2.zw - cz1.zw)));
	return o;
}

struct encode_out
{
	float4 Z1;
	float4 Z2;
};

encode_out real_fft_freq_encode(decode_out i)
{
	encode_out o;
	o.Z1 = float4(i.X1.xy + cmuli(i.X1.zw), i.Y1.xy + cmuli(i.Y1.zw));
	o.Z2 = float4(i.X2.xy + cmuli(i.X2.zw), i.Y2.xy + cmuli(i.Y2.zw));
	return o;
}

decode_out multi_convolve(decode_out a, decode_out b)
{
	decode_out o;
	o.X1 = cmul(a.X1, b.X1);
	o.Y1 = cmul(a.Y1, b.Y1);
	o.X2 = cmul(a.X2, b.X2);
	o.Y2 = cmul(a.Y2, b.Y2);
	return o;
}

void SymmetricMultiplication(inout float4 x1,inout float4 x2, in float4 y1, in float4 y2)
{
	decode_out target_decode = real_fft_freq_decode(x1,x2);
	decode_out filter_decode = real_fft_freq_decode(y1,y2);
	target_decode = multi_convolve(target_decode, filter_decode);
	encode_out target_encode = real_fft_freq_encode(target_decode);
	#ifndef INVERSE_INPUT_SCALE
	x1 = target_encode.Z1;
	x2 = target_encode.Z2;
	#else
	x1 = target_encode.Z1 INVERSE_INPUT_SCALE;
	x2 = target_encode.Z2 INVERSE_INPUT_SCALE;
	#endif
}