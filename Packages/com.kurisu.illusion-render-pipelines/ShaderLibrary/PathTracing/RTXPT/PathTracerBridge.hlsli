/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#ifndef __PATH_TRACER_BRIDGE_HLSLI__ // using instead of "#pragma once" due to https://github.com/microsoft/DirectXShaderCompiler/issues/3943
#define __PATH_TRACER_BRIDGE_HLSLI__

#include "PathTracer/Config.h"
#include "Libraries/ShaderDebug/ShaderDebug.hlsl"
#include "PathTracer/PathTracerTypes.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"   // @IllusionRP
#include "PathTracer/Rendering/Volumes/HomogeneousVolumeSampler.hlsli"
#include "PathTracer/Lighting/EnvMap.hlsli"
#include "PathTracer/Lighting/LightSampler.hlsli"

#if NVRHI_D3D12_WITH_DXR12_OPACITY_MICROMAP
#define RTXPT_FLAG_ALLOW_OPACITY_MICROMAPS RAYQUERY_FLAG_ALLOW_OPACITY_MICROMAPS
#else
#define RTXPT_FLAG_ALLOW_OPACITY_MICROMAPS 0
#endif

namespace Bridge
{
    static uint getSampleIndex();
    
    // When using multiple samples within pixel in realtime mode (which share identical camera ray), only noisy part of radiance (i.e. not direct sky) needs to be attenuated!
    static float getNoisyRadianceAttenuation();

    static uint getMaxBounceLimit();
    
    static uint getMaxDiffuseBounceLimit();

    // Gets primary camera ray for given pixel position; Note: all realtime mode subSamples currently share same camera ray at subSampleIndex == 0 (otherwise denoising guidance buffers would be noisy)
    static Ray computeCameraRay(const uint2 pixelPos);

    /** Helper to create a texture sampler instance.
    The method for computing texture level-of-detail depends on the configuration.
    \param[in] path Path state.
    \param[in] isPrimaryTriangleHit True if primary hit on a triangle.
    \return Texture sampler instance.
    */
    static ActiveTextureSampler createTextureSampler(
        const RayCone rayCone,
        const float3 rayDir,
        float coneTexLODValue,
        float3 normalW,
        bool isPrimaryHit,
        bool isTriangleHit,
        float texLODBias
#if RTXPT_STOCHASTIC_TEXTURE_FILTERING_ENABLE
        ,STF_SamplerState stfSamplerState
#endif
    );

    // @IllusionRP: the surface is evaluated by the material closest-hit shader and returned in the payload.
    static PathTracer::SurfaceData loadSurface( const IllusionPathPayload payload, const float3 rayOrigin, const float3 rayDir,
        const RayCone rayCone, const int pathVertexIndex, const uint2 pixelPosition, DebugContext debug );

    static float3 computeSurfaceRayOrigin(const ShadingData shadingData, const ActiveBSDF bsdf, bool outward);

    static void updateOutsideIoR(inout PathTracer::SurfaceData surfaceData, lpfloat outsideIoR);

    static lpfloat loadIoR(const uint materialID);

    static HomogeneousVolumeData loadHomogeneousVolumeData(const uint materialID);

    // 2.5D motion vectors
    static float3 computeMotionVector(float3 posW, float3 prevPosW);

    // 2.5D motion vectors
    static float3 computeSkyMotionVector(const uint2 pixelPos);

    // @IllusionRP: alpha testing runs in the material any-hit shaders, so there are no bridge alpha test functions.

    // There's a relatively high cost to this when used in large shaders just due to register allocation required for alphaTest, even if all geometries are opaque.
    // Consider simplifying alpha testing - perhaps splitting it up from the main geometry path, load it with fewer indirections or something like that.
    // fix: pixelPos added for the ENABLE_DEBUG_LINES_VIZ debug-line path (stale no-arg IsDebugPixel() did not compile)
    static bool traceVisibilityRay(RayDesc ray, const RayCone rayCone, const int pathVertexIndex, DebugContext debug, uint2 pixelPos);

    // @IllusionRP: scatter rays are traced with TraceRay by the ray generation shader to invoke the material hit shaders.

#if PT_USE_RESTIR_GI
    static void StoreSecondarySurfacePositionAndNormal(uint2 pixelCoordinate, float3 worldPos, float3 normal);
    static void ClearSecondarySurfaceRadiance(uint2 pixelCoordinate);
    static void AddSecondarySurfaceRadiance(uint2 pixelCoordinate, float3 secondaryRadiance);
#endif
    
    // If HasEnvMap returns false, Eval, EvalPdf and Sample will not be called.
    static bool HasEnvMap();
    
    // Used for evaluating environment map in given direction (but no importance sampling); available if HasEnvMap() returns true
    static EnvMap CreateEnvMap();

    // Used for environment map (distant lights) importance sampling; available if HasEnvMap() returns true
    static EnvMapSampler CreateEnvMapImportanceSampler();

    static LightSampler CreateLightSampler( const uint2 pixelPos, float rayConeWidth, float totalPathLength );
    static LightSampler CreateLightSampler( const uint2 pixelPos, bool isScreenSpaceCoherent );

    static float DiffuseEnvironmentMapMIPOffset( );    ///< Use lower MIP level when sampling environment map. Only 0 produces unbiased results

    static void ExportSurfaceInit(uint2 pixelPos);
    static void ExportSurface(const PathState path, PathTracer::SurfaceData surfaceData, float sceneLength, float3 motionVectors/*, const float roughness, const float3 worldNormal, float3 diffBSDFEstimate, float3 specBSDFEstimate*/ );
    static void ExportNonSurface(const PathState path, float3 virtualWorldPos, float3 motionVectors );
    static void ExportSpecHitTStart(const PathState path);
    static void ExportSpecHitTStop(const PathState path);
};

#endif // __PATH_TRACER_BRIDGE_HLSLI__