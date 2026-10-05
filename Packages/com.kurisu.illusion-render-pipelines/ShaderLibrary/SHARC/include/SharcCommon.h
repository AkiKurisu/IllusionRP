/*
 * Copyright (c) 2023-2026, NVIDIA CORPORATION. All rights reserved.
 *
 * NVIDIA CORPORATION and its licensors retain all intellectual property
 * and proprietary rights in and to this software, related documentation
 * and any modifications thereto.  Any use, reproduction, disclosure or
 * distribution of this software and related documentation without an express
 * license agreement from NVIDIA CORPORATION is strictly prohibited.
 */

// Version
#define SHARC_VERSION_MAJOR                     1
#define SHARC_VERSION_MINOR                     8
#define SHARC_VERSION_BUILD                     3
#define SHARC_VERSION_REVISION                  0

// SHaRC usage overview
//
// SHaRC uses a world-space radiance cache to reduce path tracing cost. The
// integration typically consists of three render-time passes:
//
// 1. SHaRC Update
//    Sparse RT pass, compiled with SHARC_UPDATE=1, that traces a subset of
//    paths and writes newly observed radiance into the accumulation cache.
//    Call SharcInit() for each sampled path, SharcUpdateHit() on hits, and
//    SharcUpdateMiss() on misses. If SharcUpdateHit() returns false, the path
//    must be terminated early. Each path segment is treated independently:
//    after selecting the next ray, call SharcSetThroughput() with the segment
//    throughput, then reset path throughput to 1.0 for the next segment.
//
// 2. SHaRC Resolve
//    Compute pass that calls SharcResolveEntry() for each cache entry. This
//    combines per-frame accumulation data with previously resolved data,
//    updates temporal accumulation, handles stale entry eviction, and clears
//    accumulation data for the next frame.
//
// 3. SHaRC Render / Query
//    Main render RT pass, compiled with SHARC_QUERY=1, that traces normally but
//    queries the resolved cache on eligible non-primary hits using
//    SharcGetCachedRadiance(). On a successful query, cached radiance is used
//    and the path can terminate early.
//
// Required resources are hash entries, accumulation, and resolved buffers.
// All buffers must have the same number of entries and be zero-initialized.
// Proper UAV barriers are required between passes to guarantee correctness.
//
// HLSL shaders that include SHARC headers must be compiled with DXC
// -enable-16bit-types and, for DXIL, Shader Model 6.2 or newer.

// Constants
#define SHARC_ACCUMULATED_FRAME_NUM_BIT_OFFSET  0
#define SHARC_ACCUMULATED_FRAME_NUM_BIT_NUM     16
#define SHARC_ACCUMULATED_FRAME_NUM_BIT_MASK    ((1 << SHARC_ACCUMULATED_FRAME_NUM_BIT_NUM) - 1)
#define SHARC_STALE_FRAME_NUM_BIT_OFFSET        16
#define SHARC_STALE_FRAME_NUM_BIT_NUM           16
#define SHARC_STALE_FRAME_NUM_BIT_MASK          ((1 << SHARC_STALE_FRAME_NUM_BIT_NUM) - 1)
#define SHARC_GRID_LOGARITHM_BASE               2.0f
#define SHARC_ACCUMULATED_FRAME_NUM_MIN         1       // minimum number of frames to use for data accumulation
#define SHARC_ACCUMULATED_FRAME_NUM_MAX         1024    // maximum number of frames to use for data accumulation
#define SHARC_STALE_FRAME_NUM_MAX               1024    // maximum number of frames without new samples before the cache entry is evicted

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
#define SHARC_CACHE_INDEX_BIT_NUM               26
#define SHARC_CACHE_INDEX_BIT_MASK              ((1 << SHARC_CACHE_INDEX_BIT_NUM) - 1)
#define SHARC_RESPONSIVE_INDEX_OFFSET_BIT_NUM   6
#define SHARC_RESPONSIVE_INDEX_OFFSET_BIT_MASK  ((1 << SHARC_RESPONSIVE_INDEX_OFFSET_BIT_NUM) - 1)
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

// Tweakable parameters
#ifndef SHARC_SAMPLE_NUM_THRESHOLD
#define SHARC_SAMPLE_NUM_THRESHOLD              0       // elements with sample count above this threshold will be used for early-out/resampling
#endif

#ifndef SHARC_SEPARATE_EMISSIVE
#define SHARC_SEPARATE_EMISSIVE                 0       // if enabled, emissive values must be provided separately during updates. For cache queries, you can either supply them directly or include them in the query result
#endif

#ifndef SHARC_MATERIAL_DEMODULATION
#define SHARC_MATERIAL_DEMODULATION             0       // enable material demodulation to preserve material details; requires sampling material data to reconstruct shading from cached values
#endif

#ifndef SHARC_ENABLE_SH_ENCODING
#define SHARC_ENABLE_SH_ENCODING                0       // store resolved radiance as mixed YCoCg SH2 data: Y L0/L1 plus Co/Cg L0
#endif

#ifndef SHARC_LINEAR_PROBE_WINDOW_SIZE
#define SHARC_LINEAR_PROBE_WINDOW_SIZE          8       // size of the linear search window for probe lookups
#endif

#ifndef SHARC_ENABLE_CACHE_RESAMPLING
#define SHARC_ENABLE_CACHE_RESAMPLING           SHARC_UPDATE // resamples the cache during update step
#endif

#ifndef SHARC_PROPAGATION_DEPTH
#if SHARC_ENABLE_CACHE_RESAMPLING
#define SHARC_PROPAGATION_DEPTH                 2       // controls the amount of vertices stored in memory for signal backpropagation with cache resampling
#else // !SHARC_ENABLE_CACHE_RESAMPLING
#define SHARC_PROPAGATION_DEPTH                 4       // controls the amount of vertices stored in memory for signal backpropagation
#endif // SHARC_ENABLE_CACHE_RESAMPLING
#endif

#ifndef SHARC_BLEND_ADJACENT_LEVELS
#define SHARC_BLEND_ADJACENT_LEVELS             1       // combine the data from adjacent levels on camera movement
#endif

#ifndef SHARC_ENABLE_FADE_ACCELERATION
#define SHARC_ENABLE_FADE_ACCELERATION          0       // boost accumulator convergence when signal amplitude is fading
#endif

#ifndef SHARC_RESAMPLING_DEPTH_MIN
#define SHARC_RESAMPLING_DEPTH_MIN              1       // controls minimum path depth which can be used with cache resampling
#endif

