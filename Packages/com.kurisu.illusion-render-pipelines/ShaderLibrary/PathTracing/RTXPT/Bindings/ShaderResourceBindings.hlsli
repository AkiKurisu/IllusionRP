/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#ifndef __SHADER_RESOURCE_BINDINGS_HLSLI__ // using instead of "#pragma once" due to https://github.com/microsoft/DirectXShaderCompiler/issues/3943
#define __SHADER_RESOURCE_BINDINGS_HLSLI__

// @IllusionRP: registers are assigned by Unity; raster renderer resources are removed.

#include "../SampleConstantBuffer.h"
#include "BindingDataTypes.hlsli"

// @IllusionRP: Unity cannot reflect struct members of constant buffers, so the constants live in single element structured buffers.
StructuredBuffer<SampleConstants>       t_PathTracingConstants;
StructuredBuffer<SampleMiniConstants>   t_PathTracingMiniConstants;
#define g_Const     (t_PathTracingConstants[0])
#define g_MiniConst (t_PathTracingMiniConstants[0])


// All outputs are defined here
RWTexture2D<float4>                     u_OutputColor; // main HDR output - RenderTargets::OutputColor
RWTexture2D<float4>                     u_ProcessedOutputColor; // tonemapping inputs - RenderTargets::ProcessedOutputColor
RWTexture2D<float4>                     u_PostTonemapOutputColor; // tonemapping outputs - RenderTargets::LdrColor

RWTexture2D<uint>                       u_Throughput; // used by RTXDI, etc. Packed as R11G11B10_FLOAT
RWTexture2D<float4>                     u_MotionVectors; // used by RTXDI, DLSS/TAA, etc.
RWTexture2D<float>                      u_Depth; // used by RTXDI, DLSS/TAA, etc.
RWTexture2D<float>                      u_SpecularHitT; // used by denoisers
RWTexture2D<float>                      u_ScratchFloat1; // used by post-processing

RWTexture2DArray<uint>                  u_StablePlanesHeader;
RWStructuredBuffer<StablePlane>         u_StablePlanesBuffer;
RWTexture2D<float4>                     u_StableRadiance;
RWStructuredBuffer<PackedPathTracerSurfaceData> u_SurfaceData;

// this is for debugging viz
RWStructuredBuffer<DebugFeedbackStruct> u_FeedbackBuffer;
RWStructuredBuffer<DebugLineStruct>     u_DebugLinesBuffer;
RWStructuredBuffer<DeltaTreeVizPathVertex> u_DebugDeltaPathTree;
RWStructuredBuffer<PathPayload>         u_DeltaPathSearchStack;

// DLSS-RR inputs - leaving them globally accessible so we can move the writes where most optimal
RWTexture2D<float4>                     u_RRDiffuseAlbedo;
RWTexture2D<float4>                     u_RRSpecAlbedo;
RWTexture2D<float4>                     u_RRNormalsAndRoughness;
RWTexture2D<float2>                     u_RRSpecMotionVectors;
RWTexture2D<float4>                     u_RRTransparencyLayer;
RWTexture2D<float4>                     u_DenoisingAvgLayerRadiance;

#endif // #ifndef __SHADER_RESOURCE_BINDINGS_HLSLI__
