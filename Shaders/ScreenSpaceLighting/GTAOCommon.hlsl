#ifndef GTAO_COMMON_INCLUDED
#define GTAO_COMMON_INCLUDED

// Includes
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ShaderVariablesAmbientOcclusion.hlsl"
#ifdef _SOURCE_DEPTH_NORMALS
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#else
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

// Textures & Samplers
TEXTURE2D_X_UINT2(_StencilTexture);
TEXTURE2D_X(_DepthPyramid);

// Function defines
#define SCREEN_PARAMS               _ScreenParams

// The constant below controls the geometry-awareness of the bilateral
// filter. The higher value, the more sensitive it is.
static const half kGeometryCoeff = half(0.8);

#if defined(USING_STEREO_MATRICES)
    #define unity_eyeIndex unity_StereoEyeIndex
#else
    #define unity_eyeIndex 0
#endif

#define _AOThickness                        _AOParams0.x
#define _AOFOVCorrection                    _AOParams0.y
#define _AORadius                           _AOParams0.z
#define _AOStepCount                        (uint)_AOParams0.w

#define _AOIntensity                        _AOParams1.x
#define _AOInvRadiusSq                      _AOParams1.y
#define _AOTemporalOffsetIdx                _AOParams1.z
#define _AOTemporalRotationIdx              _AOParams1.w

#define _AODirectionCount                   _AOParams2.x
#define DOWNSAMPLE                          _AOParams2.y
#define _AOInvStepCountPlusOne              _AOParams2.z
#define _AOMaxRadiusInPixels                (int)_AOParams2.w

#define _BlurTolerance                      _AOParams3.x
#define _UpsampleTolerance                  _AOParams3.y
#define _NoiseFilterStrength                _AOParams3.z
#define _StepSize                           _AOParams3.w

#define _AOTemporalUpperNudgeLimit          _AOParams4.y
#define _AOTemporalLowerNudgeLimit          _AOParams4.z
#define _AOSpatialBilateralAggressiveness   _AOParams4.w
#define _FirstDepthMipOffset                _FirstTwoDepthMipOffsets.xy
#define _SecondDepthMipOffset               _FirstTwoDepthMipOffsets.zw

// If this is set to 0 best quality is achieved when full res, but performance is significantly lower.
// If set to 1, when full res, it may lead to extra aliasing and loss of detail, but still significant higher quality than half res.
#define HALF_RES_DEPTH_WHEN_FULL_RES                1  // Make this an option.
#define HALF_RES_DEPTH_WHEN_FULL_RES_FOR_CENTRAL    0
#define MIN_DEPTH_GATHERED_FOR_CENTRAL              0
#define LOWER_RES_SAMPLE                            1

#define POWER_IN_FINAL_PASS                         1

// For Reflection Occlusion, not used yet.
#define SCREEN_SPACE_BENT_NORMAL                    0

// Accumulation options
#define TEMPORAL_ROTATION           defined(TEMPORAL)
#define ENABLE_TEMPORAL_OFFSET      defined(TEMPORAL)

static const half  HALF_ZERO        = half(0.0);
static const half  HALF_HALF        = half(0.5);
static const half  HALF_ONE         = half(1.0);
static const half  HALF_TWO         = half(2.0);

half CompareNormal(half3 d1, half3 d2)
{
    return smoothstep(kGeometryCoeff, half(1.0), dot(d1, d2));
}

uint2 GetScreenSpacePosition(float2 uv)
{
    return uint2(uv * SCREEN_PARAMS.xy * DOWNSAMPLE);
}

uint GetScreenSpaceStencil(uint2 positionSS)
{
    return GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, positionSS.xy));
}

#if !defined(_SOURCE_DEPTH_NORMALS)
float SampleAndGetLinearEyeDepth(float2 uv)
{
    float rawDepth = SampleSceneDepth(uv.xy);
    #if defined(_ORTHOGRAPHIC)
        return LinearDepthToEyeDepth(rawDepth);
    #else
        return LinearEyeDepth(rawDepth, _ZBufferParams);
    #endif
}