#ifndef SHARC_STALE_FRAME_NUM_MIN
#define SHARC_STALE_FRAME_NUM_MIN               8       // minimum number of frames to keep the element in the cache
#endif

#ifndef SHARC_GRID_LEVEL_BIAS
#define SHARC_GRID_LEVEL_BIAS                   0       // LOD bias - positive adds extra magnified levels, negative reduces levels
#endif

#ifndef SHARC_USE_FP16
#define SHARC_USE_FP16                          0       // use native fp16 for sample weights storage; HLSL requires DXC -enable-16bit-types
#endif

#ifndef SHARC_RESPONSIVE_ENTRY_PROBE_RANGE
#define SHARC_RESPONSIVE_ENTRY_PROBE_RANGE      16      // maximum depth for responsive entry search, deeper search is not justified
#endif

#ifndef HASH_GRID_COMPACT
#define HASH_GRID_COMPACT                       0
#endif

#ifndef HASH_GRID_LIMIT_EMPTY_SLOTS
#define HASH_GRID_LIMIT_EMPTY_SLOTS             2
#endif

#ifndef RW_STRUCTURED_BUFFER
#define RW_STRUCTURED_BUFFER(name, type)        RWStructuredBuffer<type> name
#endif

#ifndef BUFFER_AT_OFFSET
#define BUFFER_AT_OFFSET(name, offset)          name[offset]
#endif

#if SHARC_USE_FP16
#define SharcSampleWeight float16_t3
#else // !SHARC_USE_FP16
#define SharcSampleWeight float3
#endif // SHARC_USE_FP16

#if HASH_GRID_COMPACT
#define HashGridKey uint
#else // !HASH_GRID_COMPACT
#define HashGridKey uint64_t
#endif // !HASH_GRID_COMPACT
#define HASH_GRID_KEY_TYPE HashGridKey
#define HASH_GRID_PREFIX HashGrid
#define HASH_GRID_CONST_PREFIX HASH_GRID
#include "HashGridTypes.h"
#include "HashGridCommon.h"
#undef HASH_GRID_KEY_TYPE
#undef HASH_GRID_PREFIX
#undef HASH_GRID_CONST_PREFIX

// Error checks
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
#if SHARC_RESPONSIVE_ENTRY_PROBE_RANGE < 1
#error SHARC_RESPONSIVE_ENTRY_PROBE_RANGE must be at least 1
#endif

#if SHARC_RESPONSIVE_ENTRY_PROBE_RANGE > HASH_GRID_HASH_MAP_BUCKET_SIZE
#error SHARC_RESPONSIVE_ENTRY_PROBE_RANGE must not exceed HASH_GRID_HASH_MAP_BUCKET_SIZE
#endif

#if HASH_GRID_COMPACT
#error compact hash grid currently doesn't work with responsive lighting
#endif
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

#include "SharcTypes.h"

struct SharcParameters
{
    HashGridParameters hashGridParameters;
    HashGridData hashGridData;
    float radianceScale;            // quantization factor for atomic radiance accumulation (u32 per channel during SHARC_UPDATE). Start with 1e3f; reduce for large radiance values to prevent overflow

    RW_STRUCTURED_BUFFER(accumulationBuffer, SharcAccumulationData);
    RW_STRUCTURED_BUFFER(resolvedBuffer, SharcPackedData);
};

struct SharcState
{
#if SHARC_UPDATE
    HashGridIndex cacheIndices[SHARC_PROPAGATION_DEPTH];
    SharcSampleWeight sampleWeights[SHARC_PROPAGATION_DEPTH];
#if SHARC_ENABLE_SH_ENCODING
    float3 radianceDirections[SHARC_PROPAGATION_DEPTH];
    float radianceDirectionWeights[SHARC_PROPAGATION_DEPTH];
#endif // SHARC_ENABLE_SH_ENCODING
    uint pathLength;
 #else // !SHARC_UPDATE
    uint placeholder;               // prevents empty-struct compilation issues with GLSL
#endif // SHARC_UPDATE
};

struct SharcHitData
{
    float3 positionWorld;
    float3 normalWorld;             // geometry normal in world space. Shading or object-space normals should work, but are not generally recommended
#if SHARC_ENABLE_SH_ENCODING
    float3 radianceDirectionWorld;  // normalized outgoing direction for cached radiance, pointing from the hit toward the previous path vertex
    float radianceDirectionWeight;  // 0 for diffuse radiance, 1 for strongly directional glossy/specular radiance
#endif // SHARC_ENABLE_SH_ENCODING
#if SHARC_MATERIAL_DEMODULATION
    float3 materialDemodulation;    // demodulation factor used to preserve material details. Use > 0 when active; set to float3(1.0f, 1.0f, 1.0f) when unused
#endif // SHARC_MATERIAL_DEMODULATION
#if SHARC_SEPARATE_EMISSIVE
    float3 emissive;                // separate emissive improves behavior with dynamic lighting. Requires computing material emissive on each(even cached) hit
#endif // SHARC_SEPARATE_EMISSIVE
};

struct SharcRadianceData
{
#if SHARC_ENABLE_SH_ENCODING
    float4 luminanceSH;             // xyz - L1, w - L0
    float2 chromaL0;                // Co/Cg L0 in YCoCg color space
#else // !SHARC_ENABLE_SH_ENCODING
    float3 radiance;
#endif // SHARC_ENABLE_SH_ENCODING
};

float3 SharcGetRadianceDirection(SharcHitData sharcHitData)
{
#if SHARC_ENABLE_SH_ENCODING
    return sharcHitData.radianceDirectionWorld;
#else // !SHARC_ENABLE_SH_ENCODING
    return float3(0.0f, 0.0f, 1.0f);
#endif // SHARC_ENABLE_SH_ENCODING
}

float SharcGetRadianceDirectionWeight(SharcHitData sharcHitData)
{
#if SHARC_ENABLE_SH_ENCODING
    return saturate(sharcHitData.radianceDirectionWeight);
#else // !SHARC_ENABLE_SH_ENCODING
    return 0.0f;
#endif // SHARC_ENABLE_SH_ENCODING
}

struct SharcVoxelData
{
    SharcRadianceData accumulatedRadiance;
    float accumulatedSampleNum;
    uint accumulatedFrameNum;
    uint staleFrameNum;
    uint sampleDataExt;
};

