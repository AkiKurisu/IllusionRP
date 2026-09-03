#pragma once

#include "fft_config.hlsl"
#include "complex_math.hlsl"

#define R2_PASS P/=2; R2_Pass(i, P SWAP_FLAG_PARAM); BUFFER_SWAP

#ifndef INPLACE
void R2_Pass(uint i, uint P, bool wt)
{
    uint k = i / P;
    uint p = i % P;
    uint kP = k * P;
    uint kP2 = kP * 2;
    uint src_0 = kP2 + p;
    uint src_1 = kP2 + p + P;
    uint dst_0 = i;
    uint dst_1 = i + SIZE / 2;

    float phi = inv_sign(2 * PI) / SIZE * kP;
    complex W;
    sincos(phi, W.y, W.x);

    complex2 v_0 = buffer[!wt][index_transform(src_0)];
    complex2 v_1 = buffer[!wt][index_transform(src_1)];
    complex2 ev = complex2(cmul(W, v_1.xy), cmul(W, v_1.zw));
    buffer[wt][index_transform(dst_0)] = v_0 + ev;
    buffer[wt][index_transform(dst_1)] = v_0 - ev;
    GroupMemoryBarrierWithGroupSync();
}
#else
void R2_Pass(uint i, uint P)
{
	ACTIVE_THREAD_BEGIN
	const uint N = SIZE;
	const uint S = N / 2;
	const uint Q = S / P;
	uint k = THREAD_MAPPING_k;
	uint p = THREAD_MAPPING_s;
	//idx = k + r * N/(P*R) + p * N/P
	uint tmp1 = S / P;
	uint tmp2 = N / P * p + k;
	uint idx0 = index_transform(tmp2);
	uint idx1 = index_transform(tmp1 + tmp2);

	float phi = inv_sign(2 * PI) / SIZE * k * P;
	complex W;
	sincos(phi, W.y, W.x);

	complex2 v_0 = buffer[idx0];
	complex2 v_1 = buffer[idx1];
	complex2 ev = complex2(cmul(W, v_1.xy), cmul(W, v_1.zw));
	buffer[idx0] = v_0 + ev;
	buffer[idx1] = v_0 - ev;
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}

#define R2_PASS_REV R2_Pass_Reverse(i, P); P*=2;

void R2_Pass_Reverse(uint i, uint P)
{
	ACTIVE_THREAD_BEGIN
	const uint N = SIZE;
	const uint S = N / 2;
	const uint Q = S / P;
	uint k = THREAD_MAPPING_k;
	uint p = THREAD_MAPPING_s;
	//idx = k + r * N/(P*R) + p * N/P
	uint tmp1 = S / P;
	uint tmp2 = N / P * p + k;
	uint idx0 = index_transform(tmp2);
	uint idx1 = index_transform(tmp1 + tmp2);

	float phi = rev_sign(2 * PI) / SIZE * k * P;
	complex W;
	sincos(phi, W.y, W.x);

	complex2 v_0 = buffer[idx0];
	complex2 v_1 = buffer[idx1];
	buffer[idx0] = v_0 + v_1;
	buffer[idx1] = cmul(W, v_0 - v_1);
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}
#endif

#define R4_PASS P/=4; R4_Pass(i, P SWAP_FLAG_PARAM); BUFFER_SWAP


