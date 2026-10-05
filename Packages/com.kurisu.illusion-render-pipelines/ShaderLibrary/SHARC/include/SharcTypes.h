/*
 * Copyright (c) 2023-2026, NVIDIA CORPORATION. All rights reserved.
 *
 * NVIDIA CORPORATION and its licensors retain all intellectual property
 * and proprietary rights in and to this software, related documentation
 * and any modifications thereto.  Any use, reproduction, disclosure or
 * distribution of this software and related documentation without an express
 * license agreement from NVIDIA CORPORATION is strictly prohibited.
 */

#ifndef SHARC_TYPES_H
#define SHARC_TYPES_H

// SharcPackedData uses native float16_t storage. HLSL shaders that include this
// header must be compiled with DXC -enable-16bit-types and, for DXIL, Shader
// Model 6.2 or newer. The runtime device must support native 16-bit types.

struct SharcAccumulationData
{
#if SHARC_ENABLE_SH_ENCODING
    int4 data;
    int4 dataExt;
#else // !SHARC_ENABLE_SH_ENCODING
    uint4 data;
#endif // SHARC_ENABLE_SH_ENCODING
};

struct SharcPackedData
{
    float16_t4 radianceData;
#if SHARC_ENABLE_SH_ENCODING
    uint radianceDataExt;
    uint sampleNumData;
#endif // SHARC_ENABLE_SH_ENCODING
    uint sampleData;
    uint sampleDataExt;
};

#if SHARC_ENABLE_GLSL
layout(buffer_reference, std430, buffer_reference_align = 16) buffer RWStructuredBuffer_SharcAccumulationData
{
    SharcAccumulationData data[];
};

layout(buffer_reference, std430, buffer_reference_align = 16) buffer RWStructuredBuffer_SharcPackedData
{
    SharcPackedData data[];
};
#endif // SHARC_ENABLE_GLSL

#endif // SHARC_TYPES_H
