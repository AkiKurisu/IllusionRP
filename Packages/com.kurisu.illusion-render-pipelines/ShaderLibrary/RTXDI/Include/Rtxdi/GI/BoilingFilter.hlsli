/*
 * SPDX-FileCopyrightText: Copyright (c) 2023-2026 NVIDIA CORPORATION & AFFILIATES. All rights reserved.
 * SPDX-License-Identifier: LicenseRef-NvidiaProprietary
 *
 * NVIDIA CORPORATION, its affiliates and licensors retain all intellectual
 * property and proprietary rights in and to this material, related
 * documentation and any modifications thereto. Any use, reproduction,
 * disclosure or distribution of this material and related documentation
 * without an express license agreement from NVIDIA CORPORATION or
 * its affiliates is strictly prohibited.
 */

#ifndef RTXDI_GI_BOILING_FILTER_HLSLI
#define RTXDI_GI_BOILING_FILTER_HLSLI

#include "Rtxdi/GI/Reservoir.hlsli"
#include "Rtxdi/Utils/BoilingFilter.hlsli"

#ifdef RTXDI_ENABLE_BOILING_FILTER

// Same as RTXDI_BoilingFilter but for GI reservoirs.
void RTXDI_GIBoilingFilter(
    uint2 LocalIndex,
    float filterStrength, // (0..1]
    inout RTXDI_GIReservoir reservoir)
{
    float weight = RTXDI_Luminance(reservoir.radiance) * reservoir.weightSum;

    if (RTXDI_BoilingFilterInternal(LocalIndex, filterStrength, weight))
        reservoir = RTXDI_EmptyGIReservoir();
}

#endif // RTXDI_ENABLE_BOILING_FILTER

#endif // RTXDI_GI_BOILING_FILTER_HLSLI
