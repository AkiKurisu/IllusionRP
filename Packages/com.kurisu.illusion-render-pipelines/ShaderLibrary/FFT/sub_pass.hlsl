#pragma once

#include "fft_config.hlsl"
#include "complex_math.hlsl"

#ifndef INPLACE
#define SUBPASS(R_) sP/=R_; R##R_##_Subpass(sub_buffer, swt, sP, k, SIZE, P, R); swt = !swt;

void R2_Subpass(inout complex2 sub_buffer[2][MAX_RADIX], bool wt, uint P, uint k_, uint N_, uint P_, uint R_)
{
    uint N = R_;
    uint S = N / 2;
    for (uint i = 0; i < S; i++)
    {
        uint k = i / P;
        uint p = i % P;
        uint kP = k * P;
        uint kP2 = kP * 2;
        uint src_0 = kP2 + p;
        uint src_1 = kP2 + p + P;
        uint dst_0 = i;
        uint dst_1 = i + S;

        float phi = inv_sign(2 * PI) * (kP / (float)N + k_ * P_ * P / (float)N_);
        complex W;
        sincos(phi, W.y, W.x);
        complex2 v_0 = sub_buffer[!wt][src_0];
        complex2 v_1 = sub_buffer[!wt][src_1];
        complex2 ev = complex2(cmul(W, v_1.xy), cmul(W, v_1.zw));
        sub_buffer[wt][dst_0] = v_0 + ev;
        sub_buffer[wt][dst_1] = v_0 - ev;
    }
}
#else
#define SUBPASS(R_)  sP/=R_; R##R_##_Subpass(sub_buffer, sP, k, SIZE, P, R);
#define SUBPASS_REV(R_)  R##R_##_Subpass_Reverse(sub_buffer, sP, k, SIZE, P, R); sP*=R_;

void R2_Subpass(inout complex2 X[MAX_RADIX], uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = N / 2;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		//idx = k + r * N/(P*R) + p * N/P
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		uint idx0 = tmp2;
		uint idx1 = tmp1 + tmp2;
		
        float phi = inv_sign(2 * PI) * (k * P / (float)N + k_ * P_ * P / (float)N_);
		complex W;
		sincos(phi, W.y, W.x);
		complex2 v_0 = X[idx0];
		complex2 v_1 = X[idx1];
		complex2 ev = complex2(cmul(W, v_1.xy), cmul(W, v_1.zw));
		X[idx0] = v_0 + ev;
		X[idx1] = v_0 - ev;
	}
}


void R2_Subpass_Reverse(inout complex2 X[MAX_RADIX], uint P, uint k_, uint N_, uint P_, uint R_)
{
	const uint N = R_;
	const uint S = N / 2;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		//idx = k + r * N/(P*R) + p * N/P
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		uint idx0 = tmp2;
		uint idx1 = tmp1 + tmp2;
		
		float phi = rev_sign(2 * PI) * (k * P / (float)N + k_ * P_ * P / (float)N_);
		complex W;
		sincos(phi, W.y, W.x);
		complex2 v_0 = X[idx0];
		complex2 v_1 = X[idx1];
		X[idx0] = v_0 + v_1;
		X[idx1] = cmul(W,v_0 - v_1);
	}
}
#endif

