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

#ifndef RTXDI_TYPE_PACKING_HLSLI
#define RTXDI_TYPE_PACKING_HLSLI

// Unpack two 16-bit snorm values from the lo/hi bits of a dword.
//  - packed: Two 16-bit snorm in low/high bits.
//  - returns: Two float values in [-1,1].
float2 RTXDI_UnpackSnorm2x16(uint packed)
{
    int2 bits = int2(packed << 16, packed) >> 16;
    float2 unpacked = max(float2(bits) / 32767.0, -1.0);
    return unpacked;
}

// Pack two floats into 16-bit snorm values in the lo/hi bits of a dword.
//  - returns: Two 16-bit snorm in low/high bits.
uint RTXDI_PackSnorm2x16(float2 v)
{
    v = any(isnan(v)) ? float2(0, 0) : clamp(v, -1.0, 1.0);
    int2 iv = int2(round(v * 32767.0));
    uint packed = (iv.x & 0x0000ffff) | (iv.y << 16);

    return packed;
}

// Converts normalized direction to the octahedral map (non-equal area, signed normalized).
//  - n: Normalized direction.
//  - returns: Position in octahedral map in [-1,1] for each component.
float2 RTXDI_NormalizedVectorToOctahedralMapping(float3 n)
{
    // Project the sphere onto the octahedron (|x|+|y|+|z| = 1) and then onto the xy-plane.
    float2 p = float2(n.x, n.y) * (1.0 / (abs(n.x) + abs(n.y) + abs(n.z)));

    // Reflect the folds of the lower hemisphere over the diagonals.
    if (n.z < 0.0) {
        p = float2(
            (1.0 - abs(p.y)) * (p.x >= 0.0 ? 1.0 : -1.0),
            (1.0 - abs(p.x)) * (p.y >= 0.0 ? 1.0 : -1.0)
            );
    }

    return p;
}

// Converts point in the octahedral map to normalized direction (non-equal area, signed normalized).
//  - p: Position in octahedral map in [-1,1] for each component.
//  - returns: Normalized direction.
float3 RTXDI_OctahedralMappingToNormalizedVector(float2 p)
{
    float3 n = float3(p.x, p.y, 1.0 - abs(p.x) - abs(p.y));

    // Reflect the folds of the lower hemisphere over the diagonals.
    if (n.z < 0.0) {
        n.xy = float2(
            (1.0 - abs(n.y)) * (n.x >= 0.0 ? 1.0 : -1.0),
            (1.0 - abs(n.x)) * (n.y >= 0.0 ? 1.0 : -1.0)
            );
    }

    return normalize(n);
}

// Encode a normal packed as 2x 16-bit snorms in the octahedral mapping.
uint RTXDI_EncodeNormalizedVectorToSnorm2x16(float3 normal)
{
    float2 octNormal = RTXDI_NormalizedVectorToOctahedralMapping(normal);
    return RTXDI_PackSnorm2x16(octNormal);
}

// Decode a normal packed as 2x 16-bit snorms in the octahedral mapping.
float3 RTXDI_DecodeNormalizedVectorFromSnorm2x16(uint packedNormal)
{
    float2 octNormal = RTXDI_UnpackSnorm2x16(packedNormal);
    return RTXDI_OctahedralMappingToNormalizedVector(octNormal);
}

#endif // RTXDI_TYPE_PACKING_HLSLI