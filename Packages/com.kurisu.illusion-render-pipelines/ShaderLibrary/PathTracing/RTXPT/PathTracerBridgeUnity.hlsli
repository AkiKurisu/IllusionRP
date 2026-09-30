/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

// This software contains source code provided by NVIDIA Corporation.
// @IllusionRP: replaces Donut material lookup with Unity closest-hit evaluation through IllusionPathPayload.

#ifndef __PATH_TRACER_BRIDGE_UNITY_HLSLI__ // using instead of "#pragma once" due to https://github.com/microsoft/DirectXShaderCompiler/issues/3943
#define __PATH_TRACER_BRIDGE_UNITY_HLSLI__

#include "PathTracerBridge.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"
#define PT_EMISSIVE_TABLE_ACCESS
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingEmissive.hlsl"

#include "Bindings/ShaderResourceBindings.hlsli"
#include "Bindings/LightingBindings.hlsli"
#include "Bindings/SamplerBindings.hlsli"
#include "PathTracer/Materials/MaterialPT.h"

RaytracingAccelerationStructure SceneBVH;


uint Bridge::getSampleIndex()
{
    return g_Const.ptConsts.sampleBaseIndex + g_MiniConst.params.x;
}

float Bridge::getNoisyRadianceAttenuation()
{
    // When using multiple samples within pixel in realtime mode (which share identical camera ray), only noisy part of radiance (i.e. not direct sky) needs to be attenuated!
#if PATH_TRACER_MODE != PATH_TRACER_MODE_BUILD_STABLE_PLANES
    return g_Const.ptConsts.invSubSampleCount;
#else
    return 1.0;
#endif
}

uint Bridge::getMaxBounceLimit()
{
#if PT_BOUNCE_COUNT
    return PT_BOUNCE_COUNT;
#else
    return g_Const.ptConsts.bounceCount;
#endif
}

uint Bridge::getMaxDiffuseBounceLimit()
{
#if PT_DIFFUSE_BOUNCE_COUNT
    return PT_DIFFUSE_BOUNCE_COUNT;
#else
    return g_Const.ptConsts.diffuseBounceCount;
#endif
}

Ray Bridge::computeCameraRay(const uint2 pixelPos)
{
    SampleGenerator sampleGenerator = SampleGenerator::make( SampleGeneratorVertexBase::make( pixelPos, 0, Bridge::getSampleIndex() ) );

    // compute camera ray! would make sense to compile out if unused
    float2 subPixelOffset = g_Const.ptConsts.camera.Jitter + (sampleNext2D(sampleGenerator) - 0.5.xx) * g_Const.ptConsts.perPixelJitterAAScale;
    const float2 cameraDoFSample = sampleNext2D(sampleGenerator);
    return ComputeRayThinlens( g_Const.ptConsts.camera, pixelPos, subPixelOffset, cameraDoFSample );
}

// @IllusionRP: emissive triangles follow the analytic lights in the light buffer, in record order.
uint IllusionEmissiveTriangleBase()
{
    return t_LightsCB[0].EnvmapQuadNodeCount + t_LightsCB[0].AnalyticLightCount;
}

uint IllusionEmissiveTriangleLightIndex(uint instanceID, uint triangleIndex)
{
    uint base = IllusionEmissiveTriangleBase();
    uint record = PathTracingFindEmissiveRecord(instanceID, triangleIndex);
    return record < t_LightsCB[0].TotalLightCount - base ? base + record : RTXPT_INVALID_LIGHT_INDEX;
}

