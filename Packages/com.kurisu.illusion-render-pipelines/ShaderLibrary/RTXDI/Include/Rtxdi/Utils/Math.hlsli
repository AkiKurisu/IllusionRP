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

#ifndef RTXDI_MATH_HLSLI
#define RTXDI_MATH_HLSLI

static const float RTXDI_PI = 3.1415926535;

// Compares two values and returns true if their relative difference is lower than the threshold.
// Zero or negative threshold makes test always succeed, not fail.
bool RTXDI_CompareRelativeDifference(float reference, float candidate, float threshold)
{
    return (threshold <= 0) || abs(reference - candidate) <= threshold * max(reference, candidate);
}

// See if we will reuse this neighbor or history sample using
//    edge-stopping functions (e.g., per a bilateral filter).
bool RTXDI_IsValidNeighbor(float3 ourNorm, float3 theirNorm, float ourDepth, float theirDepth, float normalThreshold, float depthThreshold)
{
    return (dot(theirNorm.xyz, ourNorm.xyz) >= normalThreshold)
        && RTXDI_CompareRelativeDifference(ourDepth, theirDepth, depthThreshold);
}

void SetBit(inout uint target, uint bit, bool value)
{
    target &= ~(1u << bit);
    target |=  (value << bit);
}

bool GetBit(uint target, uint bit)
{
    return (target >> bit) & 1;
}

// "Explodes" an integer, i.e. inserts a 0 between each bit.  Takes inputs up to 16 bit wide.
//      For example, 0b11111111 -> 0b1010101010101010
uint RTXDI_IntegerExplode(uint x)
{
    x = (x | (x << 8)) & 0x00FF00FF;
    x = (x | (x << 4)) & 0x0F0F0F0F;
    x = (x | (x << 2)) & 0x33333333;
    x = (x | (x << 1)) & 0x55555555;
    return x;
}

// Reverse of RTXDI_IntegerExplode, i.e. takes every other bit in the integer and compresses
// those bits into a dense bit firld. Takes 32-bit inputs, produces 16-bit outputs.
//    For example, 0b'abcdefgh' -> 0b'0000bdfh'
uint RTXDI_IntegerCompact(uint x)
{
    x = (x & 0x11111111) | ((x & 0x44444444) >> 1);
    x = (x & 0x03030303) | ((x & 0x30303030) >> 2);
    x = (x & 0x000F000F) | ((x & 0x0F000F00) >> 4);
    x = (x & 0x000000FF) | ((x & 0x00FF0000) >> 8);
    return x;
}

// Converts a 2D position to a linear index following a Z-curve pattern.
uint RTXDI_ZCurveToLinearIndex(uint2 xy)
{
    return RTXDI_IntegerExplode(xy[0]) | (RTXDI_IntegerExplode(xy[1]) << 1);
}

// Converts a linear to a 2D position following a Z-curve pattern.
uint2 RTXDI_LinearIndexToZCurve(uint index)
{
    return uint2(
        RTXDI_IntegerCompact(index),
        RTXDI_IntegerCompact(index >> 1));
}

// 32 bit Jenkins hash
uint RTXDI_JenkinsHash(uint a)
{
    // http://burtleburtle.net/bob/hash/integer.html
    a = (a + 0x7ed55d16) + (a << 12);
    a = (a ^ 0xc761c23c) ^ (a >> 19);
    a = (a + 0x165667b1) + (a << 5);
    a = (a + 0xd3a2646c) ^ (a << 9);
    a = (a + 0xfd7046c5) + (a << 3);
    a = (a ^ 0xb55a4f09) ^ (a >> 16);
    return a;
}

void RTXDI_CartesianToSpherical(float3 cartesian, out float r, out float azimuth, out float elevation)
{
    r = length(cartesian);
    cartesian /= r;

    azimuth = atan2(cartesian.z, cartesian.x);
    elevation = asin(cartesian.y);
}

float3 RTXDI_SphericalToCartesian(float r, float azimuth, float elevation)
{
    float sinAz, cosAz, sinEl, cosEl;
    sincos(azimuth, sinAz, cosAz);
    sincos(elevation, sinEl, cosEl);

    float x = r * cosAz * cosEl;
    float y = r * sinEl;
    float z = r * sinAz * cosEl;

    return float3(x, y, z);
}

// Computes a multiplier to the effective sample count (M) for pairwise MIS
float RTXDI_MFactor(float q0, float q1)
{
    return (q0 <= 0.0f)
        ? 1.0f
        : clamp(pow(min(q1 / q0, 1.0f), 8.0f), 0.0f, 1.0f);
}

// Compute the pairwise MIS weight
float RTXDI_PairwiseMisWeight(float w0, float w1, float M0, float M1)
{
    // Using a balance heuristic
    float balanceDenom = (M0 * w0 + M1 * w1);
    return (balanceDenom <= 0.0f) ? 0.0f : max(0.0f, M0 * w0) / balanceDenom;
}

//
// Distance: Distance from prior hit to current hit
// RayDir: Incoming vector pointing from prior hit to current hit
// SampleNormal: Normal vector of the current hit surface
//
float RTXDI_CalculatePartialJacobian(const float Distance, const float3 RayDir, const float3 SampleNormal)
{
	const float CosineEmissionAngle = saturate(dot(SampleNormal, -RayDir));
	return Distance * Distance / CosineEmissionAngle;
}

// Calculate the elements of the Jacobian to transform the sample's solid angle.
void RTXDI_CalculatePartialJacobian(const float3 RecieverPos, const float3 SamplePos, const float3 SampleNormal,
	out float DistanceToSurfaceSqr, out float CosineEmissionAngle)
{
	const float3 Vec = RecieverPos - SamplePos;

	DistanceToSurfaceSqr = dot(Vec, Vec);
	CosineEmissionAngle = saturate(dot(SampleNormal, Vec * rsqrt(DistanceToSurfaceSqr)));
}

float RTXDI_CalculateJacobian(float3 recieverPos, float3 neighborReceiverPos, float3 neighborPosition, float3 neighborNormal)
{
	// Calculate Jacobian determinant to adjust weight.
	// See Equation (11) in the ReSTIR GI paper.
	float OriginalDistanceSqr = 0.0, OriginalCosine = 0.0;
	float NewDistanceSqr = 0.0, NewCosine = 0.0;
	RTXDI_CalculatePartialJacobian(recieverPos, neighborPosition, neighborNormal, NewDistanceSqr, NewCosine);
	RTXDI_CalculatePartialJacobian(neighborReceiverPos, neighborPosition, neighborNormal, OriginalDistanceSqr, OriginalCosine);

	float Jacobian = (NewCosine * OriginalDistanceSqr) / (OriginalCosine * NewDistanceSqr);

	if (isinf(Jacobian) || isnan(Jacobian))
		Jacobian = 0;

	return Jacobian;
}

#endif // RTXDI_MATH_HLSLI