// This returns a vector in world unit (not a position), from camera to the given point described by uv screen coordinate and depth (in absolute world unit).
half3 ReconstructViewPos(float2 uv, float depth)
{
    // Screen is y-inverted.
    uv.y = 1.0 - uv.y;

    // view pos in world space
    #if defined(_ORTHOGRAPHIC)
        float zScale = depth * _ProjectionParams.w; // divide by far plane
        float3 viewPos = _CameraViewTopLeftCorner[unity_eyeIndex].xyz
                            + _CameraViewXExtent[unity_eyeIndex].xyz * uv.x
                            + _CameraViewYExtent[unity_eyeIndex].xyz * uv.y
                            + _CameraViewZExtent[unity_eyeIndex].xyz * zScale;
    #else
        float zScale = depth * _ProjectionParams2.x; // divide by near plane
        float3 viewPos = _CameraViewTopLeftCorner[unity_eyeIndex].xyz
                            + _CameraViewXExtent[unity_eyeIndex].xyz * uv.x
                            + _CameraViewYExtent[unity_eyeIndex].xyz * uv.y;
        viewPos *= zScale;
    #endif

    return half3(viewPos);
}

// Try reconstructing normal accurately from depth buffer.
// Low:    DDX/DDY on the current pixel
// Medium: 3 taps on each direction | x | * | y |
// High:   5 taps on each direction: | z | x | * | y | w |
// https://atyuwen.github.io/posts/normal-reconstruction/
// https://wickedengine.net/2019/09/22/improved-normal-reconstruction-from-depth/
half3 ReconstructNormal(float2 uv, float depth, float3 vpos)
{
    #if defined(_RECONSTRUCT_NORMAL_LOW)
        return half3(normalize(cross(ddy(vpos), ddx(vpos))));
    #else
        float2 delta = float2(_AOBufferSize.zw * 2.0);

        // Sample the neighbour fragments
        float2 lUV = float2(-delta.x, 0.0);
        float2 rUV = float2( delta.x, 0.0);
        float2 uUV = float2(0.0,  delta.y);
        float2 dUV = float2(0.0, -delta.y);

        float3 l1 = float3(uv + lUV, 0.0); l1.z = SampleAndGetLinearEyeDepth(l1.xy); // Left1
        float3 r1 = float3(uv + rUV, 0.0); r1.z = SampleAndGetLinearEyeDepth(r1.xy); // Right1
        float3 u1 = float3(uv + uUV, 0.0); u1.z = SampleAndGetLinearEyeDepth(u1.xy); // Up1
        float3 d1 = float3(uv + dUV, 0.0); d1.z = SampleAndGetLinearEyeDepth(d1.xy); // Down1

        // Determine the closest horizontal and vertical pixels...
        // horizontal: left = 0.0 right = 1.0
        // vertical  : down = 0.0    up = 1.0
        #if defined(_RECONSTRUCT_NORMAL_MEDIUM)
             uint closest_horizontal = l1.z > r1.z ? 0 : 1;
             uint closest_vertical   = d1.z > u1.z ? 0 : 1;
        #else
            float3 l2 = float3(uv + lUV * 2.0, 0.0); l2.z = SampleAndGetLinearEyeDepth(l2.xy); // Left2
            float3 r2 = float3(uv + rUV * 2.0, 0.0); r2.z = SampleAndGetLinearEyeDepth(r2.xy); // Right2
            float3 u2 = float3(uv + uUV * 2.0, 0.0); u2.z = SampleAndGetLinearEyeDepth(u2.xy); // Up2
            float3 d2 = float3(uv + dUV * 2.0, 0.0); d2.z = SampleAndGetLinearEyeDepth(d2.xy); // Down2

            const uint closest_horizontal = abs( 2.0 * l1.z - l2.z - depth) < abs( 2.0 * r1.z - r2.z - depth) ? 0 : 1;
            const uint closest_vertical   = abs( 2.0 * d1.z - d2.z - depth) < abs( 2.0 * u1.z - u2.z - depth) ? 0 : 1;
        #endif


        // Calculate the triangle, in a counter-clockwize order, to
        // use based on the closest horizontal and vertical depths.
        // h == 0.0 && v == 0.0: p1 = left,  p2 = down
        // h == 1.0 && v == 0.0: p1 = down,  p2 = right
        // h == 1.0 && v == 1.0: p1 = right, p2 = up
        // h == 0.0 && v == 1.0: p1 = up,    p2 = left
        // Calculate the view space positions for the three points...
        float3 P1;
        float3 P2;
        if (closest_vertical == 0)
        {
            P1 = closest_horizontal == 0 ? l1 : d1;
            P2 = closest_horizontal == 0 ? d1 : r1;
        }
        else
        {
            P1 = closest_horizontal == 0 ? u1 : r1;
            P2 = closest_horizontal == 0 ? l1 : u1;
        }

        // Use the cross product to calculate the normal...
        return half3(normalize(cross(ReconstructViewPos(P2.xy, P2.z) - vpos, ReconstructViewPos(P1.xy, P1.z) - vpos)));
    #endif
}
#endif

