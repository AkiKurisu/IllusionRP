/*
 * SPDX-FileCopyrightText: Copyright (c) 2026 NVIDIA CORPORATION & AFFILIATES. All rights reserved.
 * SPDX-License-Identifier: LicenseRef-NvidiaProprietary
 *
 * NVIDIA CORPORATION, its affiliates and licensors retain all intellectual
 * property and proprietary rights in and to this material, related
 * documentation and any modifications thereto. Any use, reproduction,
 * disclosure or distribution of this material and related documentation
 * without an express license agreement from NVIDIA CORPORATION or
 * its affiliates is strictly prohibited.
 */

#pragma once

#include <stdint.h>

#include <Rtxdi/RtxdiParameters.h>

namespace rtxdi
{

// Checkerboard sampling modes match those used in NRD, based on frameIndex:
// Even frame(0)  Odd frame(1)   ...
//     B W             W B
//     W B             B W
// BLACK and WHITE modes define cells with VALID data
enum class CheckerboardMode : uint32_t
{
    Off = 0,
    Black = 1,
    White = 2
};

RTXDI_ReservoirBufferParameters CalculateReservoirBufferParameters(uint32_t renderWidth, uint32_t renderHeight, CheckerboardMode checkerboardMode);

void ComputePdfTextureSize(uint32_t maxItems, uint32_t& outWidth, uint32_t& outHeight, uint32_t& outMipLevels);

void FillNeighborOffsetBuffer(uint8_t* buffer, uint32_t neighborOffsetCount);

// 32 bit Jenkins hash
uint32_t JenkinsHash(uint32_t a);

}