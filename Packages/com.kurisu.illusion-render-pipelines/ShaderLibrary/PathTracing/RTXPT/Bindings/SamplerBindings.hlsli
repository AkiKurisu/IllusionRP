/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#ifndef __SAMPLER_BINDINGS_HLSLI__    // using instead of "#pragma once" due to https://github.com/microsoft/DirectXShaderCompiler/issues/3943
#define __SAMPLER_BINDINGS_HLSLI__

// @IllusionRP: Unity inline sampler states; materials sample in their own hit shaders.
SamplerState s_EnvironmentMapSampler_Trilinear_Repeat;
SamplerState s_EnvironmentMapImportanceSampler_Point_Clamp;
#define s_EnvironmentMapSampler s_EnvironmentMapSampler_Trilinear_Repeat
#define s_EnvironmentMapImportanceSampler s_EnvironmentMapImportanceSampler_Point_Clamp

#endif //__SAMPLER_BINDINGS_HLSLI__
