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

#ifndef RTXDI_RANDOM_SAMPLER_PER_PASS_SEEDS_HLSLI
#define RTXDI_RANDOM_SAMPLER_PER_PASS_SEEDS_HLSLI

#define RTXDI_DI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED 1
#define RTXDI_DI_TEMPORAL_RESAMPLING_RANDOM_SEED 2
#define RTXDI_DI_SPATIAL_RESAMPLING_RANDOM_SEED 3

// Used for resampling from ReSTIR DI buffers on secondary hits
//  when they lie inside the original camera view.
#define RTXDI_SECONDARY_DI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED 5
#define RTXDI_SECONDARY_DI_SPATIAL_RESAMPLING_RANDOM_SEED 6

#define RTXDI_GI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED 11
#define RTXDI_GI_TEMPORAL_RESAMPLING_RANDOM_SEED 12
#define RTXDI_GI_SPATIAL_RESAMPLING_RANDOM_SEED 13
#define RTXDI_GI_SPATIOTEMPORAL_RESAMPLING_RANDOM_SEED 14

#define RTXDI_PT_GENERATE_INITIAL_SAMPLES_RANDOM_SEED 21
#define RTXDI_PT_GENERATE_INITIAL_SAMPLES_REPLAY_RANDOM_SEED 22
#define RTXDI_PT_TEMPORAL_RESAMPLING_RANDOM_SEED 23
#define RTXDI_PT_SPATIAL_RESAMPLING_RANDOM_SEED 24

#define RTXDI_RANDAOM_SAMPLER_PRIME_CONSTANT 31

#endif // RTXDI_RANDOM_SAMPLER_PER_PASS_SEEDS_HLSLI