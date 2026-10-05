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

#ifndef RTXDI_BRDF_RAY_SAMPLE_HLSLI
#define RTXDI_BRDF_RAY_SAMPLE_HLSLI

#include "rtxdi/Utils/Math.hlsli"

#define RTXDI_BrdfRaySampleProperties_ContinuousDelta_Bit 0
#define RTXDI_BrdfRaySampleProperties_DiffuseSpecular_Bit 1
#define RTXDI_BrdfRaySampleProperties_ReflectionTransmission_Bit 2

struct RTXDI_BrdfRaySampleProperties
{
    uint flags;

    void SetContinuous()
    {
        SetBit(flags, RTXDI_BrdfRaySampleProperties_ContinuousDelta_Bit, 0);
    }
    bool IsContinuous()
    {
        return !GetBit(flags, RTXDI_BrdfRaySampleProperties_ContinuousDelta_Bit);
    }
    void SetDelta()
    {
        SetBit(flags, RTXDI_BrdfRaySampleProperties_ContinuousDelta_Bit, 1);
    }
    bool IsDelta()
    {
        return GetBit(flags, RTXDI_BrdfRaySampleProperties_ContinuousDelta_Bit);
    }

    void SetDiffuse()
    {
        SetBit(flags, RTXDI_BrdfRaySampleProperties_DiffuseSpecular_Bit, 0);
    }
    bool IsDiffuse()
    {
        return !GetBit(flags, RTXDI_BrdfRaySampleProperties_DiffuseSpecular_Bit);
    }
    void SetSpecular()
    {
        SetBit(flags, RTXDI_BrdfRaySampleProperties_DiffuseSpecular_Bit, 1);
    }
    bool IsSpecular()
    {
        return GetBit(flags, RTXDI_BrdfRaySampleProperties_DiffuseSpecular_Bit);
    }

    void SetReflection()
    {
        SetBit(flags, RTXDI_BrdfRaySampleProperties_ReflectionTransmission_Bit, 0);
    }
    bool IsReflection()
    {
        return !GetBit(flags, RTXDI_BrdfRaySampleProperties_ReflectionTransmission_Bit);
    }
    void SetTransmission()
    {
        SetBit(flags, RTXDI_BrdfRaySampleProperties_ReflectionTransmission_Bit, 1);
    }
    bool IsTransmission()
    {
        return GetBit(flags, RTXDI_BrdfRaySampleProperties_ReflectionTransmission_Bit);
    }
};

RTXDI_BrdfRaySampleProperties RTXDI_DefaultBrdfRaySampleProperties()
{
    RTXDI_BrdfRaySampleProperties properties = (RTXDI_BrdfRaySampleProperties)0;
    properties.SetContinuous();
    properties.SetDiffuse();
    properties.SetReflection();
    return properties;
}

struct RTXDI_BrdfRaySample
{
    // This points outward from the surface in world space
    float3 OutDirection;

    // This is the PDF of picking the ray w.r.t solid angle
    float OutPdf;

    // BRDF of outgoing direction premultiplied by the cosine term
    float3 BrdfTimesNoL;

    RTXDI_BrdfRaySampleProperties properties;
};

RTXDI_BrdfRaySample RTXDI_EmptyBrdfRaySample()
{
    RTXDI_BrdfRaySample brs = (RTXDI_BrdfRaySample)0;
    brs.properties = RTXDI_DefaultBrdfRaySampleProperties();
    return brs;
}

#endif // RTXDI_BRDF_RAY_SAMPLE_HLSLI