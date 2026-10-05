/*
 * SPDX-FileCopyrightText: Copyright (c) 2020-2026 NVIDIA CORPORATION & AFFILIATES. All rights reserved.
 * SPDX-License-Identifier: LicenseRef-NvidiaProprietary
 *
 * NVIDIA CORPORATION, its affiliates and licensors retain all intellectual
 * property and proprietary rights in and to this material, related
 * documentation and any modifications thereto. Any use, reproduction,
 * disclosure or distribution of this material and related documentation
 * without an express license agreement from NVIDIA CORPORATION or
 * its affiliates is strictly prohibited.
 */

#ifndef RTXDI_RESTIRGI_PARAMETERS_H
#define RTXDI_RESTIRGI_PARAMETERS_H

#include "Rtxdi/RtxdiParameters.h"
#include "Rtxdi/RtxdiTypes.h"

struct RTXDI_PackedGIReservoir
{
#ifdef __cplusplus
    using float3 = float[3];
#endif

    float3      position;
    uint32_t    packed_miscData_age_M; // See Reservoir.hlsli about the detail of the bit field.

    uint32_t    packed_radiance;    // Stored as 32bit LogLUV format.
    float       weight;
    uint32_t    packed_normal;      // Stored as 2x 16-bit snorms in the octahedral mapping
    float       unused;
};

#ifdef __cplusplus
enum class RTXDI_GIBiasCorrectionMode : uint32_t
{
    Off = RTXDI_BIAS_CORRECTION_OFF,
    Basic = RTXDI_BIAS_CORRECTION_BASIC,
    // Pairwise is not supported
    Raytraced = RTXDI_BIAS_CORRECTION_RAY_TRACED
};
#else
#define RTXDI_GIBiasCorrectionMode uint32_t
#endif

struct RTXDI_GIBufferIndices
{
    uint32_t secondarySurfaceReSTIRDIOutputBufferIndex;
    uint32_t temporalResamplingInputBufferIndex;
    uint32_t temporalResamplingOutputBufferIndex;
    uint32_t spatialResamplingInputBufferIndex;

    uint32_t spatialResamplingOutputBufferIndex;
    uint32_t finalShadingInputBufferIndex;
    uint32_t pad1;
    uint32_t pad2;
};

// Very similar to RTXDI_TemporalResamplingParameters but it has an extra field
// It's also not the same algo, and we don't want the two to be coupled
struct RTXDI_GITemporalResamplingParameters //ReSTIRGI_TemporalResamplingParameters
{
    // Surface depth similarity threshold for temporal reuse.
    // If the previous frame surface's depth is within this threshold from the current frame surface's depth,
    // the surfaces are considered similar. The threshold is relative, i.e. 0.1 means 10% of the current depth.
    // Otherwise, the pixel is not reused, and the resampling shader will look for a different one.
    float depthThreshold;

    // Surface normal similarity threshold for temporal reuse.
    // If the dot product of two surfaces' normals is higher than this threshold, the surfaces are considered similar.
    // Otherwise, the pixel is not reused, and the resampling shader will look for a different one.
    float normalThreshold;

    // Maximum history length for reuse, measured in frames.
    // Higher values result in more stable and high quality sampling, at the cost of slow reaction to changes.
    uint32_t maxHistoryLength;

    // Enables resampling from a location around the current pixel instead of what the motion vector points at,
    // in case no surface near the motion vector matches the current surface (e.g. disocclusion).
    // This behavoir makes disocclusion areas less noisy but locally biased, usually darker.
    uint32_t enableFallbackSampling;

    // Controls the bias correction math for temporal reuse. Depending on the setting, it can add
    // some shader cost and one approximate shadow ray per pixel (or per two pixels if checkerboard sampling is enabled).
    // Ideally, these rays should be traced through the previous frame's BVH to get fully unbiased results.
    RTXDI_GIBiasCorrectionMode biasCorrectionMode;

    // Discard the reservoir if its age exceeds this value.
    uint32_t maxReservoirAge;

    // Enables permuting the pixels sampled from the previous frame in order to add temporal
    // variation to the output signal and make it more denoiser friendly.
    uint32_t enablePermutationSampling;

    // Random number for permutation sampling that is the same for all pixels in the frame
    uint32_t uniformRandomNumber;
};

// See note for ReSTIRGI_TemporalResamplingParameters
struct RTXDI_GISpatialResamplingParameters
{
    // Surface depth similarity threshold for temporal reuse.
    // If the previous frame surface's depth is within this threshold from the current frame surface's depth,
    // the surfaces are considered similar. The threshold is relative, i.e. 0.1 means 10% of the current depth.
    // Otherwise, the pixel is not reused, and the resampling shader will look for a different one.
    float depthThreshold;

    // Surface normal similarity threshold for temporal reuse.
    // If the dot product of two surfaces' normals is higher than this threshold, the surfaces are considered similar.
    // Otherwise, the pixel is not reused, and the resampling shader will look for a different one.
    float normalThreshold;

    // Number of neighbor pixels considered for resampling (1-32)
    // Some of the may be skipped if they fail the surface similarity test.
    uint32_t numSamples;

    // Screen-space radius for spatial resampling, measured in pixels.
    float samplingRadius;

    // Controls the bias correction math for temporal reuse. Depending on the setting, it can add
    // some shader cost and one approximate shadow ray per pixel (or per two pixels if checkerboard sampling is enabled).
    // Ideally, these rays should be traced through the previous frame's BVH to get fully unbiased results.
    RTXDI_GIBiasCorrectionMode biasCorrectionMode;
    uint32_t pad1;
    uint32_t pad2;
    uint32_t pad3;
};

struct RTXDI_GISpatioTemporalResamplingParameters
{
    // Common parameters from both temporal and spatial resampling
    float depthThreshold;

    float normalThreshold;

    RTXDI_GIBiasCorrectionMode biasCorrectionMode;

    // Spatial parameters
    uint32_t numSamples;

    float samplingRadius;

    // Temporal parameters
    uint32_t maxHistoryLength;

    uint32_t enableFallbackSampling;

    uint32_t maxReservoirAge;

    uint32_t enablePermutationSampling;

    uint32_t uniformRandomNumber;

    uint32_t pad1;
    uint32_t pad2;
};

struct RTXDI_GIFinalShadingParameters
{
    uint32_t enableFinalVisibility;
    uint32_t enableFinalMIS;
    uint32_t pad1;
    uint32_t pad2;
};

struct RTXDI_GIParameters
{
    RTXDI_ReservoirBufferParameters reservoirBufferParams;
    RTXDI_GIBufferIndices bufferIndices;
    RTXDI_GITemporalResamplingParameters temporalResamplingParams;
    RTXDI_BoilingFilterParameters boilingFilterParams;
    RTXDI_GISpatialResamplingParameters spatialResamplingParams;
    RTXDI_GISpatioTemporalResamplingParameters spatioTemporalResamplingParams;
    RTXDI_GIFinalShadingParameters finalShadingParams;
};

#endif // RTXDI_RESTIRGI_PARAMETERS_H