#ifndef INPLACE
void R4_Subpass(inout complex2 sub_buffer[2][MAX_RADIX], bool wt, uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = R_ / 4;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		uint kP = k * P;

		uint kPR = kP * 4;
		uint temp = kPR + p;

		uint src[4];
		#define src_var(r_) sub_buffer[!wt][src[r_]]
		uint dst[4];
		uint r;
		for (r = 0; r < 4; r++)
		{
			src[r] = temp + r * P;
			dst[r] = i + r * S;
		}
		[unroll(3)]
		for (r = 1; r < 4; r++)
		{
			float phi = inv_sign(2 * PI) * (kP * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			src_var(r) = complex2(cmul(src_var(r).xy, W), cmul(src_var(r).zw, W));
		}

		complex2 f0 = src_var(0) + src_var(2);
		complex2 f1 = src_var(1) + src_var(3);
		complex2 f2 = src_var(0) - src_var(2);
		complex2 f3 = inv_sign(cmuli(src_var(1))) - inv_sign(cmuli(src_var(3)));
		sub_buffer[wt][dst[0]] = f0 + f1;
		sub_buffer[wt][dst[1]] = f2 + f3;
		sub_buffer[wt][dst[2]] = f0 - f1;
		sub_buffer[wt][dst[3]] = f2 - f3;
		
		#undef src_var
	}
}
#else
void R4_Subpass(inout complex2 sub_buffer[MAX_RADIX], uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = R_ / 4;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 src_var[4];
		uint dst[4];
		uint r;
		for (r = 0; r < 4; r++)
		{
			uint index = r * tmp1 + tmp2;
			dst[r] = index;
			src_var[r] = sub_buffer[index];
		}
		[unroll(3)]
		for (r = 1; r < 4; r++)
		{
			float phi = inv_sign(2 * PI) * (k * P * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			src_var[r] = cmul(src_var[r], W);
		}

		complex2 f0 = src_var[0] + src_var[2];
		complex2 f1 = src_var[1] + src_var[3];
		complex2 f2 = src_var[0] - src_var[2];
		complex2 f3 = inv_sign(cmuli(src_var[1])) - inv_sign(cmuli(src_var[3]));
		sub_buffer[dst[0]] = f0 + f1;
		sub_buffer[dst[1]] = f2 + f3;
		sub_buffer[dst[2]] = f0 - f1;
		sub_buffer[dst[3]] = f2 - f3;
		
	}
}



void R4_Subpass_Reverse(inout complex2 sub_buffer[MAX_RADIX], uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = R_ / 4;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 src_var[4];
		uint dst[4];
		uint r;
		for (r = 0; r < 4; r++)
		{
			uint index = r * tmp1 + tmp2;
			dst[r] = index;
			src_var[r] = sub_buffer[index];
		}
		complex twiddles[3];
		for (r = 1; r < 4; r++)
		{
			float phi = rev_sign(2 * PI) * (k * P * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			twiddles[r-1] = W;
		}

		complex2 f0 = src_var[0] + src_var[2];
		complex2 f1 = src_var[1] + src_var[3];
		complex2 f2 = src_var[0] - src_var[2];
		complex2 f3 = rev_sign(cmuli(src_var[1])) - rev_sign(cmuli(src_var[3]));
		sub_buffer[dst[0]] = f0 + f1;
		sub_buffer[dst[1]] = cmul(f2 + f3,twiddles[0]);
		sub_buffer[dst[2]] = cmul(f0 - f1,twiddles[1]);
		sub_buffer[dst[3]] = cmul(f2 - f3,twiddles[2]);

		// complex2 f[4];
		// f[0] = src_var[0] + src_var[2];
		// f[1] = src_var[1] + src_var[3];
		// f[2] = src_var[0] - src_var[2];
		// f[3] = rev_sign(cmuli(src_var[1])) - rev_sign(cmuli(src_var[3]));
		// src_var[0] = f[0] + f[1];
		// src_var[1] = f[2] + f[3];
		// src_var[2] = f[0] - f[1];
		// src_var[3] = f[2] - f[3];
		// for (r = 1; r < 4; r++)
		// {
		// 	float phi = rev_sign(2 * PI) * (k * P * r / (float)N + k_ * P_ * r * P / (float)N_);
		// 	complex W;
		// 	sincos(phi, W.y, W.x);
		// 	src_var[r] = cmul(src_var[r],W);
		// }
		//
		// sub_buffer[dst[0]] = src_var[0];
		// sub_buffer[dst[1]] = src_var[1];
		// sub_buffer[dst[2]] = src_var[2];
		// sub_buffer[dst[3]] = src_var[3];

		
	}
}
#endif