struct SharcResolveParameters
{
    float3 cameraPositionPrev;      // previous camera position
    uint accumulationFrameNum;      // maximum number of frames for the temporal accumulation window
    uint responsiveFrameNum;        // maximum number of frames for the temporal accumulation window used with responsive signal
    uint staleFrameNumMax;          // maximum number of frames without new samples before the cache entry is evicted
    uint frameIndex;
};

SharcRadianceData SharcZeroRadianceData()
{
    SharcRadianceData radianceData;
#if SHARC_ENABLE_SH_ENCODING
    radianceData.luminanceSH = float4(0, 0, 0, 0);
    radianceData.chromaL0 = float2(0, 0);
#else // !SHARC_ENABLE_SH_ENCODING
    radianceData.radiance = float3(0, 0, 0);
#endif // SHARC_ENABLE_SH_ENCODING

    return radianceData;
}

SharcRadianceData SharcAddRadianceData(SharcRadianceData a, SharcRadianceData b)
{
    SharcRadianceData result;
#if SHARC_ENABLE_SH_ENCODING
    result.luminanceSH = a.luminanceSH + b.luminanceSH;
    result.chromaL0 = a.chromaL0 + b.chromaL0;
#else // !SHARC_ENABLE_SH_ENCODING
    result.radiance = a.radiance + b.radiance;
#endif // SHARC_ENABLE_SH_ENCODING

    return result;
}

SharcRadianceData SharcScaleRadianceData(SharcRadianceData radianceData, float scale)
{
    SharcRadianceData result;
#if SHARC_ENABLE_SH_ENCODING
    result.luminanceSH = radianceData.luminanceSH * scale;
    result.chromaL0 = radianceData.chromaL0 * scale;
#else // !SHARC_ENABLE_SH_ENCODING
    result.radiance = radianceData.radiance * scale;
#endif // SHARC_ENABLE_SH_ENCODING

    return result;
}

float3 SharcRGBToYCoCg(float3 color)
{
    return float3(0.25f * (color.x + 2.0f * color.y + color.z), color.x - color.z, color.y - 0.5f * (color.x + color.z));
}

float3 SharcYCoCgToRGB(float3 color)
{
    return float3(color.x + 0.5f * (color.y - color.z), color.x + 0.5f * color.z, color.x - 0.5f * (color.y + color.z));
}

SharcRadianceData SharcEncodeRadiance(float3 radiance, float3 radianceDirection, float radianceDirectionWeight)
{
    SharcRadianceData radianceData;
#if SHARC_ENABLE_SH_ENCODING
    float3 direction = normalize(radianceDirection);
    float3 ycocg = SharcRGBToYCoCg(radiance);
    radianceData.luminanceSH = float4(ycocg.x * direction * saturate(radianceDirectionWeight), ycocg.x);
    radianceData.chromaL0 = ycocg.yz;
#else // !SHARC_ENABLE_SH_ENCODING
    radianceData.radiance = radiance;
#endif // SHARC_ENABLE_SH_ENCODING

    return radianceData;
}

float3 SharcDecodeRadiance(SharcRadianceData radianceData, float3 radianceDirection)
{
#if SHARC_ENABLE_SH_ENCODING
    float3 direction = normalize(radianceDirection);
    float directionalLuminance = min(length(radianceData.luminanceSH.xyz), max(radianceData.luminanceSH.w, 0.0f));
    float diffuseLuminance = max(radianceData.luminanceSH.w - directionalLuminance, 0.0f);
    float luminance = diffuseLuminance + max(dot(radianceData.luminanceSH.xyz, direction), 0.0f);
    float chromaScale = (radianceData.luminanceSH.w > 1e-6f) ? (luminance / radianceData.luminanceSH.w) : 0.0f;
    float2 chromaL0 = radianceData.chromaL0 * chromaScale;
    return max(SharcYCoCgToRGB(float3(luminance, chromaL0.x, chromaL0.y)), float3(0.0f, 0.0f, 0.0f));
#else // !SHARC_ENABLE_SH_ENCODING
    return radianceData.radiance;
#endif // SHARC_ENABLE_SH_ENCODING
}

uint SharcPackFloat16(float value)
{
    return f32tof16(value) & 0xFFFFu;
}

float SharcUnpackFloat16(uint value)
{
    return f16tof32(value & 0xFFFFu);
}

uint SharcPackFloat16Pair(float2 value)
{
    return SharcPackFloat16(value.x) | (SharcPackFloat16(value.y) << 16);
}

float2 SharcUnpackFloat16Pair(uint value)
{
    return float2(SharcUnpackFloat16(value), SharcUnpackFloat16(value >> 16));
}

SharcPackedData SharcZeroPackedData()
{
    SharcPackedData packedData;
    packedData.radianceData = float16_t4(0, 0, 0, 0);
#if SHARC_ENABLE_SH_ENCODING
    packedData.radianceDataExt = 0;
    packedData.sampleNumData = 0;
#endif // SHARC_ENABLE_SH_ENCODING
    packedData.sampleData = 0;
    packedData.sampleDataExt = 0;

    return packedData;
}

SharcAccumulationData SharcZeroAccumulationData()
{
    SharcAccumulationData accumulatedData;
#if SHARC_ENABLE_SH_ENCODING
    accumulatedData.data = int4(0, 0, 0, 0);
    accumulatedData.dataExt = int4(0, 0, 0, 0);
#else // !SHARC_ENABLE_SH_ENCODING
    accumulatedData.data = uint4(0, 0, 0, 0);
#endif // SHARC_ENABLE_SH_ENCODING

    return accumulatedData;
}

float SharcGetAccumulatedSampleNum(SharcAccumulationData accumulatedData)
{
#if SHARC_ENABLE_SH_ENCODING
    return float(accumulatedData.dataExt.z);
#else // !SHARC_ENABLE_SH_ENCODING
    return float(accumulatedData.data.w);
#endif // SHARC_ENABLE_SH_ENCODING
}

SharcRadianceData SharcGetAccumulatedRadianceData(SharcAccumulationData accumulatedData, float radianceScale, float sampleNum)
{
    SharcRadianceData radianceData;
    float scale = rcp(radianceScale * max(sampleNum, 1e-6f));
#if SHARC_ENABLE_SH_ENCODING
    radianceData.luminanceSH = float4(accumulatedData.data) * scale;
    radianceData.chromaL0 = float2(accumulatedData.dataExt.xy) * scale;
#else // !SHARC_ENABLE_SH_ENCODING
    radianceData.radiance = float3(accumulatedData.data.xyz) * scale;
#endif // SHARC_ENABLE_SH_ENCODING

    return radianceData;
}