// For when we don't need to output the depth or view position
// Used in the blur passes
half3 SampleNormal(float2 uv)
{
    #ifdef _SOURCE_DEPTH_NORMALS
        return half3(SampleSceneNormals(uv));
    #else
        float depth = SampleAndGetLinearEyeDepth(uv);
        half3 vpos = ReconstructViewPos(uv, depth);
        return ReconstructNormal(uv, depth, vpos);
    #endif
}

float IntegrateArcCosWeighted(float horzion1, float horizon2, float n, float cosN)
{
    float h1 = horzion1 * 2.0;
    float h2 = horizon2 * 2.0;
    float sinN = sin(n);
    return 0.25 * ((-cos(h1 - n) + cosN + h1 * sinN) + (-cos(h2 - n) + cosN + h2 * sinN));
}

float GTAOFastAcos(float x)
{
    float outVal = -0.156583 * abs(x) + HALF_PI;
    outVal *= sqrt(1.0 - abs(x));
    return x >= 0 ? outVal : PI - outVal;
}

float GetDepthForCentral(float2 positionSS)
{
#ifdef FULL_RES

    #if HALF_RES_DEPTH_WHEN_FULL_RES_FOR_CENTRAL

        #if MIN_DEPTH_GATHERED_FOR_CENTRAL

        float2 localUVs = positionSS.xy * _AOBufferSize.zw;
        return GetMinDepth(localUVs);

        #else // MIN_DEPTH_GATHERED_FOR_CENTRAL
        return LOAD_TEXTURE2D_X(_DepthPyramid, float2(0.0f, _AORTHandleSize.y) + positionSS / 2).r;
        #endif

    #else  // HALF_RES_DEPTH_WHEN_FULL_RES
        return LOAD_TEXTURE2D_X(_DepthPyramid, positionSS).r;
    #endif

#else // FULL_RES

    #if MIN_DEPTH_GATHERED_FOR_CENTRAL
        float2 localUVs = positionSS.xy * _AOBufferSize.zw;
        return GetMinDepth(localUVs);
    #else
        return LOAD_TEXTURE2D_X(_DepthPyramid, _FirstDepthMipOffset + (uint2)positionSS.xy).r;
    #endif

#endif
}

float GetDepthSample(float2 positionSS, bool lowerRes)
{
#ifdef FULL_RES

    #if HALF_RES_DEPTH_WHEN_FULL_RES
    return LOAD_TEXTURE2D_X(_DepthPyramid, _FirstDepthMipOffset + positionSS / 2).r;
    #endif

    return LOAD_TEXTURE2D_X(_DepthPyramid, positionSS).r;


#else // FULL_RES

    #if LOWER_RES_SAMPLE
    if (lowerRes)
    {
        return LOAD_TEXTURE2D_X(_DepthPyramid, _SecondDepthMipOffset + (uint2)positionSS.xy / 2).r;
    }
    else
    #endif
    {
        return LOAD_TEXTURE2D_X(_DepthPyramid, _FirstDepthMipOffset + (uint2)positionSS.xy).r;
    }
#endif
}

// --------------------------------------------
// Get sample start offset
// --------------------------------------------

float GetOffset(uint2 positionSS)
{
    // Spatial offset
    float offset = 0.25 * ((positionSS.y - positionSS.x) & 0x3);

    // Temporal offset
#if ENABLE_TEMPORAL_OFFSET
    float offsets[] = { 0.0, 0.5, 0.25, 0.75 };
    offset += offsets[_AOTemporalOffsetIdx];
#endif
    return frac(offset);
}

// --------------------------------------------
// Direction functions
// --------------------------------------------

float2 GetDirection(uint2 positionSS, int offset)
{
    float noise = InterleavedGradientNoise(positionSS.xy, 0);
    float rotations[] = { 60.0, 300.0, 180.0, 240.0, 120.0, 0.0 };

#if TEMPORAL_ROTATION
    float rotation = (rotations[_AOTemporalRotationIdx] / 360.0);
#else
    float rotation = (rotations[offset] / 360.0);
#endif

    noise += rotation;
    noise *= PI;

    return float2(cos(noise), sin(noise));
}

float3 GetPositionVS(float2 positionSS, float depth)
{
    float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
    return float3((positionSS * _AODepthToViewParams.xy - _AODepthToViewParams.zw) * linearDepth, linearDepth);
}

float3 GetNormalVS(float3 normalWS)
{
    float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_V, normalWS));
    return float3(normalVS.xy, -normalVS.z);
}