#ifndef INPLACE
void R3_Subpass(inout complex2 sub_buffer[2][MAX_RADIX], bool wt, uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = R_ / 3;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		uint kP = k * P;

		uint kPR = kP * 3;
		uint temp = kPR + p;

		uint src[3];
		#define src_var(r_) sub_buffer[!wt][src[r_]]
		uint dst[3];
		uint r;
		[unroll(3)]
		for (r = 0; r < 3; r++)
		{
			src[r] = temp + r * P;
			dst[r] = i + r * S;
		}
		[unroll(2)]
		for (r = 1; r < 3; r++)
		{
			float phi = inv_sign(2 * PI) * (kP * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			src_var(r) = cmul(src_var(r), W);
		}

		float phi = inv_sign(2 * PI) / 3;
		complex W;
		sincos(phi, W.y, W.x);
		complex2 x1p2 = src_var(1) + src_var(2);
		complex2 x1m2 = src_var(1) - src_var(2);
		complex2 m1 = W.x * x1p2;
		complex2 m2 = cmuli(W.y * x1m2);
		
		sub_buffer[wt][dst[0]] = src_var(0) + x1p2;
		sub_buffer[wt][dst[1]] = src_var(0) + m1 + m2;
		sub_buffer[wt][dst[2]] = src_var(0) + m1 - m2;
		
		#undef src_var
	}
}
#else
void R3_Subpass(inout complex2 sub_buffer[MAX_RADIX], uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = R_ / 3;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 src_var[3];
		uint dst[3];
		uint r;
		for (r = 0; r < 3; r++)
		{
			uint index = r * tmp1 + tmp2;
			dst[r] = index;
			src_var[r] = sub_buffer[index];
		}
		[unroll(2)]
		for (r = 1; r < 3; r++)
		{
			float phi = inv_sign(2 * PI) * (k * P * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			src_var[r] = cmul(src_var[r], W);
		}

		float phi = inv_sign(2 * PI) / 3;
		complex W;
		sincos(phi, W.y, W.x);
		complex2 x1p2 = src_var[1] + src_var[2];
		complex2 x1m2 = src_var[1] - src_var[2];
		complex2 m1 = W.x * x1p2;
		complex2 m2 = cmuli(W.y * x1m2);
		
		sub_buffer[dst[0]] = src_var[0] + x1p2;
		sub_buffer[dst[1]] = src_var[0] + m1 + m2;
		sub_buffer[dst[2]] = src_var[0] + m1 - m2;
		
	}
}



void R3_Subpass_Reverse(inout complex2 sub_buffer[MAX_RADIX], uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = R_ / 3;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 src_var[3];
		uint dst[3];
		uint r;
		for (r = 0; r < 3; r++)
		{
			uint index = r * tmp1 + tmp2;
			dst[r] = index;
			src_var[r] = sub_buffer[index];
		}
		complex twiddles[2];
		[unroll(2)]
		for (r = 1; r < 3; r++)
		{
			float phi = rev_sign(2 * PI) * (k * P * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			twiddles[r-1] = W;
		}

		float phi = rev_sign(2 * PI) / 3;
		complex W;
		sincos(phi, W.y, W.x);
		complex2 x1p2 = src_var[1] + src_var[2];
		complex2 x1m2 = src_var[1] - src_var[2];
		complex2 m1 = W.x * x1p2;
		complex2 m2 = cmuli(W.y * x1m2);
		
		sub_buffer[dst[0]] = src_var[0] + x1p2;
		sub_buffer[dst[1]] = cmul(src_var[0] + m1 + m2,twiddles[0]);
		sub_buffer[dst[2]] = cmul(src_var[0] + m1 - m2,twiddles[1]);
		
	}
}
#endif

#ifdef MAX_SUBPASS_RADIX
#ifndef INPLACE

#define SUBPASS_N(R_) sP/=R_; RN_Subpass(sub_buffer, swt, R_, sP, k, SIZE, P, R); swt = !swt;

