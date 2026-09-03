#pragma once

#ifdef SQRT_NORMALIZE
	#define FORWARD_FACTOR * (1.0 / sqrt(SIZE))
	#define INVERSE_FACTOR * (1.0 / sqrt(SIZE))
#else
	// energy-preserving normalization
	#define FORWARD_FACTOR * (1.0 / SIZE)
	#define INVERSE_FACTOR 
#endif

#ifdef FORWARD
	#define INPUT_SCALE 
	#define OUTPUT_SCALE FORWARD_FACTOR
#else// INVERSE
	#define INPUT_SCALE INVERSE_FACTOR
	#define OUTPUT_SCALE
#endif

// for convolution 1D
#define FORWARD_INPUT_SCALE FORWARD_FACTOR

#define INVERSE_INPUT_SCALE
#define INVERSE_OUTPUT_SCALE INVERSE_FACTOR

#define PASS_LOOP(PASS, COUNT) [unroll(COUNT)] for (pass_counter = 0; pass_counter < COUNT; pass_counter++) { PASS };

#if   defined SIZE_2048
    #define SIZE 2048
	#ifndef INPLACE
		#define INPLACE
		#define THREAD_REMAP
	#endif

	#define PASSES_COUNT 4
	#define RADIX_SEQ [PASSES_COUNT] = {8,8,8,4}
	#define FFT_PASSES PASS_LOOP(RP2_PASS(8), 3) R4_PASS
	#define FFT_PASSES_REV R4_PASS_REV PASS_LOOP(RP2_PASS_REV(8), 3) 
	#define MAX_RADIX 8
	#define MIN_RADIX 4
#elif defined SIZE_1620
	#define SIZE 1620
	#ifndef INPLACE
		#define INPLACE
		#define THREAD_REMAP
	#endif

	#define PASSES_COUNT 4
	#define RADIX_SEQ [PASSES_COUNT] = {9,9,5,4}
	#define R9_SUBPASS_COUNT 2
	#define R9_SUBPASS_RADIX_SEQ [R9_SUBPASS_COUNT] = {3,3}
	#define EX_PASS_DEC DECLARE_RN_PASS(9,SUBPASS(3) SUBPASS(3))\
	                    DECLARE_RN_PASS_REV(9,SUBPASS_REV(3) SUBPASS_REV(3))
	#define FFT_PASSES RPN_PASS(9) RPN_PASS(9) RN_PASS(5) R4_PASS
	#define FFT_PASSES_REV R4_PASS_REV RN_PASS_REV(5) RPN_PASS_REV(9) RPN_PASS_REV(9)
	#define MAX_RADIX 9
	#define MIN_RADIX 4
	#define MAX_SUBPASS_RADIX 3
#elif defined SIZE_1296
	#define SIZE 1296
	#ifndef INPLACE
		#define INPLACE
		#define THREAD_REMAP
	#endif

	#define PASSES_COUNT 4
	#define RADIX_SEQ [PASSES_COUNT] = {9,9,4,4}
	#define R9_SUBPASS_COUNT 2
	#define R9_SUBPASS_RADIX_SEQ [R9_SUBPASS_COUNT] = {3,3}
	#define EX_PASS_DEC DECLARE_RN_PASS(9,SUBPASS(3) SUBPASS(3))\
		                DECLARE_RN_PASS_REV(9,SUBPASS_REV(3) SUBPASS_REV(3))
	#define FFT_PASSES RPN_PASS(9) RPN_PASS(9) R4_PASS R4_PASS
	#define FFT_PASSES_REV R4_PASS_REV R4_PASS_REV RPN_PASS_REV(9) RPN_PASS_REV(9)
	#define MAX_RADIX 9
	#define MIN_RADIX 4
	#define MAX_SUBPASS_RADIX 3
#elif defined SIZE_1024
	#define SIZE 1024
	#ifndef INPLACE
		#define INPLACE
	#endif
	#ifdef INPLACE
		#define THREAD_REMAP
		#ifndef CONVOLUTION_2D
		#define PADDING
		#endif
	#endif

	// #define PASSES_COUNT 3
	// #define RADIX_SEQ [PASSES_COUNT] = {16,16,4}
	// #define FFT_PASSES PASS_LOOP(RP2_PASS(16),2) R4_PASS
	#define FFT_PASSES_REV R4_PASS_REV PASS_LOOP(RP2_PASS_REV(16),2)
	// #define MAX_RADIX 16
	// #define MIN_RADIX 4

	// @IllusionRP: Save Registers
	#define PASSES_COUNT 10
	#define RADIX_SEQ [PASSES_COUNT] = {2,2,2,2,2,2,2,2,2,2}
	#define FFT_PASSES PASS_LOOP(R2_PASS, 10)
	#define MAX_RADIX 2
	#define MIN_RADIX 2
