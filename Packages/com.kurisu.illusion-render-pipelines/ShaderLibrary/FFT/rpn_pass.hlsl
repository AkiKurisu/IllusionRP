#pragma once

#include "fft_config.hlsl"
#include "sub_pass.hlsl"

#define RPN_PASS(R) P /= R; MixedR##R##_Pass(i, P SWAP_FLAG_PARAM); BUFFER_SWAP

//idk wtf is going on with index_transform identifier in marco, need a new identifier here
#define _rpn_index_remap(idx) index_transform(idx)
// uint _rpn_index_remap(uint idx){return index_transform(idx);}

#ifdef MAX_SUBPASS_RADIX
#ifndef INPLACE
#define DECLARE_RN_PASS(_R_,_RN_SUBPASS_)\
void MixedR##_R_##_Pass(uint i, uint P, bool wt)\
{\
	const uint R = _R_;\
	uint S = SIZE / R;\
	const uint Q = S / P;\
	if (i < S)\
	{\
		uint k = THREAD_MAPPING_k;\
		uint p = THREAD_MAPPING_s;\
		uint kP = k * P;\
		uint kPR = kP * R;\
		uint temp = kPR + p;\
		bool swt = 0;\
		complex2 sub_buffer[2][MAX_RADIX];\
		for (uint r = 0; r < R; r++)\
		{\
			sub_buffer[swt][r] = buffer[!wt][_rpn_index_remap(temp + r * P)];\
			sub_buffer[!swt][r] = 0;\
		}\
		swt = !swt;\
		uint sP = R;\
		\
		{\
			_RN_SUBPASS_\
		}\
		for (uint t = 0; t < R; t++)\
		{\
			buffer[wt][_rpn_index_remap(i + t * S)] = sub_buffer[!swt][t];\
		}\
	}\
	GroupMemoryBarrierWithGroupSync();\
}

#define DECLARE_RN_PASS_REV(_R_,_RN_SUBPASS_)

#else

#define DECLARE_RN_PASS(_R_,_RN_SUBPASS_)\
void MixedR##_R_##_Pass(uint i, uint P)\
{\
	ACTIVE_THREAD_BEGIN\
	const uint R = _R_;\
	const uint N = SIZE;\
	const uint S = N / R;\
	const uint Q = S / P;\
	uint r;\
	if (i < S)\
	{\
		uint k = THREAD_MAPPING_k;\
		uint p = THREAD_MAPPING_s;\
\
		uint tmp1 = S / P;\
		uint tmp2 = N / P * p + k;\
\
		complex2 sub_buffer[MAX_RADIX];\
		uint idx_map[MAX_RADIX];\
		[unroll(_R_)]\
		for (r = 0; r < R; r++) idx_map[r] = r;\
\
		const uint radix_seq R##_R_##_SUBPASS_RADIX_SEQ;\
		const uint N_ = _R_;\
		uint P_ = 1;\
		[unroll(R##_R_##_SUBPASS_COUNT - 1)]\
		for (uint R_idx = R##_R_##_SUBPASS_COUNT - 1; R_idx > 0; R_idx--)\
		{\
			uint R_ = radix_seq[R_idx];\
			for (r = 0; r < R; r++)\
			{\
				shuffle_single_map(idx_map[r], N_, R_, P_);\
			}\
			P_ *= R_;\
		}\
		[unroll(_R_)]\
		for (r = 0; r < R; r++)\
			sub_buffer[idx_map[r]] = buffer[ _rpn_index_remap(r * tmp1 + tmp2) ];\
\
		uint sP = R;\
		\
		{\
			_RN_SUBPASS_\
		}\
		[unroll(_R_)]\
		for (uint t = 0; t < R; t++)\
			buffer[ _rpn_index_remap(t * tmp1 + tmp2) ] = sub_buffer[t];\
	}\
	ACTIVE_THREAD_END\
	GroupMemoryBarrierWithGroupSync();\
}

#define RPN_PASS_REV(R) MixedR##R##_Pass_Reverse(i, P); P *= R;

#define DECLARE_RN_PASS_REV(_R_,_RN_SUBPASS_)\
void MixedR##_R_##_Pass_Reverse(uint i, uint P)\
{\
	ACTIVE_THREAD_BEGIN\
	const uint R = _R_;\
	const uint N = SIZE;\
	const uint S = N / R;\
	const uint Q = S / P;\
	uint r;\
	if (i < S)\
	{\
		uint k = THREAD_MAPPING_k;\
		uint p = THREAD_MAPPING_s;\
\
		uint tmp1 = S / P;\
		uint tmp2 = N / P * p + k;\
\
		complex2 sub_buffer[MAX_RADIX];\
		uint idx_map[MAX_RADIX];\
		[unroll(_R_)]\
		for (r = 0; r < R; r++) idx_map[r] = r;\
\
		const uint radix_seq R##_R_##_SUBPASS_RADIX_SEQ;\
		const uint N_ = _R_;\
		uint P_ = 1;\
		[unroll(R##_R_##_SUBPASS_COUNT - 1)]\
		for (uint R_idx = R##_R_##_SUBPASS_COUNT - 1; R_idx > 0; R_idx--)\
		{\
			uint R_ = radix_seq[R_idx];\
			for (r = 0; r < R; r++)\
			{\
				shuffle_single_map(idx_map[r], N_, R_, P_);\
			}\
			P_ *= R_;\
		}\
		[unroll(_R_)]\
		for (r = 0; r < R; r++)\
			sub_buffer[r] = buffer[ _rpn_index_remap(r * tmp1 + tmp2) ];\
		uint sP = 1;\
		\
		{\
			_RN_SUBPASS_\
		}\
		[unroll(_R_)]\
		for (uint t = 0; t < R; t++)\
			buffer[ _rpn_index_remap(t * tmp1 + tmp2) ] = sub_buffer[idx_map[t]];\
	}\
	ACTIVE_THREAD_END\
	GroupMemoryBarrierWithGroupSync();\
}
#endif
#endif