void RN_Subpass(inout complex2 sub_buffer[2][MAX_RADIX], bool wt, uint R, uint P, uint k_, uint N_, uint P_, uint R_)
{
    uint N = R_;
    uint S = N / R;
    uint r;
    for (uint i = 0; i < S; i++)
    {
        uint k = i / P;
        uint p = i % P;
        uint kP = k * P;
        uint kPR = kP * R;
        uint temp = kPR + p;
        complex2 src_val[MAX_SUBPASS_RADIX];
        uint dst[MAX_SUBPASS_RADIX];

        for (r = 0; r < R; r++)
        {
            src_val[r] = sub_buffer[!wt][temp + r * P];
            dst[r] = i + r * S;
        }

        for (r = 1; r < R; r++)
        {
            float phi = inv_sign(2 * PI) * (kP * r / (float)N + k_ * P_ * r * P / (float)N_);
            complex W;
            sincos(phi, W.y, W.x);
            src_val[r] = complex2(cmul(src_val[r].xy, W), cmul(src_val[r].zw, W));
        }

        complex2 res = src_val[0];
        for (r = 1; r < R; r++)
        {
            res += src_val[r];
        }
        sub_buffer[wt][dst[0]] = res;
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
            sub_buffer[wt][dst[t]] = y1;
            sub_buffer[wt][dst[R - t]] = y2;
        }
    }
    
}
#else

#define SUBPASS_N(R_) sP/=R_; RN_Subpass(sub_buffer, R_, sP, k, SIZE, P, R);

void RN_Subpass(inout complex2 sub_buffer[MAX_RADIX], uint R, uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = N / R;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		// [k + r * N//(P*R) + p * N//P for r in range(R)]
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		// idx = tmp1*r + tmp2

		complex2 src_val[MAX_SUBPASS_RADIX];
		uint dst[MAX_SUBPASS_RADIX];

		uint r;

		for (r = 0; r < R; r++)
		{
			src_val[r] = sub_buffer[tmp1 * r + tmp2];
			dst[r] = tmp1 * r + tmp2;
		}

		for (r = 1; r < R; r++)
		{
			float phi = inv_sign(2 * PI) * (k * P * r / (float)N + k_ * P_ * r * P / (float)N_);
			complex W;
			sincos(phi, W.y, W.x);
			src_val[r] = complex2(cmul(src_val[r].xy, W), cmul(src_val[r].zw, W));
		}

		complex2 res = src_val[0];
		for (r = 1; r < R; r++)
		{
			res += src_val[r];
		}
		sub_buffer[dst[0]] = res;
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
			sub_buffer[dst[t]] = y1;
			sub_buffer[dst[R - t]] = y2;
		}
	}
}

#define SUBPASS_N_REV(R_) RN_Subpass_Reverse(sub_buffer, R_, sP, k, SIZE, P, R); sP*=R_;

void RN_Subpass_Reverse(inout complex2 sub_buffer[MAX_RADIX], uint R, uint P, uint k_, uint N_, uint P_, uint R_)
{
	uint N = R_;
	uint S = N / R;
	for (uint i = 0; i < S; i++)
	{
		uint k = i / P;
		uint p = i % P;
		// [k + r * N//(P*R) + p * N//P for r in range(R)]
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		// idx = tmp1*r + tmp2

		complex2 src_val[MAX_SUBPASS_RADIX];
		uint dst[MAX_SUBPASS_RADIX];

		uint r;

		for (r = 0; r < R; r++)
		{
			src_val[r] = sub_buffer[tmp1 * r + tmp2];
			dst[r] = tmp1 * r + tmp2;
		}

		complex2 res = src_val[0];
		for (r = 1; r < R; r++)
			res += src_val[r];
		sub_buffer[dst[0]] = res;
		
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
			float phi1 = rev_sign(2 * PI) * (k * P * t / (float)N + k_ * P_ * t * P / (float)N_);
			float phi2 = rev_sign(2 * PI) * (k * P * (R-t) / (float)N + k_ * P_ * (R-t) * P / (float)N_);
			complex w1,w2;
			sincos(phi1, w1.y, w1.x);
			sincos(phi2, w2.y, w2.x);
			sub_buffer[dst[t]]     = cmul(y1, w1);
			sub_buffer[dst[R - t]] = cmul(y2, w2);
		}
	}
}
#endif
#endif