SharcPackedData SharcPackVoxelData(SharcRadianceData radianceData, float sampleNum, uint accumulatedFrameNum, uint staleFrameNum, uint sampleDataExt)
{
    const float float16Max = 65504.0f;

    SharcPackedData packedData;
#if SHARC_ENABLE_SH_ENCODING
    packedData.radianceData = float16_t4(clamp(radianceData.luminanceSH, float4(-float16Max, -float16Max, -float16Max, -float16Max), float4(float16Max, float16Max, float16Max, float16Max)));
    packedData.radianceDataExt = SharcPackFloat16Pair(clamp(radianceData.chromaL0, float2(-float16Max, -float16Max), float2(float16Max, float16Max)));
    packedData.sampleNumData = SharcPackFloat16(min(sampleNum, float16Max));
#else // !SHARC_ENABLE_SH_ENCODING
    packedData.radianceData = float16_t4(min(float4(radianceData.radiance, sampleNum), float4(float16Max, float16Max, float16Max, float16Max)));
#endif // SHARC_ENABLE_SH_ENCODING
    packedData.sampleData = accumulatedFrameNum | (staleFrameNum << SHARC_STALE_FRAME_NUM_BIT_OFFSET);
    packedData.sampleDataExt = sampleDataExt;

    return packedData;
}

SharcVoxelData SharcUnpackVoxelData(SharcPackedData packedData)
{
    SharcVoxelData voxelData;
#if SHARC_ENABLE_SH_ENCODING
    voxelData.accumulatedRadiance.luminanceSH = float4(packedData.radianceData);
    voxelData.accumulatedRadiance.chromaL0 = SharcUnpackFloat16Pair(packedData.radianceDataExt);
    voxelData.accumulatedSampleNum = SharcUnpackFloat16(packedData.sampleNumData);
#else // !SHARC_ENABLE_SH_ENCODING
    float4 radianceAndSampleNum = float4(packedData.radianceData);
    voxelData.accumulatedRadiance.radiance = radianceAndSampleNum.xyz;
    voxelData.accumulatedSampleNum = radianceAndSampleNum.w;
#endif // SHARC_ENABLE_SH_ENCODING
    voxelData.accumulatedFrameNum = (packedData.sampleData >> SHARC_ACCUMULATED_FRAME_NUM_BIT_OFFSET) & SHARC_ACCUMULATED_FRAME_NUM_BIT_MASK;
    voxelData.staleFrameNum = (packedData.sampleData >> SHARC_STALE_FRAME_NUM_BIT_OFFSET) & SHARC_STALE_FRAME_NUM_BIT_MASK;
    voxelData.sampleDataExt = packedData.sampleDataExt;

    return voxelData;
}

SharcVoxelData SharcGetVoxelData(RW_STRUCTURED_BUFFER(voxelDataBuffer, SharcPackedData), HashGridIndex hashGridIndex)
{
    SharcPackedData packedData = BUFFER_AT_OFFSET(voxelDataBuffer, hashGridIndex);

    return SharcUnpackVoxelData(packedData);
}

float SharcLuma(float3 color)
{
    const float3 luma = float3(0.213f, 0.715f, 0.072f);

    return dot(color, luma);
}

float SharcRadianceLuma(SharcRadianceData radianceData)
{
#if SHARC_ENABLE_SH_ENCODING
    return max(radianceData.luminanceSH.w, 0.0f);
#else // !SHARC_ENABLE_SH_ENCODING
    return SharcLuma(radianceData.radiance);
#endif // SHARC_ENABLE_SH_ENCODING
}

void SharcAddVoxelData(in SharcParameters sharcParameters, HashGridIndex hashGridIndex, float3 sampleValue, float3 sampleWeight, float3 sampleDirection, float sampleDirectionWeight, uint sampleData)
{
#if SHARC_ENABLE_SH_ENCODING
    SharcRadianceData scaledSample = SharcEncodeRadiance(sampleValue * sampleWeight, sampleDirection, sampleDirectionWeight);
    int4 scaledLuminanceSH = int4(scaledSample.luminanceSH * sharcParameters.radianceScale);
    int2 scaledChromaL0 = int2(scaledSample.chromaL0 * sharcParameters.radianceScale);

    if (scaledLuminanceSH.x != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.x, scaledLuminanceSH.x);
    if (scaledLuminanceSH.y != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.y, scaledLuminanceSH.y);
    if (scaledLuminanceSH.z != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.z, scaledLuminanceSH.z);
    if (scaledLuminanceSH.w != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.w, scaledLuminanceSH.w);
    if (scaledChromaL0.x != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).dataExt.x, scaledChromaL0.x);
    if (scaledChromaL0.y != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).dataExt.y, scaledChromaL0.y);
    if (sampleData != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).dataExt.z, int(sampleData));
#else // !SHARC_ENABLE_SH_ENCODING
    uint3 scaledRadiance = uint3(sampleValue * sampleWeight * sharcParameters.radianceScale);

    if (scaledRadiance.x != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.x, scaledRadiance.x);
    if (scaledRadiance.y != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.y, scaledRadiance.y);
    if (scaledRadiance.z != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.z, scaledRadiance.z);
    if (sampleData != 0) InterlockedAdd(BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, hashGridIndex).data.w, sampleData);
#endif // SHARC_ENABLE_SH_ENCODING
}

void SharcInit(inout SharcState sharcState)
{
#if SHARC_UPDATE
    sharcState.pathLength = 0;
#endif // SHARC_UPDATE
}

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
uint SharcPackCacheIndexWithOffset(uint baseIndex, int offset)
{
    uint offsetBits = (uint(offset)) & SHARC_RESPONSIVE_INDEX_OFFSET_BIT_MASK;

    return (baseIndex & SHARC_CACHE_INDEX_BIT_MASK) | (offsetBits << SHARC_CACHE_INDEX_BIT_NUM);
}

int SharcUnpackOffset(uint packedIndex)
{
    return (int(packedIndex)) >> SHARC_CACHE_INDEX_BIT_NUM;
}

