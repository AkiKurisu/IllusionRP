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

#pragma pack_matrix(row_major)

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIApplicationBridge.hlsl"
#include "../ShadingHelpers.hlsli"

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/GI/Reservoir.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/ReservoirAddressing.hlsli"

#ifdef WITH_NRD
#define NRD_HEADER_ONLY
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/NRD/NRD.hlsli"
#endif

#include "../ShadingHelpers.hlsli"

static const float kMaxBrdfValue = 1e4;
static const float kMISRoughness = 0.3;

float GetMISWeight(const SplitBrdf roughBrdf, const SplitBrdf trueBrdf, const float3 diffuseAlbedo)
{
    float3 combinedRoughBrdf = roughBrdf.demodulatedDiffuse * diffuseAlbedo + roughBrdf.specular;
    float3 combinedTrueBrdf = trueBrdf.demodulatedDiffuse * diffuseAlbedo + trueBrdf.specular;

    combinedRoughBrdf = clamp(combinedRoughBrdf, 1e-4, kMaxBrdfValue);
    combinedTrueBrdf = clamp(combinedTrueBrdf, 0, kMaxBrdfValue);

    const float initWeight = saturate(calcLuminance(combinedTrueBrdf) / calcLuminance(combinedTrueBrdf + combinedRoughBrdf));
    return initWeight * initWeight * initWeight;
}

RTXDI_GIReservoir LoadInitialSampleReservoir(int2 reservoirPosition, RAB_Surface primarySurface)
{
    const uint gbufferIndex = RTXDI_ReservoirPositionToPointer(g_Const.restirGI.reservoirBufferParams, reservoirPosition, 0);
    const SecondaryGBufferData secondaryGBufferData = u_SecondaryGBuffer[gbufferIndex];

    const float3 normal = octToNdirUnorm32(secondaryGBufferData.normal);
    const float3 throughput = Unpack_R16G16B16A16_FLOAT(secondaryGBufferData.throughputAndFlags).rgb;

    // Note: the secondaryGBufferData.emission field contains the sampled radiance saved in ShadeSecondarySurfaces
    return RTXDI_MakeGIReservoir(secondaryGBufferData.worldPos,
        normal, secondaryGBufferData.emission * throughput, secondaryGBufferData.pdf);
}

#if USE_RAY_QUERY
[numthreads(RTXDI_SCREEN_SPACE_GROUP_SIZE, RTXDI_SCREEN_SPACE_GROUP_SIZE, 1)]
void main(uint2 GlobalIndex : SV_DispatchThreadID)
#else
[shader("raygeneration")]
void RayGen()
#endif
{
#if !USE_RAY_QUERY
    uint2 GlobalIndex = DispatchRaysIndex().xy;
#endif
    uint2 pixelPosition = RTXDI_ReservoirPosToPixelPos(GlobalIndex, g_Const.runtimeParams.activeCheckerboardField);

    if (any(pixelPosition >= int2(g_Const.view.viewportSize)))  // @IllusionRP: the last row and column are outside the viewport.
        return;

    const RAB_Surface primarySurface = RAB_GetGBufferSurface(pixelPosition, false);

    const uint2 reservoirPosition = RTXDI_PixelPosToReservoirPos(pixelPosition, g_Const.runtimeParams.activeCheckerboardField);
    // @IllusionRP: shades the output of the last enabled resampling stage, as the RTXDI context assigns it, rather than the initial samples.
    const RTXDI_GIReservoir reservoir = RTXDI_LoadGIReservoir(g_Const.restirGI.reservoirBufferParams, reservoirPosition, g_Const.restirGI.bufferIndices.finalShadingInputBufferIndex);

    float3 diffuse = 0;
    float3 specular = 0;
    float hitDistance = 0;  // @IllusionRP

    if (RTXDI_IsValidGIReservoir(reservoir))
    {
        hitDistance = length(reservoir.position - primarySurface.worldPos);  // @IllusionRP
        float3 radiance = reservoir.radiance * reservoir.weightSum;

        float3 visibility = 1.0;
        if (g_Const.restirGI.finalShadingParams.enableFinalVisibility)
        {
            visibility = GetFinalVisibility(SceneBVH, primarySurface, reservoir.position);
        }

        radiance *= visibility;

        const SplitBrdf brdf = EvaluateBrdf(primarySurface, reservoir.position);

        if (g_Const.restirGI.finalShadingParams.enableFinalMIS)
        {
            const RTXDI_GIReservoir initialReservoir = LoadInitialSampleReservoir(reservoirPosition, primarySurface);
            const SplitBrdf brdf0 = EvaluateBrdf(primarySurface, initialReservoir.position);

            RAB_Surface roughenedSurface = primarySurface;
            roughenedSurface.material.roughness = max(roughenedSurface.material.roughness, kMISRoughness);

            const SplitBrdf roughBrdf = EvaluateBrdf(roughenedSurface, reservoir.position);
            const SplitBrdf roughBrdf0 = EvaluateBrdf(roughenedSurface, initialReservoir.position);

            const float finalWeight = 1.0 - GetMISWeight(roughBrdf, brdf, primarySurface.material.diffuseAlbedo);
            const float initialWeight = GetMISWeight(roughBrdf0, brdf0, primarySurface.material.diffuseAlbedo);

            const float3 initialRadiance = initialReservoir.radiance * initialReservoir.weightSum;

            diffuse = brdf.demodulatedDiffuse * radiance * finalWeight
                    + brdf0.demodulatedDiffuse * initialRadiance * initialWeight;

            specular = brdf.specular * radiance * finalWeight
                     + brdf0.specular * initialRadiance * initialWeight;
        }
        else
        {
            diffuse = brdf.demodulatedDiffuse * radiance;
            specular = brdf.specular * radiance;
        }
        specular = RayTracedGIDemodulateSpecular(primarySurface, specular);  // @IllusionRP: demodulated by the lobe's FGD
    }

    // @IllusionRP: indirect lighting is the only signal, so this is the first pass, and the sample distance is its hit distance.
    StoreShadingOutput(GlobalIndex, pixelPosition,
        primarySurface.viewDepth, primarySurface.material.roughness, diffuse, specular, hitDistance, true, true);
}
