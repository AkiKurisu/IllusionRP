/*
 * SPDX-FileCopyrightText: Copyright (c) 2025-2026 NVIDIA CORPORATION & AFFILIATES. All rights reserved.
 * SPDX-License-Identifier: LicenseRef-NvidiaProprietary
 *
 * NVIDIA CORPORATION, its affiliates and licensors retain all intellectual
 * property and proprietary rights in and to this material, related
 * documentation and any modifications thereto. Any use, reproduction,
 * disclosure or distribution of this material and related documentation
 * without an express license agreement from NVIDIA CORPORATION or
 * its affiliates is strictly prohibited.
 */

#ifndef RTXDI_SAMPLED_LIGHT_DATA_HLSLI
#define RTXDI_SAMPLED_LIGHT_DATA_HLSLI

struct RTXDI_SampledLightData
{
    // Light index (bits 0..30) and validity bit (31)
	uint lightData;

	// Sample UV encoded in 16-bit fixed point format
	uint uvData;
};

RTXDI_SampledLightData RTXDI_SampledLightData_CreateInvalidData()
{
	RTXDI_SampledLightData data = (RTXDI_SampledLightData)0;
	return data;
}

static const uint RTXDI_SampledLightData_LightIndexMask = 0x7FFFFFFF;
static const uint RTXDI_SampledLightData_ValidityMask = 0x80000000;

uint RTXDI_SampledLightData_GetLightIndex(RTXDI_SampledLightData sampledLightData)
{
	return sampledLightData.lightData & RTXDI_SampledLightData_LightIndexMask;
}

uint RTXDI_SampledLightData_GetLightValidityBit(RTXDI_SampledLightData sampledLightData)
{
	return sampledLightData.lightData & RTXDI_SampledLightData_ValidityMask;
}

bool RTXDI_SampledLightData_IsValidLightData(RTXDI_SampledLightData sampledLightData)
{
	return RTXDI_SampledLightData_GetLightValidityBit(sampledLightData) != 0;
}

void RTXDI_SampledLightData_SetLightData(inout RTXDI_SampledLightData sampledLightData, uint lightIndex)
{
	// Assumes lightIndex is valid
	sampledLightData.lightData = 0;
	sampledLightData.lightData |= RTXDI_SampledLightData_ValidityMask;
	sampledLightData.lightData |= lightIndex & RTXDI_SampledLightData_LightIndexMask;
}

uint RTXDI_LightIndexToLightData(uint lightIndex)
{
	uint lightData = lightIndex | RTXDI_SampledLightData_ValidityMask;
	return lightData;
}

float2 RTXDI_SampledLightData_GetUVDataFloat2(RTXDI_SampledLightData sampledLightData)
{
	// TODO: Factor out float2 -> uint packing routine
	return float2(sampledLightData.uvData & 0xffff, sampledLightData.uvData >> 16) / float(0xffff);
}

void RTXDI_SampledLightData_SetUVData(inout RTXDI_SampledLightData sampledLightData, float2 uv)
{
	// TODO: Factor out float2 -> uint packing routine
	sampledLightData.uvData = uint(saturate(uv.x) * 0xffff) | (uint(saturate(uv.y) * 0xffff) << 16);
}

#endif // RTXDI_SAMPLED_LIGHT_DATA_HLSLI