bool SharcGetResponsiveIndexOffset(in SharcParameters sharcParameters, HashGridIndex hashGridIndex, out int responsiveIndexOffset, out bool isNewSample)
{
    responsiveIndexOffset = SharcUnpackOffset(hashGridIndex);
    isNewSample = false;
    if (responsiveIndexOffset == 0)
    {
        HashGridKey hashGridKey = BUFFER_AT_OFFSET(sharcParameters.hashGridData.hashEntriesBuffer, hashGridIndex);
        uint baseSlot = HashGridGetBaseSlot(hashGridKey, sharcParameters.hashGridData.capacity);
        hashGridKey |= HashGridKey(1) << (HASH_GRID_KEY_BIT_NUM - 1);
        HashGridIndex responsiveIndex;
        uint bucketOffset;
        if (!HashGridInsert(sharcParameters.hashGridData, hashGridKey, baseSlot, SHARC_RESPONSIVE_ENTRY_PROBE_RANGE, responsiveIndex, bucketOffset))
            return false;

        responsiveIndexOffset = int(responsiveIndex) - int(hashGridIndex);
        isNewSample = true;
    }

    return true;
}
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

void SharcUpdateMiss(in SharcParameters sharcParameters, in SharcState sharcState, float3 radiance
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
    , bool isResponsiveLighting
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING
    )
{
#if SHARC_UPDATE
    for (int i = 0; i < sharcState.pathLength; ++i)
    {
        HashGridIndex hashGridIndex = sharcState.cacheIndices[i];
        bool isNewSample = false;

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
        if (isResponsiveLighting)
        {
            int responsiveIndexOffset;
            if (!SharcGetResponsiveIndexOffset(sharcParameters, sharcState.cacheIndices[i], responsiveIndexOffset, isNewSample))
                return;

            hashGridIndex += responsiveIndexOffset;
        }

        hashGridIndex &= SHARC_CACHE_INDEX_BIT_MASK;
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING
#if SHARC_ENABLE_SH_ENCODING
        float3 radianceDirection = sharcState.radianceDirections[i];
        float radianceDirectionWeight = sharcState.radianceDirectionWeights[i];
#else // !SHARC_ENABLE_SH_ENCODING
        float3 radianceDirection = float3(0.0f, 0.0f, 1.0f);
        float radianceDirectionWeight = 0.0f;
#endif // SHARC_ENABLE_SH_ENCODING
        SharcAddVoxelData(sharcParameters, hashGridIndex, radiance, sharcState.sampleWeights[i], radianceDirection, radianceDirectionWeight, isNewSample ? 1 : 0);
    }
#endif // SHARC_UPDATE
}

bool SharcUpdateHit(in SharcParameters sharcParameters, inout SharcState sharcState, SharcHitData sharcHitData, float3 directLighting, float random
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
    , bool isResponsiveLighting
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING
    )
{
    bool continueTracing = true;
#if SHARC_UPDATE
    HashGridKey hashGridKey;
    HashGridIndex hashGridIndex;
    if (!HashGridInsertEntry(sharcParameters.hashGridData, sharcHitData.positionWorld, sharcHitData.normalWorld, sharcParameters.hashGridParameters, hashGridKey, hashGridIndex))
        return false;

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
    HashGridIndex responsiveCacheIndex = HASH_GRID_INVALID_CACHE_INDEX;
    if (isResponsiveLighting)
    {
        uint baseSlot = HashGridGetBaseSlot(hashGridKey, sharcParameters.hashGridData.capacity);
        hashGridKey |= HashGridKey(1) << (HASH_GRID_KEY_BIT_NUM - 1);
        uint bucketOffset;
        if (!HashGridInsert(sharcParameters.hashGridData, hashGridKey, baseSlot, SHARC_RESPONSIVE_ENTRY_PROBE_RANGE, responsiveCacheIndex, bucketOffset))
            return false;
    }
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

    float3 sharcRadiance = directLighting;
    float3 materialDemodulation = float3(1.0f, 1.0f, 1.0f);
    float3 sharcRadianceDirection = SharcGetRadianceDirection(sharcHitData);
    float sharcRadianceDirectionWeight = SharcGetRadianceDirectionWeight(sharcHitData);
#if SHARC_MATERIAL_DEMODULATION
    materialDemodulation = sharcHitData.materialDemodulation;
#endif // SHARC_MATERIAL_DEMODULATION
    float3 inverseMaterialDemodulation = rcp(materialDemodulation);

#if SHARC_ENABLE_CACHE_RESAMPLING
    uint resamplingDepth = uint(round(lerp(SHARC_RESAMPLING_DEPTH_MIN, SHARC_PROPAGATION_DEPTH, random)));
    if (resamplingDepth <= sharcState.pathLength)
    {
        SharcVoxelData voxelData = SharcGetVoxelData(sharcParameters.resolvedBuffer, hashGridIndex);
        if (voxelData.accumulatedSampleNum > SHARC_SAMPLE_NUM_THRESHOLD)
        {
            sharcRadiance = SharcDecodeRadiance(voxelData.accumulatedRadiance, sharcRadianceDirection);
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
            if (responsiveCacheIndex != HASH_GRID_INVALID_CACHE_INDEX)
            {
                SharcVoxelData responsiveVoxelData = SharcGetVoxelData(sharcParameters.resolvedBuffer, responsiveCacheIndex);
                if (responsiveVoxelData.accumulatedSampleNum > SHARC_SAMPLE_NUM_THRESHOLD)
                    sharcRadiance += SharcDecodeRadiance(responsiveVoxelData.accumulatedRadiance, sharcRadianceDirection);
            }
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING
            sharcRadiance *= materialDemodulation;
            continueTracing = false;
        }
    }
#endif // SHARC_ENABLE_CACHE_RESAMPLING

    if (continueTracing)
    {
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
        if (responsiveCacheIndex != HASH_GRID_INVALID_CACHE_INDEX)
        {
            SharcAddVoxelData(sharcParameters, responsiveCacheIndex, directLighting * inverseMaterialDemodulation, float3(1.0f, 1.0f, 1.0f), sharcRadianceDirection, sharcRadianceDirectionWeight, 1);
            directLighting = float3(0.0f, 0.0f, 0.0f); // avoid adding the direct lighting contribution twice
        }
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING
        SharcAddVoxelData(sharcParameters, hashGridIndex, directLighting * inverseMaterialDemodulation, float3(1.0f, 1.0f, 1.0f), sharcRadianceDirection, sharcRadianceDirectionWeight, 1);
    }

#if SHARC_SEPARATE_EMISSIVE
    sharcRadiance += sharcHitData.emissive;
#endif // SHARC_SEPARATE_EMISSIVE

    uint i;
    for (i = 0; i < sharcState.pathLength; ++i)
    {
        HashGridIndex tempHashGridIndex = sharcState.cacheIndices[i];
        if (tempHashGridIndex == HASH_GRID_INVALID_CACHE_INDEX)
            continue;

        bool isNewSample = false;
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
        if (responsiveCacheIndex != HASH_GRID_INVALID_CACHE_INDEX)
        {
            int responsiveIndexOffset;
            if (!SharcGetResponsiveIndexOffset(sharcParameters, sharcState.cacheIndices[i], responsiveIndexOffset, isNewSample))
                return false;

            tempHashGridIndex += responsiveIndexOffset;

            if (isNewSample)
                sharcState.cacheIndices[i] = SharcPackCacheIndexWithOffset(sharcState.cacheIndices[i], responsiveIndexOffset);
        }

        tempHashGridIndex &= SHARC_CACHE_INDEX_BIT_MASK;
        isNewSample = isNewSample && isResponsiveLighting;
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING
#if SHARC_ENABLE_SH_ENCODING
        float3 radianceDirection = sharcState.radianceDirections[i];
        float radianceDirectionWeight = sharcState.radianceDirectionWeights[i];
#else // !SHARC_ENABLE_SH_ENCODING
        float3 radianceDirection = float3(0.0f, 0.0f, 1.0f);
        float radianceDirectionWeight = 0.0f;
#endif // SHARC_ENABLE_SH_ENCODING
        SharcAddVoxelData(sharcParameters, tempHashGridIndex, sharcRadiance, sharcState.sampleWeights[i], radianceDirection, radianceDirectionWeight, isNewSample ? 1 : 0);
    }

    for (i = min(sharcState.pathLength, SHARC_PROPAGATION_DEPTH - 1); i > 0; --i)
    {
        sharcState.cacheIndices[i] = sharcState.cacheIndices[i - 1];
        sharcState.sampleWeights[i] = sharcState.sampleWeights[i - 1];
#if SHARC_ENABLE_SH_ENCODING
        sharcState.radianceDirections[i] = sharcState.radianceDirections[i - 1];
        sharcState.radianceDirectionWeights[i] = sharcState.radianceDirectionWeights[i - 1];
#endif // SHARC_ENABLE_SH_ENCODING
    }

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
    if (responsiveCacheIndex != HASH_GRID_INVALID_CACHE_INDEX)
    {
        int responsiveIndexOffset = int(responsiveCacheIndex) - int(hashGridIndex);
        hashGridIndex = SharcPackCacheIndexWithOffset(hashGridIndex, responsiveIndexOffset);
    }
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

    sharcState.cacheIndices[0] = hashGridIndex;
    sharcState.sampleWeights[0] = SharcSampleWeight(inverseMaterialDemodulation);
#if SHARC_ENABLE_SH_ENCODING
    sharcState.radianceDirections[0] = sharcRadianceDirection;
    sharcState.radianceDirectionWeights[0] = sharcRadianceDirectionWeight;
#endif // SHARC_ENABLE_SH_ENCODING
    sharcState.pathLength = min(++sharcState.pathLength, SHARC_PROPAGATION_DEPTH);
#endif // SHARC_UPDATE
    return continueTracing;
}

