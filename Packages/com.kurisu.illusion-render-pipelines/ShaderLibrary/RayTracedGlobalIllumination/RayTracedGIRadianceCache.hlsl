#ifndef ILLUSION_RAY_TRACED_GI_RADIANCE_CACHE_INCLUDED
#define ILLUSION_RAY_TRACED_GI_RADIANCE_CACHE_INCLUDED

// SHaRC's separate emissive mode keeps a surface's own emission out of its entry, so light sampling's MIS stays intact: an
// update path passes on the emission a hit adds to the vertex before it, and a query adds the hit's own emission.
#define SHARC_SEPARATE_EMISSIVE 1
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SHARC/include/SharcCommon.h"

// The defaults of SHaRC's integration guide.
static const float kRayTracedGICacheSceneScale = 50.0;
static const float kRayTracedGICacheRadianceScale = 1e3;
static const uint kRayTracedGICacheAccumulationFrameNum = 32;
static const uint kRayTracedGICacheStaleFrameNumMax = 64;

RWStructuredBuffer<uint64_t> u_RayTracedGICacheHashEntries;
RWStructuredBuffer<SharcAccumulationData> u_RayTracedGICacheAccumulation;
RWStructuredBuffer<SharcPackedData> u_RayTracedGICacheResolved;
float4 _RayTracedGICacheCameraPosition;
uint _RayTracedGICacheCapacity;

SharcParameters RayTracedGICacheParameters()
{
    SharcParameters parameters;
    parameters.hashGridParameters.cameraPosition = _RayTracedGICacheCameraPosition.xyz;
    parameters.hashGridParameters.logarithmBase = SHARC_GRID_LOGARITHM_BASE;
    parameters.hashGridParameters.sceneScale = kRayTracedGICacheSceneScale;
    parameters.hashGridParameters.levelBias = SHARC_GRID_LEVEL_BIAS;
    parameters.hashGridData.capacity = _RayTracedGICacheCapacity;
    parameters.hashGridData.hashEntriesBuffer = u_RayTracedGICacheHashEntries;
    parameters.radianceScale = kRayTracedGICacheRadianceScale;
    parameters.accumulationBuffer = u_RayTracedGICacheAccumulation;
    parameters.resolvedBuffer = u_RayTracedGICacheResolved;
    return parameters;
}

#if SHARC_QUERY
// As SHaRC's render pass does, a hit uses the cache when the segment that reached it is longer than a voxel's diagonal and the
// cone of the lobes that launched it, by SHaRC's accumulated roughness, is wider than a voxel.
bool RayTracedGIQueryCache(float3 position, float3 normal, float segmentLength, float pathRoughness, out float3 radiance)
{
    radiance = 0.0;
    const SharcParameters parameters = RayTracedGICacheParameters();
    float voxelSize = HashGridGetVoxelSize(HashGridGetLevel(position, parameters.hashGridParameters), parameters.hashGridParameters);
    float alpha = min(pathRoughness, 0.99);
    alpha *= alpha;
    float footprint = segmentLength * sqrt(0.5 * alpha * alpha / (1.0 - alpha * alpha));
    if (segmentLength <= voxelSize * sqrt(3.0) || footprint <= voxelSize)
        return false;
    SharcHitData hitData;
    hitData.positionWorld = position;
    hitData.normalWorld = normal;
    hitData.emissive = 0.0;
    return SharcGetCachedRadiance(parameters, hitData, radiance, false);
}
#endif

#endif