#ifndef INPLACE
void R4_Pass(uint i, uint P , bool wt)
{
    uint S = SIZE / 4;
	const uint Q = S / P;
    if (i < S)
    {
        uint k = THREAD_MAPPING_k;
        uint p = THREAD_MAPPING_s;
        uint kP = k * P;

        uint kPR = kP * 4;
        uint temp = kPR + p;

        complex2 src_var[4];
        uint dst[4];
        uint r;
        for (r = 0; r < 4; r++)
        {
            src_var[r] = buffer[!wt][index_transform(temp + r * P)];
            dst[r] = index_transform(i + r * S);
        }
        for (r = 1; r < 4; r++)
        {
            float phi = inv_sign(2 * PI) / SIZE * (kP * r);
            complex W;
            sincos(phi, W.y, W.x);
            src_var[r] = complex2(cmul(src_var[r].xy, W), cmul(src_var[r].zw, W));
        }
    	
        // buffer[wt][dst[0]] = src_var[0] + src_var[2] + src_var[1] + src_var[3];
        // buffer[wt][dst[1]] = src_var[0] - src_var[2] + inv_sign(cmuli(src_var[1])) + inv_sign(-cmuli(src_var[3]));
        // buffer[wt][dst[2]] = src_var[0] + src_var[2] - src_var[1] - src_var[3];
        // buffer[wt][dst[3]] = src_var[0] - src_var[2] + inv_sign(-cmuli(src_var[1])) + inv_sign(cmuli(src_var[3]));
    	complex2 f0 = src_var[0] + src_var[2];
    	complex2 f1 = src_var[1] + src_var[3];
    	complex2 f2 = src_var[0] - src_var[2];
    	complex2 f3 = inv_sign(cmuli(src_var[1])) - inv_sign(cmuli(src_var[3]));
        buffer[wt][dst[0]] = f0 + f1;
        buffer[wt][dst[1]] = f2 + f3;
        buffer[wt][dst[2]] = f0 - f1;
        buffer[wt][dst[3]] = f2 - f3;
    }
    GroupMemoryBarrierWithGroupSync();
}
#else
void R4_Pass(uint i, uint P)
{
	ACTIVE_THREAD_BEGIN
	const uint N = SIZE;
	const uint S = SIZE / 4;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 src_var[4];
		uint dst[4];

		uint r;
		for (r = 0; r < 4; r++)
		{
			uint index = index_transform(r * tmp1 + tmp2);
			dst[r] = index;
			src_var[r] = buffer[index];
		}
		for (r = 1; r < 4; r++)
		{
			float phi = inv_sign(2 * PI) / N * (k * P * r);
			complex W;
			sincos(phi, W.y, W.x);
			src_var[r] = complex2(cmul(src_var[r].xy, W), cmul(src_var[r].zw, W));
		}
		// buffer[index_transform(0 * tmp1 + tmp2)] = src_var[0] + src_var[2] + src_var[1] + src_var[3];
		// buffer[index_transform(1 * tmp1 + tmp2)] = src_var[0] - src_var[2] + inv_sign(cmuli(src_var[1])) + inv_sign(-cmuli(src_var[3]));
		// buffer[index_transform(2 * tmp1 + tmp2)] = src_var[0] + src_var[2] - src_var[1] - src_var[3];
		// buffer[index_transform(3 * tmp1 + tmp2)] = src_var[0] - src_var[2] + inv_sign(-cmuli(src_var[1])) + inv_sign(cmuli(src_var[3]));
		// this only optimized for 0.003 ms fuck me
		complex2 f0 = src_var[0] + src_var[2];
		complex2 f1 = src_var[1] + src_var[3];
		complex2 f2 = src_var[0] - src_var[2];
		complex2 f3 = inv_sign(cmuli(src_var[1])) - inv_sign(cmuli(src_var[3]));
		buffer[dst[0]] = f0 + f1;
		buffer[dst[1]] = f2 + f3;
		buffer[dst[2]] = f0 - f1;
		buffer[dst[3]] = f2 - f3;
	}
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}

#define R4_PASS_REV R4_Pass_Reverse(i, P); P*=4;

void R4_Pass_Reverse(uint i, uint P)
{
	ACTIVE_THREAD_BEGIN
	const uint N = SIZE;
	const uint S = SIZE / 4;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;
		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 src_var[4];
		uint dst[4];

		uint r;
		for (r = 0; r < 4; r++)
		{
			uint index = index_transform(r * tmp1 + tmp2);
			dst[r] = index;
			src_var[r] = buffer[index];
		}
		complex twiddles[3];
		[unroll(3)]
		for (r = 1; r < 4; r++)
		{
			float phi = rev_sign(2 * PI) / N * (k * P * r);
			complex W;
			sincos(phi, W.y, W.x);
			twiddles[r-1] = W;
		}
		complex2 f0 = src_var[0] + src_var[2];
		complex2 f1 = src_var[1] + src_var[3];
		complex2 f2 = src_var[0] - src_var[2];
		complex2 f3 = rev_sign(cmuli(src_var[1])) - rev_sign(cmuli(src_var[3]));
		buffer[dst[0]] = f0 + f1;
		buffer[dst[1]] = cmul(f2 + f3,twiddles[0]);
		buffer[dst[2]] = cmul(f0 - f1,twiddles[1]);
		buffer[dst[3]] = cmul(f2 - f3,twiddles[2]);
	}
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}
#endif