#pragma once
#include "fft_config.hlsl"
#include "complex_math.hlsl"

inline void DTF4(
	in complex2 s0, in complex2 s1, in complex2 s2, in complex2 s3,
	out complex2 o0, out complex2 o1, out complex2 o2, out complex2 o3
) {
	// o0 = s0 + s2 + s1 + s3;
	// o1 = s0 - s2 + inv_sign(cmuli(s1)) + inv_sign(-cmuli(s3));
	// o2 = s0 + s2 - s1 - s3;
	// o3 = s0 - s2 + inv_sign(-cmuli(s1)) + inv_sign(cmuli(s3));
	complex2 f0 = s0 + s2;
	complex2 f1 = s1 + s3;
	complex2 f2 = s0 - s2;
	complex2 f3 = inv_sign(cmuli(s1)) - inv_sign(cmuli(s3));

	o0 = f0 + f1;
	o1 = f2 + f3;
	o2 = f0 - f1;
	o3 = f2 - f3;
}

inline void DFT_R(
	uint R,
	in complex2 src_val[/* at least R+1 */],
	uint t,
	out complex2 out_pos,
	out complex2 out_neg
) {
	complex2 y1 = src_val[0];
	complex2 y2 = src_val[0];

	uint t_loop_count = R / 2 + 1;

	// for (r = 1; r < R; r++)
	for (uint r = 1; r < t_loop_count; r++) {
		float phi = inv_sign(2 * PI) / R * (r * t);
		float2 W;
		sincos(phi, W.y, W.x); // W = cos(phi) + i·sin(phi)

		// complex2 val = src_val[r];
		// complex2 mul1 = cconjmul(val.xy, W);
		// complex2 mul2 = cconjmul(val.zw, W);
		// y1 += complex2(mul1.xy, mul2.xy);
		// y2 += complex2(mul1.zw, mul2.zw); //conj part
		
		complex2 x1 = src_val[r];
		complex2 x2 = src_val[R - r];
		complex2 m1 = W.x * (x1 + x2);
		complex2 m2 = cmuli(W.y * (x1 - x2));
		y1 += m1 + m2;
		y2 += m1 - m2;
	}

	out_pos = y1;
	out_neg = y2;
}