float UpdateHorizon(float maxH, float candidateH, float distSq)
{
    float falloff = saturate(1.0 - distSq * _AOInvRadiusSq);

    return candidateH > maxH ? lerp(maxH, candidateH, falloff) : lerp(maxH, candidateH, _AOThickness);
}

float HorizonLoop(float3 positionVS, float3 V, float2 rayStart, float2 rayDir, float rayOffset,
    float rayStep, int mipModifier)
{
    float maxHorizon = -1.0f;  // cos(pi)
    float t = rayOffset * rayStep + rayStep;

    const uint startWithLowerRes = min(max(0, _AOStepCount / 2 - 2), 3);
    for (uint i = 0; i < _AOStepCount; i++)
    {
        float2 samplePos = max(2, min(rayStart + t * rayDir, _AOBufferSize.xy - 2));

        // Find horizons at these steps:
        float sampleDepth = GetDepthSample(samplePos, i > startWithLowerRes);
        float3 samplePosVS = GetPositionVS(samplePos.xy, sampleDepth);

        float3 deltaPos = samplePosVS - positionVS;
        float deltaLenSq = dot(deltaPos, deltaPos);

        float currHorizon = dot(deltaPos, V) * rsqrt(deltaLenSq + 0.0001f);
        maxHorizon = UpdateHorizon(maxHorizon, currHorizon, deltaLenSq);

        t += rayStep;
    }

    return maxHorizon;
}

// --------------------------------------------
// Output functions
// --------------------------------------------
float PackAOOutput(float AO, float depth)
{
    uint packedDepth = PackFloatToUInt(depth, 0, 23);
    uint packedAO = PackFloatToUInt(AO, 24, 8);
    uint packedVal = packedAO | packedDepth;
    // If it is a NaN we have no guarantee the sampler will keep the bit pattern, hence we invalidate the depth, meaning that the various bilateral passes will skip the sample.
    if ((packedVal & 0x7FFFFFFF) > 0x7F800000)
    {
        packedVal = packedAO;
    }

    // We need to output as float as gather4 on an integer texture is not always supported.
    return asfloat(packedVal);
}

float4 PackHistoryData(float AO, float depth, float mvLen)
{
    float4 finalHistory;
    finalHistory.xy = PackFloatToR8G8(depth);
    finalHistory.z = saturate(AO);
    finalHistory.w = saturate(mvLen);
    return finalHistory;
}

void UnpackHistoryData(float4 historyData, out float AO, out float depth, out float mvLen)
{
    AO = historyData.z;
    mvLen = saturate(historyData.w);
    depth = UnpackFloatFromR8G8(historyData.xy);
}

void UnpackData(float data, out float AO, out float depth)
{
    depth = UnpackUIntToFloat(asuint(data), 0, 23);
    AO = UnpackUIntToFloat(asuint(data), 24, 8);
}

void UnpackGatheredData(float4 data, out float4 AOs, out float4 depths)
{
    UnpackData(data.x, AOs.x, depths.x);
    UnpackData(data.y, AOs.y, depths.y);
    UnpackData(data.z, AOs.z, depths.z);
    UnpackData(data.w, AOs.w, depths.w);
}

void GatherAOData(TEXTURE2D_X_FLOAT(_AODataSource), float2 UV, out float4 AOs, out float4 depths)
{
    float4 data = GATHER_TEXTURE2D_X(_AODataSource, sampler_PointClamp, UV);
    UnpackGatheredData(data, AOs, depths);
}

float OutputFinalAO(float AO)
{
#if POWER_IN_FINAL_PASS
    return PositivePow(AO, _AOIntensity);
#else
    return saturate(1 - AO);
#endif
}

half4 PackAONormal(float ao, float3 normal)
{
    normal *= HALF_HALF;
    normal += HALF_HALF;
    return half4(ao, normal);
}

float4 PackAODepthNormal(float ao, float depth, float3 normal)
{
#ifdef PACK_AO_DEPTH
    // Not used since missing precision.
    // Layout: [AO, Depth, OctNormal.x, OctNormal.y]
    // half2 normalEncoded = PackNormalOctQuadEncode(normal);
    // return half4(ao, depth, normalEncoded.x, normalEncoded.y);
    return PackAOOutput(ao, depth).xxxx;
#else
    // @IllusionRP: URP version packing
    return PackAONormal(ao, normal);
#endif
}

half3 GetPackedNormal(half4 p)
{
#ifdef PACK_AO_DEPTH
    return UnpackNormalOctQuadEncode(p.zw);
#else
    // @IllusionRP: URP version unpacking
    return p.gba * HALF_TWO - HALF_ONE;
#endif
}

half GetPackedAO(half4 p)
{
    return p.r;
}

