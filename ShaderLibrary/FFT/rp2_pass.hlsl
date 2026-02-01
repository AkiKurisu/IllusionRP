#pragma once

#include "sub_pass.hlsl"

#define RP2_PASS(R) P /= R; RP2_Pass(i, P, R SWAP_FLAG_PARAM); BUFFER_SWAP

#ifndef INPLACE
void RP2_Pass(uint i, uint P, uint R, bool wt)
{
	uint S = SIZE / R;
	uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;
		uint kP = k * P;
		uint kPR = kP * R;
		uint temp = kPR + p;

		bool swt = 0;
		complex2 sub_buffer[2][MAX_RADIX];
		uint dst[MAX_RADIX];

		for (uint r = 0; r < R; r++)
		{
			sub_buffer[swt][r] = buffer[!wt][index_transform(temp + r * P)];
			sub_buffer[!swt][r] = 0;
			dst[r] = index_transform(i + r * S);
		}
		swt = !swt;

		uint sP = R;
		for (uint _ = log2(R); _ > 0; _--)
		{
			SUBPASS(2);
		}

		for (uint t = 0; t < R; t++)
		{
			buffer[wt][dst[t]] = sub_buffer[!swt][t];
		}
	}
	GroupMemoryBarrierWithGroupSync();
}
#else



void shuffle_single_map(inout uint idx, uint N, uint R, uint P)
{
	uint g = N / P;
	uint s = idx / g;
	idx = idx % g;
	idx = (idx % R) * g / R + idx / R + s * g;
}

void shuffle_map_R2(uint N, inout uint idx[MAX_RADIX])
{
	uint i;
	switch (N)
	{
	case 2:
		idx[0] = 0;
		idx[1] = 1;
		break;
	case 4:
		idx[0] = 0;
		idx[1] = 2;
		idx[2] = 1;
		idx[3] = 3;
		break;
	case 8:
		idx[0] = 0;
		idx[1] = 4;
		idx[2] = 2;
		idx[3] = 6;
		idx[4] = 1;
		idx[5] = 5;
		idx[6] = 3;
		idx[7] = 7;
		break;
	case 16:
		idx[0] = 0;
		idx[1] = 8;
		idx[2] = 4;
		idx[3] = 12;
		idx[4] = 2;
		idx[5] = 10;
		idx[6] = 6;
		idx[7] = 14;
		idx[8] = 1;
		idx[9] = 9;
		idx[10] = 5;
		idx[11] = 13;
		idx[12] = 3;
		idx[13] = 11;
		idx[14] = 7;
		idx[15] = 15;
		break;
	}
}

void shuffle_map(uint N, inout uint idx[MAX_RADIX])
{
	for(uint i=0;i<N;i++) idx[i] = i;
	uint P = 1;
	while (P < N/2)
	{
		uint R = 2;
		for (uint r = 0; r < N; r++)
		{
			shuffle_single_map(idx[r], N, R, P);
		}
		P *= R;
	}
}

void RP2_Pass(uint i, uint P, uint R)
{
	ACTIVE_THREAD_BEGIN
	const uint N = SIZE;
	const uint S = N / R;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;

		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 sub_buffer[MAX_RADIX];
		uint idx_map[MAX_RADIX];
		// for(uint r=0;r<R;r++) idx_map[r] = r;
		shuffle_map_R2(R, idx_map);
		for (uint r = 0; r < R; r++)
			sub_buffer[idx_map[r]] = buffer[index_transform(r * tmp1 + tmp2)];

		uint sP = R;
		for (int _ = log2(R) - 1; _ >= 0; _ -= 1)
		{
			SUBPASS(2);
		}

		for (uint t = 0; t < R; t++)
			buffer[index_transform(t * tmp1 + tmp2)] = sub_buffer[t];
	}
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}

#define RP2_PASS_REV(R) RP2_Pass_Reverse(i, P, R); P *= R;

void RP2_Pass_Reverse(uint i, uint P, uint R)
{
	ACTIVE_THREAD_BEGIN
	const uint N = SIZE;
	const uint S = N / R;
	const uint Q = S / P;
	if (i < S)
	{
		uint k = THREAD_MAPPING_k;
		uint p = THREAD_MAPPING_s;

		uint tmp1 = S / P;
		uint tmp2 = N / P * p + k;
		//idx = k + r * N/(P*R) + p * N/P

		complex2 sub_buffer[MAX_RADIX];
		for (uint r = 0; r < R; r++)
		{
			uint index = index_transform(r * tmp1 + tmp2);
			if (index >= SIZE + PADDING_SIZE) continue;
			sub_buffer[r] = buffer[index];
		}

		uint sP = 1;
		for (int _ = log2(R); _ > 0; _ -= 1)
		{
			SUBPASS_REV(2);
		}

		uint idx_map[MAX_RADIX];
		shuffle_map_R2(R, idx_map);
		for (uint t = 0; t < R; t++)
		{
			uint index = index_transform(t * tmp1 + tmp2);
			
			if (index >= SIZE + PADDING_SIZE)continue;
			uint id = idx_map[t];
			
			if (id >= MAX_RADIX)continue;
			buffer[index] = sub_buffer[id];
		}
	}
	ACTIVE_THREAD_END
	GroupMemoryBarrierWithGroupSync();
}
#endif