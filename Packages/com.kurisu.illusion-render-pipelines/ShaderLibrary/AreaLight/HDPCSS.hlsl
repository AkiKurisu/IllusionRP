#ifndef HD_PCSS_AREA_INCLUDED
#define HD_PCSS_AREA_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/PCSS.hlsl"

real PenumbraSizePunctual(real Reciever, real Blocker)
{
    return abs((Reciever - Blocker) / Blocker);
}

real PenumbraSizeDirectional(real Reciever, real Blocker, real rangeScale)
{
    return abs(Reciever - Blocker) * rangeScale;
}

///////////////////////////////
// PCSS variant for area lights

void FilterScaleOffset(real3 coord, real maxSampleZDistance, real shadowmapSamplingScale, out real2 filterScalePos, out real2 filterScaleNeg, out real2 filterOffset)
{
    real d = shadowmapSamplingScale * maxSampleZDistance / (1 - coord.z);
    real2 target = (coord.xy + 0.5) * 0.5;

    filterScalePos = (1 - target) * d;
    filterScaleNeg = target * d;
    filterOffset = (target - coord.xy) * d;
}

bool BlockerSearch_Area(inout real closestBlocker, real maxSampleZDistance, real2 shadowmapInAtlasScale, real2 posTCAtlas, real3 posTCShadowmap, real2 minCoord, real2 maxCoord, real2 sampleJitter, Texture2D shadowMap, SamplerState pointSampler, int sampleCount)
{
    // The z extent of the filter cone shouldn't go beyond the near plane of the shadow
#if UNITY_REVERSED_Z
    #define NEARPLANE 1
    maxSampleZDistance = min(1 - posTCShadowmap.z, maxSampleZDistance);
#else
    #define NEARPLANE 0
    maxSampleZDistance = min(posTCShadowmap.z, maxSampleZDistance);
#endif

    real sampleCountInverse = rcp((real)sampleCount);

    real2 filterScalePos, filterScaleNeg;
    real2 filterOffset;
    FilterScaleOffset(posTCShadowmap, maxSampleZDistance, shadowmapInAtlasScale.x, filterScalePos, filterScaleNeg, filterOffset);

    closestBlocker = NEARPLANE;
    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; ++i)
    {
        real sampleDistNorm;
        real2 offset = ComputeFibonacciSpiralDiskSampleClumpedCubic(i, sampleCountInverse, sampleDistNorm);
        offset = real2(offset.x *  sampleJitter.y + offset.y * sampleJitter.x,
                       offset.x * -sampleJitter.x + offset.y * sampleJitter.y);

        offset = offset * real2(offset.x > 0 ? filterScalePos.x : filterScaleNeg.x, offset.y > 0 ? filterScalePos.y : filterScaleNeg.y) + filterOffset * sampleDistNorm;
        real zoffset = maxSampleZDistance * sampleDistNorm;

        real2 pos = posTCAtlas + offset;

        real blocker = SAMPLE_TEXTURE2D_LOD(shadowMap, pointSampler, pos, 0.0).x;

        if (!(any(pos < minCoord) || any(pos > maxCoord)) &&
            COMPARE_DEVICE_DEPTH_CLOSER(blocker, posTCShadowmap.z + zoffset) &&
            COMPARE_DEVICE_DEPTH_CLOSER(closestBlocker, blocker))
        {
                closestBlocker = blocker;
        }
    }

    return COMPARE_DEVICE_DEPTH_CLOSER(NEARPLANE, closestBlocker);
}

real PCSS_Area(real2 posTCAtlas, real3 posTCShadowmap, real maxSampleZDistance, real2 shadowmapInAtlasScale, real2 shadowmapInAtlasOffset, real2 minCoord, real2 maxCoord, real2 sampleJitter, Texture2D shadowMap, SamplerComparisonState compSampler, int sampleCount)
{
    real biasFactor = 1;
    real sampleCountInverse = rcp((real)sampleCount + biasFactor);
    real sampleBias = biasFactor * sampleCountInverse;

    real2 filterScalePos, filterScaleNeg;
    real2 filterOffset;
    FilterScaleOffset(posTCShadowmap, maxSampleZDistance, shadowmapInAtlasScale.x, filterScalePos, filterScaleNeg, filterOffset);

    real sum = 0.0;
    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; ++i)
    {
        real sampleDistNorm;
        real2 offset = ComputeFibonacciSpiralDiskSampleUniform(i, sampleCountInverse, sampleBias, sampleDistNorm);
        offset = real2(offset.x *  sampleJitter.y + offset.y * sampleJitter.x,
                       offset.x * -sampleJitter.x + offset.y * sampleJitter.y);

        offset = offset * real2(offset.x > 0 ? filterScalePos.x : filterScaleNeg.x, offset.y > 0 ? filterScalePos.y : filterScaleNeg.y) + filterOffset * sampleDistNorm;
        real zoffset = maxSampleZDistance * sampleDistNorm;

        real3 pos = 0;
        pos.xy = posTCAtlas + offset;
        pos.z = posTCShadowmap.z + zoffset;

        sum += (any(pos.xy < minCoord) || any(pos.xy > maxCoord)) ? 1 : SAMPLE_TEXTURE2D_SHADOW(shadowMap, compSampler, pos).r;
    }

    return sum / sampleCount;
}

#endif // HD_PCSS_AREA_INCLUDED