PathTracer::SurfaceData Bridge::loadSurface( const IllusionPathPayload payload, const float3 rayOrigin, const float3 rayDir,
    const RayCone rayCone, const int pathVertexIndex, const uint2 pixelPos, DebugContext debug )
{
    const bool frontFacing = PathTracingHasSurfaceFlag(payload, PT_SURFACE_FRONT_FACING);
    const float3 faceN = PathTracingUnpackNormal(payload.normalFace);
    const float3 vertexN = PathTracingUnpackNormal(payload.normalVertex);
    const float3 shadingN = PathTracingUnpackNormal(payload.normalShading);
    const float3 shadingT = PathTracingUnpackNormal(payload.tangentShading);

    const float4 diffuseOpacity = PathTracingUnpackHalf4(payload.diffuseOpacity);
    const float4 specularRoughness = PathTracingUnpackHalf4(payload.specularRoughness);
    const float4 emissionMetallic = PathTracingUnpackHalf4(payload.emissionMetallic);
    const uint family = PathTracingGetFamily(payload);
    const bool hasMediumParameters = family == PT_FAMILY_LIT;
    const float2 transmission = hasMediumParameters ? PathTracingUnpackHalf2(payload.parameters.y) : 0;
    const float2 iorTintR = hasMediumParameters ? PathTracingUnpackHalf2(payload.parameters.z) : float2(1.5, 1.0);
    const float2 tintGB = hasMediumParameters ? PathTracingUnpackHalf2(payload.parameters.w) : 1;


    ShadingData ptShadingData = ShadingData::make();
    ptShadingData.posW = rayOrigin + rayDir * payload.hitT;
    ptShadingData.V    = -rayDir;

    ptShadingData.N = shadingN;
    float3 B = cross(shadingN, shadingT);
    ptShadingData.T = normalize(cross(B, shadingN));
    ptShadingData.B = cross(shadingN, ptShadingData.T) * (PathTracingHasSurfaceFlag(payload, PT_SURFACE_BITANGENT_FLIP) ? -1.0 : 1.0);

    // Primitive data
    ptShadingData.faceNCorrected = frontFacing ? faceN : -faceN;
    ptShadingData.vertexN = frontFacing ? vertexN : -vertexN;
    ptShadingData.frontFacing = frontFacing;
    ptShadingData.N = frontFacing ? ptShadingData.N : -ptShadingData.N;
    ptShadingData.T = frontFacing ? ptShadingData.T : -ptShadingData.T;

    const uint materialID = payload.familyParameters.w;
    const uint materialFlags = materialID < _PathTracingMaterialCount ? _PathTracingMaterials[materialID].Flags : 0u;
    const lpfloat matIoR = Bridge::loadIoR(materialID);
    const bool thinSurface = PathTracingHasSurfaceFlag(payload, PT_SURFACE_THIN);

    ptShadingData.materialID = materialID;
    ptShadingData.mtl = MaterialHeader::make();
    ptShadingData.mtl.setNestedPriority(min(InteriorList::kMaxNestedPriority, 1u + (materialFlags >> PTMaterialFlags_NestedPriorityShift)));
    ptShadingData.mtl.setThinSurface( thinSurface );
    ptShadingData.mtl.setPSDExclude((materialFlags & PTMaterialFlags_PSDExclude) != 0);
    ptShadingData.mtl.setPSDDominantDeltaLobeP1((materialFlags & PTMaterialFlags_PSDDominantDeltaLobeP1Mask) >> PTMaterialFlags_PSDDominantDeltaLobeP1Shift);
    ptShadingData.mtl.setPSDBlockMotionVectorsAtSurface( false );

    // Helper function to adjust the shading normal to reduce black pixels due to back-facing view direction. Note: This breaks the reciprocity of the BSDF!
    adjustShadingNormal( ptShadingData, float4(ptShadingData.T, 1.0), true, false );

    ptShadingData.shadowNoLFadeout = 0;

    lpfloat3 bsdfDataDiffuse = (lpfloat3)diffuseOpacity.rgb;
    lpfloat3 bsdfDataSpecular = (lpfloat3)specularRoughness.rgb;
    lpfloat bsdfDataRoughness = (lpfloat)specularRoughness.a;
    lpfloat bsdfDataMetallic = (lpfloat)emissionMetallic.a;
    lpfloat bsdfDataSpecularTransmission = (lpfloat)transmission.x * (1.f - bsdfDataMetallic);
    lpfloat bsdfDataDiffuseTransmission = (lpfloat)transmission.y * (1.f - bsdfDataMetallic);
    lpfloat3 bsdfDataTransmission = lpfloat3(iorTintR.y, tintGB.x, tintGB.y);

    ptShadingData.mtl.setActiveLobes( (uint)LobeType::All );

    // Assume the default IoR for vacuum on the front-facing side.
    // The renderer may override this for nested dielectrics (see 'handleNestedDielectrics' calling Bridge::updateOutsideIoR)
    ptShadingData.IoR = 1.f;
    lpfloat bsdfDataEta = ptShadingData.IoR / matIoR;
    if (!ptShadingData.mtl.isThinSurface() && !ptShadingData.frontFacing)
        bsdfDataEta = (matIoR / ptShadingData.IoR);

    uint neeTriangleLightIndex = RTXPT_INVALID_LIGHT_INDEX;
    uint neeAnalyticLightIndex = RTXPT_INVALID_LIGHT_INDEX;

#if !defined(RTXPT_MATERIAL_IS_EMISSIVE) || RTXPT_MATERIAL_IS_EMISSIVE
    // The standard material supports uniform emission over the hemisphere.
    // Note: we only support single sided emissives at the moment; If upgrading, make sure to upgrade NEE codepath as well (i.e. PolymorphicLight.hlsli)
    if (ptShadingData.frontFacing)
        ptShadingData.emission = (lpfloat3)emissionMetallic.rgb;
#endif
    if (ptShadingData.frontFacing && any(emissionMetallic.rgb > 0))
        neeTriangleLightIndex = IllusionEmissiveTriangleLightIndex(payload.instanceID, payload.triangleIndex);

    StandardBSDFData bsdfData = StandardBSDFData::make( bsdfDataDiffuse, bsdfDataSpecular, bsdfDataRoughness, bsdfDataMetallic, bsdfDataEta, bsdfDataTransmission, bsdfDataDiffuseTransmission, bsdfDataSpecularTransmission );
    StandardBSDF bsdf = StandardBSDF::make( bsdfData );

    return PathTracer::SurfaceData::make(ptShadingData, bsdf,
#if PATH_TRACER_MODE==PATH_TRACER_MODE_BUILD_STABLE_PLANES // otherwise motion vectors not needed
                                    ptShadingData.posW + PathTracingUnpackHalf4(payload.motion).xyz,
#endif
                                    matIoR, neeTriangleLightIndex, neeAnalyticLightIndex);
}