#elif defined SIZE_972
	#define SIZE 972
	#ifndef INPLACE
		#define INPLACE
	#endif

	#define R9_SUBPASS_COUNT 2
	#define R9_SUBPASS_RADIX_SEQ [R9_SUBPASS_COUNT] = {3,3}
	#define R6_SUBPASS_COUNT 2
	#define R6_SUBPASS_RADIX_SEQ [R6_SUBPASS_COUNT] = {3,2}
	#define EX_PASS_DEC DECLARE_RN_PASS(9,SUBPASS(3) SUBPASS(3))\
		                DECLARE_RN_PASS(6,SUBPASS(3) SUBPASS(2))\
		                DECLARE_RN_PASS_REV(9 ,SUBPASS_REV(3) SUBPASS_REV(3))\
		                DECLARE_RN_PASS_REV(6 ,SUBPASS_REV(2) SUBPASS_REV(3))
	#define MAX_SUBPASS_RADIX 3
	#define PASSES_COUNT 4
	#define RADIX_SEQ [PASSES_COUNT] = {9,3,6,6}
	#define FFT_PASSES RPN_PASS(9) RN_PASS(3) RPN_PASS(6) RPN_PASS(6)
	#define FFT_PASSES_REV RPN_PASS_REV(6) RPN_PASS_REV(6) RN_PASS_REV(3) RPN_PASS_REV(9)
	#define MAX_RADIX 9
	#define MIN_RADIX 3
#elif defined SIZE_729
	#define SIZE 729
	#ifndef INPLACE
		#define INPLACE
	#endif

	#define PASSES_COUNT 3
	#define RADIX_SEQ [PASSES_COUNT] = {9,9,9}
	#define R9_SUBPASS_COUNT 2
	#define R9_SUBPASS_RADIX_SEQ [R9_SUBPASS_COUNT] = {3,3}
	#define EX_PASS_DEC DECLARE_RN_PASS(9,SUBPASS(3) SUBPASS(3))\
                        DECLARE_RN_PASS_REV(9,SUBPASS_REV(3) SUBPASS_REV(3))
	#define FFT_PASSES PASS_LOOP(RPN_PASS(9), 3)
	#define FFT_PASSES_REV PASS_LOOP(RPN_PASS_REV(9), 3)
	#define MAX_RADIX 9
	#define MIN_RADIX 9
	#define MAX_SUBPASS_RADIX 3
#elif defined SIZE_512
    #define SIZE 512
	#ifndef INPLACE
		#define INPLACE
	#endif
	#ifdef INPLACE
		#define PADDING
		#define THREAD_REMAP
	#endif

	#ifndef VERTICAL
		#define PASSES_COUNT 3
		#define RADIX_SEQ [PASSES_COUNT] = {8,8,8}
		#define FFT_PASSES PASS_LOOP(RP2_PASS(8),3)
		#define FFT_PASSES_REV PASS_LOOP(RP2_PASS_REV(8),3)
		#define MAX_RADIX 8
		#define MIN_RADIX 8
		#define MAX_SUBPASS_RADIX 2
	#else
		// @IllusionRP: Save Registers
		#define PASSES_COUNT 9
		#define RADIX_SEQ [PASSES_COUNT] = {2,2,2,2,2,2,2,2,2}
	    #define FFT_PASSES PASS_LOOP(R2_PASS,9)
		#define FFT_PASSES_REV PASS_LOOP(R2_PASS_REV,9) 
	    #define MAX_RADIX 2
	    #define MIN_RADIX 2
	#endif

#elif defined SIZE_256
	#ifndef INPLACE
		#define INPLACE
	#endif
	#ifdef INPLACE
		#define PADDING
		#define THREAD_REMAP
	#endif
	#define SIZE 256
	#define PASSES_COUNT 2
	#define RADIX_SEQ [PASSES_COUNT] = {16,16}
	#define FFT_PASSES PASS_LOOP(RP2_PASS(16),2)
	#define FFT_PASSES_REV PASS_LOOP(RP2_PASS_REV(16),2) 
	#define MAX_RADIX 16
	#define MIN_RADIX 16
	#define MAX_SUBPASS_RADIX 2
#elif defined SIZE_64
	#define SIZE 64
	#ifndef INPLACE
		#define INPLACE
	#endif
	#define PASSES_COUNT 2
	#define RADIX_SEQ [PASSES_COUNT] = {8,8}
	#define FFT_PASSES RP2_PASS(8) RP2_PASS(8)
	#define FFT_PASSES_REV RP2_PASS_REV(8) RP2_PASS_REV(8)
	#define MAX_RADIX 8
	#define MIN_RADIX 8
	#define MAX_SUBPASS_RADIX 2
#elif defined SIZE_32
	#define SIZE 32
	#ifndef INPLACE
		#define INPLACE
	#endif
	#define PASSES_COUNT 2
	#define RADIX_SEQ [PASSES_COUNT] = {8,4}
	#define FFT_PASSES RP2_PASS(8) R4_PASS
	#define FFT_PASSES_REV R4_PASS_REV RP2_PASS_REV(8)
	#define MAX_RADIX 8
	#define MIN_RADIX 4
	#define MAX_SUBPASS_RADIX 2
