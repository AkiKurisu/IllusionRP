#ifndef HD_SHADOW_SAMPLING_AREA_INCLUDED
#define HD_SHADOW_SAMPLING_AREA_INCLUDED
// Various shadow sampling algorithms used by the area light shadow filtering tiers (EVSM and PCSS).

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/ShadowMoments.hlsl"

//
//                  1 tap EVSM sampling
//
float SampleShadow_EVSM_1tap(float3 tcs, float lightLeakBias, float varianceBias, float2 evsmExponents, bool fourMoments, Texture2D tex, SamplerState samp)
{
#if UNITY_REVERSED_Z
    float  depth      = 1.0 - tcs.z;
#else
    float  depth      = tcs.z;
#endif


    float4 moments = SAMPLE_TEXTURE2D_LOD(tex, samp, tcs.xy, 0.0);

    UNITY_BRANCH
    if (fourMoments)
    {
        float2 warpedDepth = ShadowMoments_WarpDepth(depth, evsmExponents);

        // Derivate of warping at depth
        float2 depthScale = evsmExponents * warpedDepth;
        float2 minVariance = depthScale * depthScale * varianceBias;

        float posContrib = ShadowMoments_ChebyshevsInequality(moments.xz, warpedDepth.x, minVariance.x, lightLeakBias);
        float negContrib = ShadowMoments_ChebyshevsInequality(moments.yw, warpedDepth.y, minVariance.y, lightLeakBias);
        return min(posContrib, negContrib);
    }
    else
    {
        float warpedDepth = ShadowMoments_WarpDepth_PosOnlyBaseTwo(depth, evsmExponents.x);

        // Derivate of warping at depth
        float depthScale = evsmExponents.x * warpedDepth;
        float minVariance = depthScale * depthScale * varianceBias;

        return ShadowMoments_ChebyshevsInequality(moments.xy, warpedDepth, minVariance, lightLeakBias);
    }
}

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/HDPCSS.hlsl"

// TODO: This PCSS variant works for other types of lights as well, but is not well tested there, so we're introducing it only for area lights for now.
float SampleShadow_PCSS_Area(float3 posTCAtlas, float2 posSS, float2 shadowmapInAtlasScale, float2 shadowmapInAtlasOffset, float shadowSoftness, float minFilterRadius, int blockerSampleCount, int filterSampleCount, Texture2D tex, SamplerComparisonState compSamp, SamplerState samp, float depthBias, float4 zParams, bool isPerspective, float2 shadowAtlasInfo)
{
#if SHADOW_USE_DEPTH_BIAS == 1
    posTCAtlas.z += depthBias;
#endif

    // This is a modified PCSS. Instead of performing both the blocker search and filtering phases using a flat disc of samples centered around
    // the shaded point, it adds a z offset to sample points extruding them in a cone shape - pyramid, actually - towards the light. The base of the pyramid
    // is the near plane of the area light (surface of the area light when near plane is at 0), the apex at the shaded point, and samples lie on the 4 sides
    // of the pyramid.
    //
    // The idea is that only casters within the volume of that pyramid would contribute to the shadow. In other words any casters caught by a sample with
    // z further away from the light than z of that sample don't contribute to the shadow.
    //
    // The maximum heigh of the pyramid is the z distance between the shaded point and the near plane. Lowering that height is necessary to keep
    // the sampling kernel sizes reasonable and is controlled by maxSampleZDistance. Higher maxSampleZDistance values result in wider penumbras.

    // Rescale the softness param so that the default 1 gives a very soft shadow without pushing it to edge, where artifacts start to show up.
    // This way setting softness to slightly more than 1 will get the shadow close to the raytraced reference, but with a more stable default.
    float maxSampleZDistance = shadowSoftness * 65.0;

    // Undo shadowmap-in-atlas scaling of this value, since we don't interpret it here as the size of the sampling kernel, but z distance
    float shadowmapWidth = shadowmapInAtlasScale.x * shadowAtlasInfo.x;
    // The 4096 literal is here for historical reasons
    maxSampleZDistance *= 4096.0 / shadowmapWidth;
    // TODO: move all the softness aka max distance rescaling to c#

    float2 sampleJitter = ComputePcfSampleJitter(posSS.xy, (uint)_TaaFrameInfo.z);

    // TODO: should maybe pass it from an earlier stage instead of calculating it again
    float3 posTCShadowmap = float3((posTCAtlas.xy - shadowmapInAtlasOffset) / shadowmapInAtlasScale, posTCAtlas.z);

    real2 minCoord = shadowmapInAtlasOffset;
    real2 maxCoord = shadowmapInAtlasOffset + shadowmapInAtlasScale;

    //1) Blocker Search
    float blocker = 0.0;
    bool blockerFound = BlockerSearch_Area(blocker, maxSampleZDistance, shadowmapInAtlasScale, posTCAtlas.xy, posTCShadowmap, minCoord, maxCoord, sampleJitter, tex, samp, blockerSampleCount);

    //2) Penumbra Estimation
    maxSampleZDistance *= isPerspective ? PenumbraSizePunctual(posTCAtlas.z, blocker) : PenumbraSizeDirectional(posTCAtlas.z, blocker, zParams.x);
    // Extend the sampling cone only up to a certain margin before the blocker. Extending it past that distance will make samples miss the blocker and the shadow will fade.
    maxSampleZDistance = min(maxSampleZDistance, (blocker - posTCAtlas.z) * 0.9);
    // minFilterRadius can extend the cone past the above, so min&max instead of clamp.
    maxSampleZDistance = max(maxSampleZDistance, minFilterRadius * 10);

    //3) Filter
    // We can't early out of the function if blockers are not found since Vulkan triggers a warning otherwise
    bool withinShadowmap = all(posTCShadowmap.xy > 0) && all(posTCShadowmap.xy < 1);
    return blockerFound && withinShadowmap ? PCSS_Area(posTCAtlas.xy, posTCShadowmap, maxSampleZDistance, shadowmapInAtlasScale, shadowmapInAtlasOffset, minCoord, maxCoord, sampleJitter, tex, compSamp, filterSampleCount) : 1.0f;
}

#endif // HD_SHADOW_SAMPLING_AREA_INCLUDED
