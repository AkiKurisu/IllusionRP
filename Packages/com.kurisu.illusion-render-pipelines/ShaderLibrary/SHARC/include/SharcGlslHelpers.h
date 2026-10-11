/*
 * Copyright (c) 2023-2026, NVIDIA CORPORATION. All rights reserved.
 *
 * NVIDIA CORPORATION and its licensors retain all intellectual property
 * and proprietary rights in and to this software, related documentation
 * and any modifications thereto.  Any use, reproduction, disclosure or
 * distribution of this software and related documentation without an express
 * license agreement from NVIDIA CORPORATION is strictly prohibited.
 */

#if SHARC_ENABLE_GLSL

// Required extensions
// #extension GL_EXT_buffer_reference : require
// #extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
// #extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
// #extension GL_EXT_shader_atomic_int64 : require
// #extension GL_KHR_shader_subgroup_ballot : require
// #extension GL_EXT_shader_16bit_storage : enable
// #extension GL_EXT_control_flow_attributes : require

// Buffer reference types can be constructed from a 'uint64_t' or a 'uvec2' value.
// The low - order 32 bits of the reference map to and from the 'x' component
// of the 'uvec2'.

#define float2 vec2
#define float3 vec3
#define float4 vec4

#define uint2 uvec2
#define uint3 uvec3
#define uint4 uvec4

#define int2 ivec2
#define int3 ivec3
#define int4 ivec4

#define float16_t2 f16vec2
#define float16_t3 f16vec3
#define float16_t4 f16vec4

#define lerp mix
#define rcp(x) (1.0/(x))
#define saturate(x) clamp(x,0,1)
#define countbits bitCount
#define asfloat uintBitsToFloat
#define asuint floatBitsToUint
#define f32tof16(f) packHalf2x16(vec2(f, 0))
#define f16tof32(u) unpackHalf2x16(u).x
#define InterlockedAdd atomicAdd
#define InterlockedOr atomicOr
#define InterlockedExchange(destination, value, original) original = atomicExchange(destination, value)
#define InterlockedCompareExchange(destination, compare, value, original) original = atomicCompSwap(destination, compare, value)

#define HASH_GRID_CONST const
#define HASH_GRID_LOOP_ATTR [[dont_unroll]]

#define RW_STRUCTURED_BUFFER_I(name, type) RWStructuredBuffer_##type name
#define RW_STRUCTURED_BUFFER(name, type) RW_STRUCTURED_BUFFER_I(name, type)

#define BUFFER_AT_OFFSET(name, offset) name.data[offset]

layout(buffer_reference, std430, buffer_reference_align = 8) buffer RWStructuredBuffer_uint64_t {
    uint64_t data[];
};

layout(buffer_reference, std430, buffer_reference_align = 4) buffer RWStructuredBuffer_uint {
    uint data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) buffer RWStructuredBuffer_uint4 {
    uvec4 data[];
};

#endif // SHARC_ENABLE_GLSL
