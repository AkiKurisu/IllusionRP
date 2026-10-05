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

#ifndef RTXDI_GI_SPATIAL_RESAMPLING_HLSLI
#define RTXDI_GI_SPATIAL_RESAMPLING_HLSLI

#include "Rtxdi/GI/Reservoir.hlsli"
#include "Rtxdi/Utils/Checkerboard.hlsli"

// Enabled by default. Application code need to define those macros appropriately to optimize shaders.
#ifndef RTXDI_GI_ALLOWED_BIAS_CORRECTION
#define RTXDI_GI_ALLOWED_BIAS_CORRECTION RTXDI_BIAS_CORRECTION_RAY_TRACED
#endif

int2 RTXDI_CalculateSpatialResamplingOffset(int sampleIdx, float radius, const uint neighborOffsetMask)
{
    sampleIdx &= int(neighborOffsetMask);
    return int2(float2(RTXDI_NEIGHBOR_OFFSETS_BUFFER[sampleIdx].xy) * radius);
}

RTXDI_GIReservoir RTXDI_GISpatialResampling(
    const uint2 pixelPosition,
    const RAB_Surface surface,
	uint sourceBufferIndex,
    const RTXDI_GIReservoir inputReservoir,
    inout RTXDI_RandomSamplerState rng,
    const RTXDI_RuntimeParameters params,
    const RTXDI_ReservoirBufferParameters reservoirParams,
    const RTXDI_GISpatialResamplingParameters sparams)
{
    const uint numSamples = sparams.numSamples;

    // The current reservoir.
    RTXDI_GIReservoir curReservoir = RTXDI_EmptyGIReservoir();

    float selectedTargetPdf = 0;
    if (RTXDI_IsValidGIReservoir(inputReservoir)) {
        selectedTargetPdf = RAB_GetGISampleTargetPdfForSurface(inputReservoir.position, inputReservoir.radiance, surface);

        RTXDI_CombineGIReservoirs(curReservoir, inputReservoir, /* random = */ 0.5, selectedTargetPdf);
    }

    // We loop through neighbors twice if bias correction is enabled.  Cache the validity / edge-stopping function
    // results for the 2nd time through.
    uint cachedResult = 0;

    // Since we're using our bias correction scheme, we need to remember which light selection we made
    int selected = -1;

    const int neighborSampleStartIdx = int(RTXDI_GetNextRandom(rng) * params.neighborOffsetMask);

    // Walk the specified number of spatial neighbors, resampling using RIS
    for (int i = 0; i < numSamples; ++i)
    {
        // Get screen-space location of neighbor
        int2 idx = int2(pixelPosition) + RTXDI_CalculateSpatialResamplingOffset(neighborSampleStartIdx + i, sparams.samplingRadius, params.neighborOffsetMask);

        idx = RAB_ClampSamplePositionIntoView(idx, false);

        RTXDI_ActivateCheckerboardPixel(idx, false, params.activeCheckerboardField);

        RAB_Surface neighborSurface = RAB_GetGBufferSurface(idx, false);

        // Test surface similarity, discard the sample if the surface is too different.
        if (!RTXDI_IsValidNeighbor(
            RAB_GetSurfaceNormal(surface), RAB_GetSurfaceNormal(neighborSurface),
            RAB_GetSurfaceLinearDepth(surface), RAB_GetSurfaceLinearDepth(neighborSurface),
            sparams.normalThreshold, sparams.depthThreshold))
        {
            continue;
        }

        // Test material similarity and perform any other app-specific tests.
        if (!RAB_AreMaterialsSimilar(RAB_GetMaterial(surface), RAB_GetMaterial(neighborSurface)))
        {
            continue;
        }

        const uint2 neighborReservoirPos = RTXDI_PixelPosToReservoirPos(idx, params.activeCheckerboardField);
        RTXDI_GIReservoir neighborReservoir = RTXDI_LoadGIReservoir(reservoirParams, neighborReservoirPos, sourceBufferIndex);

        if (!RTXDI_IsValidGIReservoir(neighborReservoir))
        {
            continue;
        }

        // Calculate Jacobian determinant to adjust weight.
        // float jacobian = RTXDI_CalculateJacobian(RAB_GetSurfaceWorldPos(surface), RAB_GetSurfaceWorldPos(neighborSurface), neighborReservoir);
		float jacobian = RTXDI_CalculateJacobian(RAB_GetSurfaceWorldPos(surface), RAB_GetSurfaceWorldPos(neighborSurface), neighborReservoir.position, neighborReservoir.normal);

        // Compute reuse weight.
        float targetPdf = RAB_GetGISampleTargetPdfForSurface(neighborReservoir.position, neighborReservoir.radiance, surface);

        // The Jacobian to transform a GI sample's solid angle holds the lengths and angles to the GI sample from the surfaces,
        // that are valuable information to determine if the GI sample should be combined with the current sample's stream.
        // This function also may clamp the value of the Jacobian.
        if (!RAB_ValidateGISampleWithJacobian(jacobian))
        {
            continue;
        }

        // Valid neighbor surface and its GI reservoir. Combine the reservor.
        cachedResult |= (1u << uint(i));

        // Combine
        bool isUpdated = RTXDI_CombineGIReservoirs(curReservoir, neighborReservoir, RTXDI_GetNextRandom(rng), targetPdf * jacobian);
        if (isUpdated) {
            selected = i;
            selectedTargetPdf = targetPdf;
        }
    }

#if RTXDI_GI_ALLOWED_BIAS_CORRECTION >= RTXDI_BIAS_CORRECTION_BASIC
    if (sparams.biasCorrectionMode >= RTXDI_BIAS_CORRECTION_BASIC)
    {
        // Compute the unbiased normalization factor (instead of using 1/M)
        float pi = selectedTargetPdf;
        float piSum = selectedTargetPdf * inputReservoir.M;

        // If the GI reservoir has selected other than the initial sample, the position should be come from the previous frame.
        // However, there is no idea for the previous position of the initial GI reservoir, so it just uses the current position as its previous one.
        // float3 selectedPositionInPreviousFrame = curReservoir.position;

        // We need to walk our neighbors again
        for (int i = 0; i < numSamples; ++i)
        {
            // If we skipped this neighbor above, do so again.
            if ((cachedResult & (1u << uint(i))) == 0) continue;

            // Get the screen-space location of our neighbor
            int2 idx = int2(pixelPosition) + RTXDI_CalculateSpatialResamplingOffset(neighborSampleStartIdx + i, sparams.samplingRadius, params.neighborOffsetMask);

            idx = RAB_ClampSamplePositionIntoView(idx, false);

            RTXDI_ActivateCheckerboardPixel(idx, false, params.activeCheckerboardField);

            // Load our neighbor's G-buffer and its GI reservoir again.
            RAB_Surface neighborSurface = RAB_GetGBufferSurface(idx, false);

            const uint2 neighborReservoirPos = RTXDI_PixelPosToReservoirPos(idx, params.activeCheckerboardField);
            RTXDI_GIReservoir neighborReservoir = RTXDI_LoadGIReservoir(reservoirParams, neighborReservoirPos, sourceBufferIndex);

            // Get the PDF of the sample RIS selected in the first loop, above, *at this neighbor*
            float ps = RAB_GetGISampleTargetPdfForSurface(curReservoir.position, curReservoir.radiance, neighborSurface);

            // This should be done to correct bias.
#if RTXDI_GI_ALLOWED_BIAS_CORRECTION >= RTXDI_BIAS_CORRECTION_RAY_TRACED
            if (sparams.biasCorrectionMode == RTXDI_BIAS_CORRECTION_RAY_TRACED && ps > 0)
            {
                if (!RAB_GetConservativeVisibility(neighborSurface, curReservoir.position))
                {
                    ps = 0;
                }
            }
#endif
            // Select this sample for the (normalization) numerator if this particular neighbor pixel
            // was the one we selected via RIS in the first loop, above.
            pi = selected == i ? ps : pi;

            // Add to the sums of weights for the (normalization) denominator
            piSum += ps * neighborReservoir.M;
        }

        // "MIS-like" normalization
        // {wSum * (pi/piSum)} * 1/selectedTargetPdf
        {
            float normalizationNumerator = pi;
            float normalizationDenominator = selectedTargetPdf * piSum;
            RTXDI_FinalizeGIResampling(curReservoir, normalizationNumerator, normalizationDenominator);
        }
    }
    else
#endif
    {
        // Normalization
        // {wSum * (1/ M)} * 1/selectedTargetPdf
        {
            float normalizationNumerator = 1.0;
            float normalizationDenominator = curReservoir.M * selectedTargetPdf;
            RTXDI_FinalizeGIResampling(curReservoir, normalizationNumerator, normalizationDenominator);
        }
    }

    return curReservoir;
}

#endif // RTXDI_GI_SPATIAL_RESAMPLING_HLSLI
