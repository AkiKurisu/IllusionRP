/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#ifndef __LIGHTING_BINDINGS_HLSLI__    // using instead of "#pragma once" due to https://github.com/microsoft/DirectXShaderCompiler/issues/3943
#define __LIGHTING_BINDINGS_HLSLI__

// @IllusionRP: registers are assigned by Unity.

#include "../PathTracer/Lighting/LightingTypes.hlsli"

// Bindings 10-19 are scene lighting: environment map (distant lights) sampling, local lights sampling, etc.
TextureCube<float4> t_EnvironmentMap;
Texture2D<float>    t_EnvironmentMapImportanceMap;

StructuredBuffer<LightingControlData>       t_LightsCB;
StructuredBuffer<PolymorphicLightInfo>      t_Lights;
StructuredBuffer<PolymorphicLightInfoEx>    t_LightsEx;

// @IllusionRP: Unity binds 32-bit buffers as structured buffers, so typed buffers are declared as StructuredBuffer.
StructuredBuffer<uint>                                t_LightProxyCounters;
StructuredBuffer<uint>                                t_LightProxyIndices;
StructuredBuffer<uint>                                t_LightLocalSamplingBuffer;
Texture2D<uint>                             t_EnvLookupMap;

RWTexture2D<float>                          u_LightFeedbackTotalWeight;
RWTexture2D<uint>  u_LightFeedbackCandidates;

#endif //__LIGHTING_BINDINGS_HLSLI__