void Bridge::updateOutsideIoR(inout PathTracer::SurfaceData surfaceData, lpfloat outsideIoR)
{
    surfaceData.shadingData.IoR = outsideIoR;

    ///< Relative index of refraction (incident IoR / transmissive IoR), dependent on whether we're exiting or entering
    surfaceData.bsdf.data.SetEta( surfaceData.shadingData.frontFacing ? (surfaceData.shadingData.IoR / surfaceData.interiorIoR) : (surfaceData.interiorIoR / surfaceData.shadingData.IoR) );
}

lpfloat Bridge::loadIoR(const uint materialID)
{
    if( materialID >= _PathTracingMaterialCount )
        return 1.0;
    else
        return (lpfloat)_PathTracingMaterials[materialID].IoR;
}

HomogeneousVolumeData Bridge::loadHomogeneousVolumeData(const uint materialID)
{
    HomogeneousVolumeData ptVolume;
    ptVolume.sigmaS = float3(0,0,0);
    ptVolume.sigmaA = float3(0,0,0);
    ptVolume.g = 0.0;
    if (materialID >= _PathTracingMaterialCount)
        return ptVolume;
    VolumePTConstants volumeInfo;
    volumeInfo.AttenuationColor = _PathTracingMaterials[materialID].AttenuationColor;
    volumeInfo.AttenuationDistance = _PathTracingMaterials[materialID].AttenuationDistance;
    ptVolume.sigmaA = -log(clamp(volumeInfo.AttenuationColor, 1e-7, 1)) / max(1e-30, volumeInfo.AttenuationDistance.xxx);
    return ptVolume;
}

// 2.5D motion vectors
float3 Bridge::computeMotionVector( float3 posW, float3 prevPosW )
{
    SimpleViewConstants view = g_Const.view;
    SimpleViewConstants previousView = g_Const.previousView;

    float4 clipPos = mul(float4(posW, 1), view.matWorldToClipNoOffset);
    clipPos.xyz /= clipPos.w;
    float4 prevClipPos = mul(float4(prevPosW, 1), previousView.matWorldToClipNoOffset);
    prevClipPos.xyz /= prevClipPos.w;

    if (clipPos.w <= 0 || prevClipPos.w <= 0)
        return float3(0,0,0);

    float3 motion;
    motion.xy = (prevClipPos.xy - clipPos.xy) * view.clipToWindowScale;
    motion.z = prevClipPos.w - clipPos.w; // Use view depth

    return motion;
}

// 2.5D motion vectors
float3 Bridge::computeSkyMotionVector( const uint2 pixelPos )
{
    SimpleViewConstants view = g_Const.view;
    SimpleViewConstants previousView = g_Const.previousView;

    float4 clipPos = float4( (pixelPos + 0.5.xx)/g_Const.view.clipToWindowScale+float2(-1,1), 1e-7, 1.0);
    float4 viewPos = mul( clipPos, view.matClipToWorldNoOffset ); viewPos.xyzw /= viewPos.w;
    float4 prevClipPos = mul(viewPos, previousView.matWorldToClipNoOffset);
    prevClipPos.xyz /= prevClipPos.w;

    float3 motion;
    motion.xy = (prevClipPos.xy - clipPos.xy) * view.clipToWindowScale;
    motion.z = 0;

    return motion;
}

// @IllusionRP: alpha testing and stochastic coverage run in the material any-hit shaders.

bool Bridge::traceVisibilityRay(RayDesc ray, const RayCone rayCone, const int pathVertexIndex, DebugContext debug, uint2 pixelPos)
{
    IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_VISIBILITY, Hash32Combine(Hash32(pixelPos.x + (pixelPos.y << 16)), Bridge::getSampleIndex() * 0x9E3779B9u + pathVertexIndex), rayCone.getWidth(), rayCone.getSpreadAngle());
    payload.hitT = 0.0;
    TraceRay(SceneBVH, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER, 0xff, 0, 1, 0, ray, payload);
    return payload.hitT < 0.0;
}