void SharcSetThroughput(inout SharcState sharcState, float3 throughput)
{
#if SHARC_UPDATE
    for (uint i = 0; i < sharcState.pathLength; ++i)
        sharcState.sampleWeights[i] *= SharcSampleWeight(throughput);
#endif // SHARC_UPDATE
}

void SharcSetRadianceDirectionWeight(inout SharcState sharcState, float radianceDirectionWeight)
{
#if SHARC_UPDATE && SHARC_ENABLE_SH_ENCODING
    if (sharcState.pathLength > 0)
        sharcState.radianceDirectionWeights[0] = saturate(radianceDirectionWeight);
#endif // SHARC_UPDATE && SHARC_ENABLE_SH_ENCODING
}

bool SharcGetCachedRadianceFromHash(in SharcParameters sharcParameters, in SharcHitData sharcHitData, HashGridKey hashGridKey, out float3 radiance, bool skipResponsiveLighting)
{
    HashGridIndex hashGridIndex;
    uint baseSlot = HashGridGetBaseSlot(hashGridKey, sharcParameters.hashGridData.capacity);
    uint bucketOffset;
    if (!HashGridFind(sharcParameters.hashGridData, hashGridKey, baseSlot, HASH_GRID_HASH_MAP_BUCKET_SIZE, hashGridIndex, bucketOffset))
        return false;

    SharcVoxelData voxelData = SharcGetVoxelData(sharcParameters.resolvedBuffer, hashGridIndex);
    if (voxelData.accumulatedSampleNum > SHARC_SAMPLE_NUM_THRESHOLD)
    {
        float3 sharcRadianceDirection = SharcGetRadianceDirection(sharcHitData);
        radiance = SharcDecodeRadiance(voxelData.accumulatedRadiance, sharcRadianceDirection);

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
        uint temp;
        hashGridKey |= HashGridKey(1) << (HASH_GRID_KEY_BIT_NUM - 1);
        if (!skipResponsiveLighting && HashGridFind(sharcParameters.hashGridData, hashGridKey, baseSlot, SHARC_RESPONSIVE_ENTRY_PROBE_RANGE, hashGridIndex, temp))
        {
            SharcVoxelData responsiveVoxelData = SharcGetVoxelData(sharcParameters.resolvedBuffer, hashGridIndex);
            if (responsiveVoxelData.accumulatedSampleNum > SHARC_SAMPLE_NUM_THRESHOLD)
                radiance += SharcDecodeRadiance(responsiveVoxelData.accumulatedRadiance, sharcRadianceDirection);
        }
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

#if SHARC_MATERIAL_DEMODULATION
        radiance *= sharcHitData.materialDemodulation;
#endif // SHARC_MATERIAL_DEMODULATION
#if SHARC_SEPARATE_EMISSIVE
        radiance += sharcHitData.emissive;
#endif // SHARC_SEPARATE_EMISSIVE

        return true;
    }

    return false;
}

bool SharcGetCachedRadiance(in SharcParameters sharcParameters, in SharcHitData sharcHitData, out float3 radiance, bool skipResponsiveLighting)
{
    HashGridKey hashGridKey = HashGridComputeSpatialHash(sharcHitData.positionWorld, sharcHitData.normalWorld, sharcParameters.hashGridParameters);
    return SharcGetCachedRadianceFromHash(sharcParameters, sharcHitData, hashGridKey, radiance, skipResponsiveLighting);
}

