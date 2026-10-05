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

#ifndef RTXDI_GI_SPATIOTEMPORAL_RESAMPLING_HLSLI
#define RTXDI_GI_SPATIOTEMPORAL_RESAMPLING_HLSLI

#include "Rtxdi/GI/Reservoir.hlsli"
#include "Rtxdi/GI/SpatialResampling.hlsli"
#include "Rtxdi/GI/TemporalResampling.hlsli"

RTXDI_GIReservoir RTXDI_GISpatioTemporalResampling(
    const uint2 pixelPosition,
    const RAB_Surface surface,
	const uint sourceBufferIndex,
	const float3 screenSpaceMotion,
    RTXDI_GIReservoir inputReservoir,
    inout RTXDI_RandomSamplerState rng,
    const RTXDI_RuntimeParameters params,
    const RTXDI_ReservoirBufferParameters reservoirParams,
    const RTXDI_GISpatioTemporalResamplingParameters stparams)
{
    // Backproject this pixel to last frame
    int2 prevPos = int2(round(float2(pixelPosition) + screenSpaceMotion.xy));
    const float expectedPrevLinearDepth = RAB_GetSurfaceLinearDepth(surface) + screenSpaceMotion.z;

    // The current reservoir.
    RTXDI_GIReservoir curReservoir = RTXDI_EmptyGIReservoir();

    float selectedTargetPdf = 0;
    if (RTXDI_IsValidGIReservoir(inputReservoir))
    {
        selectedTargetPdf = RAB_GetGISampleTargetPdfForSurface(inputReservoir.position, inputReservoir.radiance, surface);

        RTXDI_CombineGIReservoirs(curReservoir, inputReservoir, /* random = */ 0.5, selectedTargetPdf);
    }

    // We loop through neighbors twice if bias correction is enabled.  Cache the validity / edge-stopping function
    // results for the 2nd time through.
    uint cachedResult = 0;

    // Since we're using our bias correction scheme, we need to remember which light selection we made
    int selected = -1;
    bool usingFallback = false;
    bool foundTemporalSurface = false;

    // The loop below jumps around the sample indices a bit to implement all the needed features:
    // 1. Temporal surface search, starting at sample 0 exactly at the motion vector, then a few
    //    samples around that location trying to find a matching surface.
    // 2. Fallback temporal reuse, in case step 1 failed. This step and further sampling 
    //    will switch to sampling around the current pixel position.
    // 3. Spatial reuse with multiple samples merged into the output - in contrast with temporal
    //    reuse that only ends up merging one sample.
    // The normalization loop later in this function relies on the mask produced in the first loop
    // to only consider the actually merged samples, and on the 'prevPos' value being modified
    // to support fallback sampling.
    const int temporalSampleCount = 5;
    const int fallbackSampleCount = 1;
    const int totalTemporalSampleCount = temporalSampleCount + fallbackSampleCount;
    const int totalSampleCount = min(totalTemporalSampleCount + int(stparams.numSamples), 32);

    const int temporalSampleStartIdx = int(RTXDI_GetNextRandom(rng) * 8);
    const int temporalJitterRadius = (params.activeCheckerboardField == 0) ? 1 : 2;
    const int neighborSampleStartIdx = int(RTXDI_GetNextRandom(rng) * params.neighborOffsetMask);

    // Walk the specified number of spatial neighbors, resampling using RIS
    for (int i = 0; i < totalSampleCount; ++i)
    {
        if (i < totalTemporalSampleCount && foundTemporalSurface)
        {
            // If we've just found a temporal surface, skip to the first spatial sample.
            i = totalTemporalSampleCount - 1;
            continue;
        }

        if (i == temporalSampleCount)
        {
            if (stparams.enableFallbackSampling)
            {
                // None of the temporal surfaces at the motion vector matched the current one,
                // switch to the fallback location.
                prevPos = int2(pixelPosition);
                usingFallback = true;
            }
            else
            {
                // Fallback disabled - skip to the first spatial sample
                i = totalTemporalSampleCount - 1;
                continue;
            }
        }

        const bool isFirstTemporalSample = i == 0;
        const bool isJitteredTemporalSample = i < temporalSampleCount;
        const bool isFallbackSample = i == temporalSampleCount;

        // Get screen-space location of neighbor
        int2 idx;
        if (isFirstTemporalSample || isFallbackSample)
        {
            idx = prevPos;

            if (stparams.enablePermutationSampling || isFallbackSample)
                RTXDI_ApplyPermutationSampling(idx, stparams.uniformRandomNumber);
        }
        else if (isJitteredTemporalSample)
        {
            idx = prevPos + RTXDI_CalculateTemporalResamplingOffset(temporalSampleStartIdx + i, temporalJitterRadius);
        }
        else
        {
            idx = prevPos + RTXDI_CalculateSpatialResamplingOffset(neighborSampleStartIdx + i, stparams.samplingRadius, params.neighborOffsetMask);
            idx = RAB_ClampSamplePositionIntoView(idx, true);
        }

        RTXDI_ActivateCheckerboardPixel(idx, true, params.activeCheckerboardField);

        RAB_Surface neighborSurface = RAB_GetGBufferSurface(idx, true);

        // Test surface similarity, discard the sample if the surface is too different.
        // Skip the test if we're sampling around the fallback location.
        if (!usingFallback && !RTXDI_IsValidNeighbor(
            RAB_GetSurfaceNormal(surface), RAB_GetSurfaceNormal(neighborSurface),
            expectedPrevLinearDepth, RAB_GetSurfaceLinearDepth(neighborSurface),
            stparams.normalThreshold, stparams.depthThreshold))
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

        foundTemporalSurface = true;

        if (neighborReservoir.age >= stparams.maxReservoirAge)
        {
            continue;
        }

        // Calculate Jacobian determinant to adjust weight.
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

        // Clamp history length
        neighborReservoir.M = min(neighborReservoir.M, stparams.maxHistoryLength);

        // Make the sample older
        ++neighborReservoir.age;

        // Valid neighbor surface and its GI reservoir. Combine the reservor.
        cachedResult |= (1u << uint(i));

        // Combine
        bool isUpdated = RTXDI_CombineGIReservoirs(curReservoir, neighborReservoir, RTXDI_GetNextRandom(rng), targetPdf * jacobian);
        if (isUpdated)
        {
            selected = i;
            selectedTargetPdf = targetPdf;
        }
    }

#if RTXDI_GI_ALLOWED_BIAS_CORRECTION >= RTXDI_BIAS_CORRECTION_BASIC
    if (stparams.biasCorrectionMode >= RTXDI_BIAS_CORRECTION_BASIC)
    {
        // Compute the unbiased normalization factor (instead of using 1/M)
        float pi = selectedTargetPdf;
        float piSum = selectedTargetPdf * inputReservoir.M;

        // If the GI reservoir has selected other than the initial sample, the position should be come from the previous frame.
        // However, there is no idea for the previous position of the initial GI reservoir, so it just uses the current position as its previous one.
        // float3 selectedPositionInPreviousFrame = curReservoir.position;

        // We need to walk our neighbors again
        for (int i = 0; i < totalSampleCount; ++i)
        {
            // If we skipped this neighbor above, do so again.
            if ((cachedResult & (1u << uint(i))) == 0) continue;

            // Replicate the logic for sample position computation used in the loop above
            const bool isFirstTemporalSample = i == 0;
            const bool isJitteredTemporalSample = i < temporalSampleCount;
            const bool isFallbackSample = i == temporalSampleCount;

            // Get screen-space location of neighbor
            int2 idx;
            if (isFirstTemporalSample || isFallbackSample)
            {
                idx = prevPos;

                if (stparams.enablePermutationSampling || isFallbackSample)
                    RTXDI_ApplyPermutationSampling(idx, stparams.uniformRandomNumber);
            }
            else if (isJitteredTemporalSample)
            {
                idx = prevPos + RTXDI_CalculateTemporalResamplingOffset(temporalSampleStartIdx + i, temporalJitterRadius);
            }
            else
            {
                idx = prevPos + RTXDI_CalculateSpatialResamplingOffset(neighborSampleStartIdx + i, stparams.samplingRadius, params.neighborOffsetMask);
                idx = RAB_ClampSamplePositionIntoView(idx, true);
            }

            RTXDI_ActivateCheckerboardPixel(idx, true, params.activeCheckerboardField);

            // Load our neighbor's G-buffer and its GI reservoir again.
            RAB_Surface neighborSurface = RAB_GetGBufferSurface(idx, true);

            const uint2 neighborReservoirPos = RTXDI_PixelPosToReservoirPos(idx, params.activeCheckerboardField);
            RTXDI_GIReservoir neighborReservoir = RTXDI_LoadGIReservoir(reservoirParams, neighborReservoirPos, sourceBufferIndex);

            // Clamp history length
            neighborReservoir.M = min(neighborReservoir.M, stparams.maxHistoryLength);

            // Get the PDF of the sample RIS selected in the first loop, above, *at this neighbor*
            float ps = RAB_GetGISampleTargetPdfForSurface(curReservoir.position, curReservoir.radiance, neighborSurface);

            // This should be done to correct bias.
#if RTXDI_GI_ALLOWED_BIAS_CORRECTION >= RTXDI_BIAS_CORRECTION_RAY_TRACED
            if (stparams.biasCorrectionMode == RTXDI_BIAS_CORRECTION_RAY_TRACED && ps > 0)
            {
                RAB_Surface fallbackSurface;
                if (i == 0)
                    fallbackSurface = surface;
                else
                    fallbackSurface = neighborSurface;

                if (!RAB_GetTemporalConservativeVisibility(fallbackSurface, neighborSurface, curReservoir.position))
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

#endif // RTXDI_GI_SPATIOTEMPORAL_RESAMPLING_HLSLI