EnvMap Bridge::CreateEnvMap()
{
    return EnvMap::make( t_EnvironmentMap, s_EnvironmentMapSampler, g_Const.envMapSceneParams );
}

EnvMapSampler Bridge::CreateEnvMapImportanceSampler()
{
    return EnvMapSampler::make(
        s_EnvironmentMapImportanceSampler,
        t_EnvironmentMapImportanceMap,
        g_Const.envMapImportanceSamplingParams,
        t_EnvironmentMap,
        s_EnvironmentMapSampler,
        g_Const.envMapSceneParams
    );
}

LightSampler Bridge::CreateLightSampler( const uint2 pixelPos, float rayConeWidth, float totalPathLength )
{
    bool isScreenSpaceCoherent = LightSampler::IsScreenSpaceCoherentHeuristic( t_LightsCB, rayConeWidth, totalPathLength );
    return LightSampler::make( t_LightsCB, t_Lights, t_LightsEx, t_LightProxyCounters, t_LightProxyIndices, t_LightLocalSamplingBuffer, t_EnvLookupMap, u_LightFeedbackTotalWeight, u_LightFeedbackCandidates, pixelPos, isScreenSpaceCoherent );
}

LightSampler Bridge::CreateLightSampler( const uint2 pixelPos, bool isScreenSpaceCoherent )
{
    return LightSampler::make( t_LightsCB, t_Lights, t_LightsEx, t_LightProxyCounters, t_LightProxyIndices, t_LightLocalSamplingBuffer, t_EnvLookupMap, u_LightFeedbackTotalWeight, u_LightFeedbackCandidates, pixelPos, isScreenSpaceCoherent );
}

bool Bridge::HasEnvMap()
{
    return g_Const.envMapSceneParams.Enabled;
}

float Bridge::DiffuseEnvironmentMapMIPOffset( )
{
    return g_Const.ptConsts.EnvironmentMapDiffuseSampleMIPLevel;
}

void Bridge::ExportSurfaceInit(uint2 pixelPos)
{
    u_Depth[pixelPos] = 0;                  // this is a signal that data is invalid - there's (rare) cases where neither ExportSurface or ExportNonSurface get called
    u_SpecularHitT[pixelPos] = 0;           // it is common for this to be missing
}

void Bridge::ExportSurface(const PathState path, PathTracer::SurfaceData surfaceData, float sceneLength, float3 motionVectors )
{
    uint2 pixelPos = path.GetPixelPos();

    u_MotionVectors[pixelPos]   = float4(motionVectors, 0);

    const Ray cameraRay = Bridge::computeCameraRay( pixelPos );

    float3 virtualWorldPos = cameraRay.origin + cameraRay.dir * sceneLength;
    float4 clipPos = mul(float4(virtualWorldPos, 1), g_Const.view.matWorldToClip);
    u_Depth[pixelPos] = clipPos.z / clipPos.w;
    u_Throughput[pixelPos] = Pack_R11G11B10_FLOAT(saturate(path.GetThp()));
}

void Bridge::ExportNonSurface(const PathState path, float3 virtualWorldPos, float3 motionVectors )
{
    uint2 pixelPos = path.GetPixelPos();

    u_MotionVectors[pixelPos]   = float4(motionVectors, 0);

    float4 clipPos = mul(float4(virtualWorldPos, 1), g_Const.view.matWorldToClip);
    u_Depth[pixelPos] = clipPos.z / clipPos.w;
    u_Throughput[pixelPos] = 0;
}

void Bridge::ExportSpecHitTStart(const PathState path)
{
    u_SpecularHitT[path.GetPixelPos()] = -path.GetSceneLength();
}

void Bridge::ExportSpecHitTStop(const PathState path)
{
    uint2 pixelPos = path.GetPixelPos();

    float denoisingSceneLength = u_SpecularHitT[pixelPos];
    if (denoisingSceneLength < 0)  // 0 means not initialized - nothing to do here (shouldn't happen really!); >0 means we've already filled it up in previous pass when using multiple samples per pixel - again, nothing to do here
    {
        float specHitT = max( 0, path.GetSceneLength() + denoisingSceneLength );
        u_SpecularHitT[pixelPos] = specHitT;
    }
}

PathTracer::WorkingContext GetWorkingContext()
{
    PathTracer::WorkingContext ret;
    ret.PtConsts = g_Const.ptConsts;
    ret.Debug.Init( g_Const.debug, u_FeedbackBuffer, u_DebugLinesBuffer, u_DebugDeltaPathTree, u_DeltaPathSearchStack );
    ret.StablePlanes = StablePlanesContext::make(u_StablePlanesHeader, u_StablePlanesBuffer, u_StableRadiance, g_Const.ptConsts);
    ret.OutputColor = u_OutputColor;
    return ret;
}

#endif // __PATH_TRACER_BRIDGE_UNITY_HLSLI__