int SharcGetGridDistance2(int3 position)
{
    return position.x * position.x + position.y * position.y + position.z * position.z;
}

HashGridKey SharcGetAdjacentLevelHashKey(HashGridKey hashGridKey, HashGridParameters gridParameters, float3 cameraPositionPrev)
{
    int3 gridPosition;
    gridPosition.x = int((hashGridKey >> HASH_GRID_POSITION_BIT_NUM * 0) & HASH_GRID_POSITION_BIT_MASK);
    gridPosition.y = int((hashGridKey >> HASH_GRID_POSITION_BIT_NUM * 1) & HASH_GRID_POSITION_BIT_MASK);
    gridPosition.z = int((hashGridKey >> HASH_GRID_POSITION_BIT_NUM * 2) & HASH_GRID_POSITION_BIT_MASK);

    // Sign-extend packed coordinates without divergent branches.
    gridPosition = (gridPosition << (32 - HASH_GRID_POSITION_BIT_NUM)) >> (32 - HASH_GRID_POSITION_BIT_NUM);

    int level = int((hashGridKey >> HASH_GRID_LEVEL_BIT_OFFSET) & HASH_GRID_LEVEL_BIT_MASK);

    float voxelSize = HashGridGetVoxelSize(uint(level), gridParameters);
    float inverseVoxelSize = rcp(voxelSize);
    int3 cameraGridPosition = int3(floor(gridParameters.cameraPosition * inverseVoxelSize));
    int3 cameraVector = cameraGridPosition - gridPosition;
    int cameraDistance = SharcGetGridDistance2(cameraVector);

    int3 cameraGridPositionPrev = int3(floor(cameraPositionPrev * inverseVoxelSize));
    int3 cameraVectorPrev = cameraGridPositionPrev - gridPosition;
    int cameraDistancePrev = SharcGetGridDistance2(cameraVectorPrev);

    if (cameraDistance < cameraDistancePrev)
    {
        gridPosition = int3(floor(gridPosition / gridParameters.logarithmBase));
        level = min(level + 1, int(HASH_GRID_LEVEL_BIT_MASK));
    }
    else // this may be inaccurate
    {
        gridPosition = int3(floor(gridPosition * gridParameters.logarithmBase));
        level = max(level - 1, 1);
    }

    HashGridKey modifiedHashGridKey = ((HashGridKey(gridPosition.x) & HASH_GRID_POSITION_BIT_MASK) << (HASH_GRID_POSITION_BIT_NUM * 0))
        | ((HashGridKey(gridPosition.y) & HASH_GRID_POSITION_BIT_MASK) << (HASH_GRID_POSITION_BIT_NUM * 1))
        | ((HashGridKey(gridPosition.z) & HASH_GRID_POSITION_BIT_MASK) << (HASH_GRID_POSITION_BIT_NUM * 2))
        | ((HashGridKey(level) & HASH_GRID_LEVEL_BIT_MASK) << HASH_GRID_LEVEL_BIT_OFFSET);

#if HASH_GRID_USE_NORMALS
    modifiedHashGridKey |= hashGridKey & (HashGridKey(HASH_GRID_NORMAL_BIT_MASK) << HASH_GRID_NORMAL_BIT_OFFSET);
#endif // HASH_GRID_USE_NORMALS

    return modifiedHashGridKey;
}

void SharcResolveEntry(uint entryIndex, SharcParameters sharcParameters, SharcResolveParameters resolveParameters)
{
    if (entryIndex >= sharcParameters.hashGridData.capacity)
        return;

    HashGridKey hashGridKey = BUFFER_AT_OFFSET(sharcParameters.hashGridData.hashEntriesBuffer, entryIndex);
    if (hashGridKey == HASH_GRID_INVALID_HASH_KEY)
        return;

    bool isResponsiveSignal = false;
#if SHARC_ENABLE_RESPONSIVE_LIGHTING
    isResponsiveSignal = (hashGridKey >> (HASH_GRID_KEY_BIT_NUM - 1)) != 0;
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

    SharcAccumulationData accumulatedData = BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, entryIndex);
    SharcPackedData resolvedData = BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, entryIndex);
    SharcVoxelData sharcVoxelData = SharcUnpackVoxelData(resolvedData);

    float sampleNum = SharcGetAccumulatedSampleNum(accumulatedData);
    float sampleNumPrev = sharcVoxelData.accumulatedSampleNum;
    uint accumulatedFrameNum = sharcVoxelData.accumulatedFrameNum + 1;
    uint staleFrameNum = sharcVoxelData.staleFrameNum;

    staleFrameNum = (sampleNum != 0) ? 0 : staleFrameNum + 1;
    uint staleFrameNumMax = clamp(resolveParameters.staleFrameNumMax, SHARC_STALE_FRAME_NUM_MIN, SHARC_STALE_FRAME_NUM_MAX);

    if (isResponsiveSignal)
        staleFrameNumMax = resolveParameters.responsiveFrameNum;

    bool isValidElement = (staleFrameNum < staleFrameNumMax) ? true : false;
    if (!isValidElement)
    {
        SharcAccumulationData zeroAccumulationData = SharcZeroAccumulationData();
        SharcPackedData zeroPackedData = SharcZeroPackedData();

        BUFFER_AT_OFFSET(sharcParameters.hashGridData.hashEntriesBuffer, entryIndex) = HASH_GRID_INVALID_HASH_KEY;
        BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, entryIndex) = zeroAccumulationData;
        BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, entryIndex) = zeroPackedData;
        return;
    }
    else if (sampleNum == 0 && !isResponsiveSignal)
    {
        // Resolve dispatches exactly one thread per entry, so no atomic is needed.
        BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, entryIndex).sampleData = resolvedData.sampleData + ((1 << SHARC_ACCUMULATED_FRAME_NUM_BIT_OFFSET) | (1 << SHARC_STALE_FRAME_NUM_BIT_OFFSET));
#if SHARC_ENABLE_FADE_ACCELERATION
        uint bitOffset = resolveParameters.frameIndex % 32u;
        uint bit = 1u << bitOffset;
        BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, entryIndex).sampleDataExt = resolvedData.sampleDataExt | bit;
#endif // SHARC_ENABLE_FADE_ACCELERATION
        return;
    }

