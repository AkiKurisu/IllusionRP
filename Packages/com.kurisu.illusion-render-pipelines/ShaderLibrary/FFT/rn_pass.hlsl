#pragma once

#include "fft_config.hlsl"
#include "complex_math.hlsl"

#define RN_PASS(R) P /= R; RN_Pass(i, P, R SWAP_FLAG_PARAM); BUFFER_SWAP

#ifndef INPLACE
void RN_Pass(uint i, uint P, uint R, bool wt)
{
	uint S = SIZE / R;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;
		uint kP = k * P;
		uint kPR = kP * R;
		uint temp = kPR + p;
		complex2 src_val[MAX_RADIX];
		uint dst[MAX_RADIX];

		uint r;

		for (r = 0; r < R; r++)
		{
			src_val[r] = buffer[!wt][index_transform(temp + r * P)];
			dst[r] = index_transform(i + r * S);
		}

		for (r = 1; r < R; r++)
		{
			float phi = inv_sign(2 * PI) / SIZE * (kP * r);
			complex W;
			sincos(phi, W.y, W.x);
			src_val[r] = complex2(cmul(src_val[r].xy, W), cmul(src_val[r].zw, W));
		}

		complex2 res = src_val[0];
		for (r = 1; r < R; r++)
			res += src_val[r];
		buffer[wt][dst[0]] = res;
		
		uint t_loop_count = R / 2 + 1;
		for (uint t = 1; t < t_loop_count; t++)
		{
			complex2 y1 = src_val[0];
			complex2 y2 = src_val[0];
			// for (r = 1; r < R; r++)
			for (r = 1; r < t_loop_count; r++)
			{
				float phi = inv_sign(2 * PI) / R * (r * t);
				complex W;
				sincos(phi, W.y, W.x);
				// complex2 val = src_val[r];
				// complex2 mul1 = cconjmul(val.xy, W);
				// complex2 mul2 = cconjmul(val.zw, W);
				// y1 += complex2(mul1.xy, mul2.xy);
				// y2 += complex2(mul1.zw, mul2.zw); //conj part
				complex2 x1 = src_val[r];
				complex2 x2 = src_val[R-r];
				complex2 m1 = W.x * (x1+x2);
				complex2 m2 = cmuli(W.y * (x1-x2));
				y1 += m1 + m2;
				y2 += m1 - m2;
			}
			buffer[wt][dst[t]] = y1;
			buffer[wt][dst[R - t]] = y2;
		}
	}
	GroupMemoryBarrierWithGroupSync();
}
#else
void RN_Pass(uint i, uint P, uint R)
{
	ACTIVE_THREAD_BEGIN
	uint S = SIZE / R;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;
		uint tmp1 = S / P;
		uint tmp2 = SIZE / P * p + k;
		// idx = tmp1*r + tmp2
		
		complex2 src_val[MAX_RADIX];
		uint dst[MAX_RADIX];

		uint r;


		for (r = 0; r < R; r++)
		{
			src_val[r] = buffer[index_transform(tmp1*r + tmp2)];
			dst[r] = index_transform(tmp1*r + tmp2);
		}

		for (r = 1; r < R; r++)
		{
			float phi = inv_sign(2 * PI) / SIZE * (k * P * r);
			complex W;
			sincos(phi, W.y, W.x);
			src_val[r] = complex2(cmul(src_val[r].xy, W), cmul(src_val[r].zw, W));
		}

		complex2 res = src_val[0];
		for (r = 1; r < R; r++)
		{
			res += src_val[r];
		}
		buffer[dst[0]] = res;
		uint t_loop_count = R / 2 + 1;
		for (uint t = 1; t < t_loop_count; t++)
		{
			complex2 y1 = src_val[0];
			complex2 y2 = src_val[0];
			// for (r = 1; r < R; r++)
			for (r = 1; r < t_loop_count; r++)
			{
				float phi = inv_sign(2 * PI) / R * (r * t);
				complex W;
				sincos(phi, W.y, W.x);
				// complex2 val = src_val[r];
				// complex2 mul1 = cconjmul(val.xy, W);
				// complex2 mul2 = cconjmul(val.zw, W);
				// y1 += complex2(mul1.xy, mul2.xy);
				// y2 += complex2(mul1.zw, mul2.zw); //conj part
				complex2 x1 = src_val[r];
				complex2 x2 = src_val[R-r];
				complex2 m1 = W.x * (x1+x2);
				complex2 m2 = cmuli(W.y * (x1-x2));
				y1 += m1 + m2;
				y2 += m1 - m2;
			}
			buffer[dst[t]] = y1;
			buffer[dst[R - t]] = y2;
		}
	}
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}

#define RN_PASS_REV(R) RN_Pass_Reverse(i, P, R); P *= R;

void RN_Pass_Reverse(uint i, uint P, uint R)
{
	ACTIVE_THREAD_BEGIN
	uint S = SIZE / R;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;
		uint tmp1 = S / P;
		uint tmp2 = SIZE / P * p + k;
		// idx = tmp1*r + tmp2
		
		complex2 src_val[MAX_RADIX];
		uint dst[MAX_RADIX];

		uint r;


		for (r = 0; r < R; r++)
		{
			src_val[r] = buffer[index_transform(tmp1*r + tmp2)];
			dst[r] = index_transform(tmp1*r + tmp2);
		}

		complex2 res = src_val[0];
		for (r = 1; r < R; r++)
			res += src_val[r];
		buffer[dst[0]] = res;
		
		uint t_loop_count = R / 2 + 1;
		for (uint t = 1; t < t_loop_count; t++)
		{
			complex2 y1 = src_val[0];
			complex2 y2 = src_val[0];
			// for (r = 1; r < R; r++)
			for (r = 1; r < t_loop_count; r++)
			{
				float phi = rev_sign(2 * PI) / R * (r * t);
				complex W;
				sincos(phi, W.y, W.x);
				// complex2 val = src_val[r];
				// complex2 mul1 = cconjmul(val.xy, W);
				// complex2 mul2 = cconjmul(val.zw, W);
				// y1 += complex2(mul1.xy, mul2.xy);
				// y2 += complex2(mul1.zw, mul2.zw); //conj part
				complex2 x1 = src_val[r];
				complex2 x2 = src_val[R-r];
				complex2 m1 = W.x * (x1+x2);
				complex2 m2 = cmuli(W.y * (x1-x2));
				y1 += m1 + m2;
				y2 += m1 - m2;
			}
			float phi1 = rev_sign(2 * PI) * (k * P * t / (float)SIZE);
			float phi2 = rev_sign(2 * PI) * (k * P * (R-t) / (float)SIZE);
			complex w1,w2;
			sincos(phi1, w1.y, w1.x);
			sincos(phi2, w2.y, w2.x);
			buffer[dst[t]]     = cmul(y1, w1);
			buffer[dst[R - t]] = cmul(y2, w2);
		}
	}
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}
#endif