// Reference: HDRP.GTAO.compute
float4 GTAO(uint2 positionSS)
{
    // Read buffers as early as possible.
    float currDepth = GetDepthForCentral(positionSS);
    float3 positionVS = GetPositionVS(positionSS, currDepth);
    uint2 nonScaledPositionSS = positionSS * rcp(DOWNSAMPLE);

#ifdef _SOURCE_DEPTH_NORMALS
    half3 norm_o = LoadSceneNormals(nonScaledPositionSS);
#else
    float2 uv = nonScaledPositionSS / _ScreenParams.xy;
    half3 norm_o = SampleNormal(uv);
#endif

    uint stencilValue = GetScreenSpaceStencil(nonScaledPositionSS);
    bool doesntReceiveSSAO = (stencilValue & STENCIL_USAGE_NO_AO) != 0;
    if (doesntReceiveSSAO)
    {
#if POWER_IN_FINAL_PASS
        return PackAODepthNormal(1, currDepth, norm_o);
#else
        return PackAODepthNormal(0, currDepth, norm_o);
#endif
    }

    float offset = GetOffset(positionSS);
    float2 rayStart = positionSS;
    float integral = 0;

#ifdef TEMPORAL
    const int dirCount = 1;
#else
    const int dirCount = _AODirectionCount;
#endif

    float3 V = normalize(-positionVS);
    float fovCorrectedradiusSS = clamp(_AORadius * _AOFOVCorrection * rcp(positionVS.z), _AOStepCount, _AOMaxRadiusInPixels);
    float step = max(1, fovCorrectedradiusSS * _AOInvStepCountPlusOne);
    
#if SCREEN_SPACE_BENT_NORMAL
    float bentAngle;
    float3 norm_bent;
#endif

// Note: We unroll here for Metal to work around a Metal shader compiler crash
#if defined(TEMPORAL) || defined(SHADER_API_METAL)
    [unroll]
#endif
    for (int i = 0; i < dirCount; ++i)
    {
        float2 dir = GetDirection(positionSS, i);

        // NOTE: Work around a shader compilation bug, where removing the tiny epsilon causes
        // incorrect output on some drivers. The epsilon should be small enough to not affect
        // the output.
        float2 negDir = -dir + 1e-30;

        // Find horizons
        float2 maxHorizons;
        maxHorizons.x = HorizonLoop(positionVS, V, rayStart, dir, offset, step, 0);
        maxHorizons.y = HorizonLoop(positionVS, V, rayStart, negDir, offset, step, 0);

        // We now can transform normal data into normal in view space (latency from read should have been hidden as much as possible)
        float3 normalVS = GetNormalVS(norm_o);

        // Integrate horizons
        float3 sliceN = normalize(cross(float3(dir.xy, 0.0f), V.xyz));
        float3 projN = normalVS - sliceN * dot(normalVS, sliceN);
        float projNLen = length(projN);
        float cosN = dot(projN / projNLen, V);

        float3 T = cross(V, sliceN);
        float N = -sign(dot(projN, T)) * GTAOFastAcos(cosN);

        // Now we find the actual horizon angles
        maxHorizons.x = -GTAOFastAcos(maxHorizons.x);
        maxHorizons.y = GTAOFastAcos(maxHorizons.y);
        maxHorizons.x = N + max(maxHorizons.x - N, -HALF_PI);
        maxHorizons.y = N + min(maxHorizons.y - N, HALF_PI);
        integral += AnyIsNaN(maxHorizons) ? 1 : IntegrateArcCosWeighted(maxHorizons.x, maxHorizons.y, N, cosN);

#if SCREEN_SPACE_BENT_NORMAL
        bentAngle = (maxHorizons.x + maxHorizons.y) * 0.5; // average both horizon angles
        norm_bent += V * cos(bentAngle) - T * sin(bentAngle);
#endif
    }

    integral /= dirCount;

#if SCREEN_SPACE_BENT_NORMAL
    norm_bent /= dirCount;
#endif

    if (currDepth == UNITY_RAW_FAR_CLIP_VALUE || integral < -1e-2f)
    {
        integral = 1;
    }

#if POWER_IN_FINAL_PASS
    float ao = saturate(integral);
#else
    float ao = 1.0f - PositivePow(saturate(integral), _AOIntensity);
#endif
    
#if SCREEN_SPACE_BENT_NORMAL
    return PackAODepthNormal(ao, currDepth, norm_bent);
#else
    return PackAODepthNormal(ao, currDepth, norm_o);
#endif
}

#endif // GTAO_COMMON_INCLUDED