#if SHARC_ENABLE_RESPONSIVE_LIGHTING
    if (isResponsiveSignal)
    {
        HashGridKey targetHashGridKey = hashGridKey & ~(HashGridKey(1) << (HASH_GRID_KEY_BIT_NUM - 1)); // clear responsive bit to find the main entry
        uint baseSlot = HashGridGetBaseSlot(targetHashGridKey, sharcParameters.hashGridData.capacity);
        for (uint i = 0; i < SHARC_RESPONSIVE_ENTRY_PROBE_RANGE; ++i)
        {
            uint searchIndex = baseSlot + i;
            HashGridKey searchHashGridKey = BUFFER_AT_OFFSET(sharcParameters.hashGridData.hashEntriesBuffer, searchIndex);
            if (searchHashGridKey == targetHashGridKey)
            {
                SharcAccumulationData targetAccumulatedData = BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, searchIndex);
                sampleNum = SharcGetAccumulatedSampleNum(targetAccumulatedData);
                break;
            }
        }
    }
#endif // SHARC_ENABLE_RESPONSIVE_LIGHTING

    // Performs hash map lookup to find existing entries in case previous insertions
    // encountered collisions and a different slot was assigned.
    // Uses a fixed-size linear probe window
    if (sampleNumPrev == 0)
    {
        uint searchEnd = min(entryIndex + 1 + SHARC_LINEAR_PROBE_WINDOW_SIZE, sharcParameters.hashGridData.capacity);
        for (uint i = entryIndex + 1; i < searchEnd; ++i)
        {
            HashGridKey hashKeyOld = BUFFER_AT_OFFSET(sharcParameters.hashGridData.hashEntriesBuffer, i);
            if (hashKeyOld == hashGridKey)
            {
                resolvedData = BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, i);
                sharcVoxelData = SharcUnpackVoxelData(resolvedData);
                sampleNumPrev = sharcVoxelData.accumulatedSampleNum;
                accumulatedFrameNum = sharcVoxelData.accumulatedFrameNum + 1;
                staleFrameNum = 0;
                break;
            }
        }
    }

    SharcRadianceData accumulatedRadiance = SharcGetAccumulatedRadianceData(accumulatedData, sharcParameters.radianceScale, sampleNum);
    SharcRadianceData accumulatedRadiancePrev = sharcVoxelData.accumulatedRadiance;
    uint accumulationFrameNum = clamp(isResponsiveSignal ? resolveParameters.responsiveFrameNum : resolveParameters.accumulationFrameNum, SHARC_ACCUMULATED_FRAME_NUM_MIN, SHARC_ACCUMULATED_FRAME_NUM_MAX);
    if (accumulatedFrameNum > accumulationFrameNum)
    {
        float normalizationScale = float(accumulationFrameNum) / float(accumulatedFrameNum);
        accumulatedFrameNum = accumulationFrameNum;
        sampleNumPrev *= normalizationScale;
    }

#if SHARC_ENABLE_FADE_ACCELERATION
    {
        uint bitOffset = resolveParameters.frameIndex % 32u;
        uint bit = 1u << bitOffset;

        float lumaCur = SharcRadianceLuma(accumulatedRadiance);
        float lumaPrev = SharcRadianceLuma(accumulatedRadiancePrev);
        bool fading = lumaCur < lumaPrev;

        sharcVoxelData.sampleDataExt = (sharcVoxelData.sampleDataExt & ~bit) | (fading ? bit : 0u);
        uint fadingFrameNum = countbits(sharcVoxelData.sampleDataExt);
        if (fadingFrameNum == 32)
            sampleNumPrev = sampleNum;
    }
#endif // SHARC_ENABLE_FADE_ACCELERATION
    float sampleTotalInv = rcp(sampleNumPrev + sampleNum);
    accumulatedRadiance = SharcAddRadianceData(SharcScaleRadianceData(accumulatedRadiancePrev, sampleNumPrev * sampleTotalInv), SharcScaleRadianceData(accumulatedRadiance, sampleNum * sampleTotalInv));
    float accumulatedSampleNum = sampleNumPrev + sampleNum;

#if SHARC_BLEND_ADJACENT_LEVELS
    // Reproject sample from adjacent level
    float3 cameraOffset = sharcParameters.hashGridParameters.cameraPosition.xyz - resolveParameters.cameraPositionPrev.xyz;
    if (!isResponsiveSignal && (dot(cameraOffset, cameraOffset) > 1e-6f) && (accumulatedFrameNum <= 2))
    {
        HashGridKey adjacentLevelHashKey = SharcGetAdjacentLevelHashKey(hashGridKey, sharcParameters.hashGridParameters, resolveParameters.cameraPositionPrev);

        HashGridIndex hashGridIndex;
        uint baseSlot = HashGridGetBaseSlot(adjacentLevelHashKey, sharcParameters.hashGridData.capacity);
        uint hashCollisionsNum;
        if (HashGridFind(sharcParameters.hashGridData, adjacentLevelHashKey, baseSlot, HASH_GRID_HASH_MAP_BUCKET_SIZE, hashGridIndex, hashCollisionsNum))
        {
            SharcPackedData adjacentPackedDataPrev = BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, hashGridIndex);
            SharcVoxelData adjacentVoxelDataPrev = SharcUnpackVoxelData(adjacentPackedDataPrev);
            float adjacentSampleNum = adjacentVoxelDataPrev.accumulatedSampleNum;
            if (adjacentSampleNum > SHARC_SAMPLE_NUM_THRESHOLD)
            {
                float blendWeight = rcp(adjacentSampleNum + accumulatedSampleNum);
                accumulatedRadiance = SharcAddRadianceData(
                    SharcScaleRadianceData(adjacentVoxelDataPrev.accumulatedRadiance, adjacentSampleNum * blendWeight),
                    SharcScaleRadianceData(accumulatedRadiance, accumulatedSampleNum * blendWeight));
                accumulatedSampleNum += adjacentSampleNum;
            }
        }
    }
#endif // SHARC_BLEND_ADJACENT_LEVELS

    BUFFER_AT_OFFSET(sharcParameters.resolvedBuffer, entryIndex) = SharcPackVoxelData(accumulatedRadiance, accumulatedSampleNum, accumulatedFrameNum, staleFrameNum, sharcVoxelData.sampleDataExt);

#if !SHARC_ENABLE_RESPONSIVE_LIGHTING
    // Clear buffer entry for the next frame
    SharcAccumulationData zeroAccumulationData = SharcZeroAccumulationData();
    BUFFER_AT_OFFSET(sharcParameters.accumulationBuffer, entryIndex) = zeroAccumulationData;
#endif // !SHARC_ENABLE_RESPONSIVE_LIGHTING
}