#elif defined SIZE_16
	#define SIZE 16
	#ifndef INPLACE
		#define INPLACE
	#endif
	#define PASSES_COUNT 2
	#define RADIX_SEQ [PASSES_COUNT] = {4,4}
	#define FFT_PASSES R4_PASS R4_PASS
	#define FFT_PASSES_REV R4_PASS_REV R4_PASS_REV
	#define MAX_RADIX 4
	#define MIN_RADIX 4
#else
	#define SIZE 512

	#define PASSES_COUNT 3
	#define RADIX_SEQ [PASSES_COUNT] = {8,8,8}
	#define FFT_PASSES PASS_LOOP(RP2_PASS(8),3)
	#define FFT_PASSES_REV PASS_LOOP(RP2_PASS_REV(8),3)
	#define MAX_RADIX 8
	#define MIN_RADIX 8
	#define INPLACE
	#define MAX_SUBPASS_RADIX 2
#endif

#define MAX_S (SIZE / MIN_RADIX)
#define THREAD_FULL_ELEMENT MIN_RADIX


#ifdef INVERSE
	#define inv_sign(x) (x)
	#define rev_sign(x) (x)
#elif defined FORWARD
	#define inv_sign(x) (-x)
	#define rev_sign(x) (-x)
#elif defined CONVOLUTION_1D || defined CONVOLUTION_2D
	#ifndef INPLACE
		#define DYNAMIC_INVERSE //for out-of-place
	#else
		#define inv_sign(x) (-x)
		#define rev_sign(x) (x)
	#endif
#else
	#define inv_sign(x) (-x)
	#define rev_sign(x) (-x)
#endif


#ifdef DYNAMIC_INVERSE
static bool is_inv;
#define inv_sign(x) (is_inv? x : -x)
#endif


#define GS_TYPE float4
#define complex float2
#define complex2 float4

#ifdef INOUT_TARGET
	#define SOURCE Target
#else
	Texture2D<complex2> Source;
	#define SOURCE Source
#endif

RWTexture2D<complex2> Target;

#if defined CONVOLUTION_1D || defined CONVOLUTION_2D
Texture2D<complex2> ConvKernelSpectrum;
#endif


#ifdef PADDING
#define PADDING_C 15
#define PADDING_SIZE (SIZE/PADDING_C)
uint index_transform(uint idx)
{
	return idx + idx / PADDING_C;
}
#else
#define index_transform(IDX) (IDX)
#define PADDING_SIZE 0
#endif

#ifndef INPLACE
groupshared GS_TYPE buffer[2][SIZE + PADDING_SIZE];
#define BUFFER_SWAP wt = !wt;
#define SWAP_FLAG_PARAM_DEC , bool wt
#define SWAP_FLAG_PARAM , wt
#define BUFFER_READ buffer[!wt]
#define BUFFER_WRITE buffer[wt]
#else
#ifndef CONVOLUTION_2D
groupshared GS_TYPE buffer[SIZE + PADDING_SIZE];
#endif
#define BUFFER_SWAP
#define SWAP_FLAG_PARAM_DEC
#define SWAP_FLAG_PARAM
#define BUFFER buffer
#endif

#ifdef CONVOLUTION_2D
static bool active;
#define ACTIVE_THREAD_BEGIN if(active){
#define ACTIVE_THREAD_END   }

groupshared GS_TYPE buffer_[2][SIZE + PADDING_SIZE];
static uint thread_task;
#define buffer buffer_[thread_task]
#else
#define ACTIVE_THREAD_BEGIN
#define ACTIVE_THREAD_END
#endif


#ifndef THREAD_REMAP

#define THREAD_MAPPING_k i / P
#define THREAD_MAPPING_s i % P

#else

#define THREAD_MAPPING_k i % Q
#define THREAD_MAPPING_s i / Q

#endif



#if defined READ_BLOCK || defined WRITE_BLOCK || defined RW_SHIFT
int4 ReadWriteRangeAndOffset;
#define ReadWriteRange uint2(ReadWriteRangeAndOffset.xy)
#define ReadWriteOffset int2(ReadWriteRangeAndOffset.zw)
#ifdef VERTICAL
#define RW_BLOCK_DIR(idx) idx.y
#else
#define RW_BLOCK_DIR(idx) idx.x
#endif
#endif

#ifdef RW_SHIFT
#define RWINDEX_SHIFT(idx) uint2(int2(idx) +  ReadWriteOffset.xy)
#else
#define RWINDEX_SHIFT(idx) idx
#endif

#ifdef READ_BLOCK
// utilize unsigned overflow
#define read_source(idx) (RW_BLOCK_DIR(RWINDEX_SHIFT(idx)) - ReadWriteRange.x < ReadWriteRange.y ? SOURCE[RWINDEX_SHIFT(idx)] : complex2(0,0,0,0))
#else
#define read_source(idx) SOURCE[RWINDEX_SHIFT(idx)]
#endif
#ifdef WRITE_BLOCK
#define write_target(idx,val) if(RW_BLOCK_DIR(RWINDEX_SHIFT(idx)) - ReadWriteRange.x < ReadWriteRange.y){Target[RWINDEX_SHIFT(idx)] = val;}
#else
#define write_target(idx,val) Target[RWINDEX_SHIFT(idx)] = val
#endif

// #define DEBUG_CONV_ONLY

