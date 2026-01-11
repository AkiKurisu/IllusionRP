#ifndef SCREEN_SPACE_REFLECTION_COMMON_INCLUDED
#define SCREEN_SPACE_REFLECTION_COMMON_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Raytracing/RaytracingSampling.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ShaderVariablesScreenSpaceReflection.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/DeclareDepthPyramid.hlsl"
#ifdef SSR_REPROJECT
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ScreenSpaceLighting.hlsl"
#endif

#if defined(SSR_REPROJECT) || defined(SSR_ACCUMULATE)
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/DeclareMotionVectorTexture.hlsl"
#endif

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/NormalBuffer.hlsl"

TEXTURE2D_X(_CameraDepthTexture); // Not used in Hiz version
TEXTURE2D_X_UINT2(_StencilTexture);

#ifdef SSR_REPROJECT
    TEXTURE2D_X(_SsrHitPointTexture);
    TEXTURE2D_X(_ColorPyramidTexture);
#endif

#ifdef SSR_ACCUMULATE
    TEXTURE2D_X(_SsrHitPointTexture);
#endif

/// ================ Legacy ================ //
#define BinaryStepCount 5
#define LINEAR_TRACE_DEPTH_BIAS 0.05
#define LINEAR_TRACE_2D_THICKNESS 0.1

static half dither[16] = {
    0.0, 0.5, 0.125, 0.625,
    0.75, 0.25, 0.875, 0.375,
    0.187, 0.687, 0.0625, 0.562,
    0.937, 0.437, 0.812, 0.312
};
/// ================ Legacy ================ //

/// ================== Hiz ================= //
#define SSR_STEPS                   _Steps
#define _SsrReflectsSky             1            // Should disable in transparent objects
#ifndef SSR_APPROX
    #define SAMPLES_VNDF            1
#endif
#define SSR_TRACE_EPS               0.000488281f // 2^-11, should be good up to 4K
#define SSR_TRACE_TOWARDS_EYE
#define SSR_TRACE_BEHIND_OBJECTS
#define MIN_GGX_ROUGHNESS           0.00001f
#define MAX_GGX_ROUGHNESS           0.99999f
#define DOWNSAMPLE                  _SsrDownsamplingDivider
#define _FrameCount                 _TaaFrameInfo.y
/// ================== Hiz ================= //

#if 0
    #define SSR_MATRIX_VP           UNITY_MATRIX_VP // Jittered
    #define SSR_MATRIX_I_VP         UNITY_MATRIX_I_VP
#else
    #define SSR_MATRIX_VP           _GlobalViewProjMatrix // Jittered
    #define SSR_MATRIX_I_VP         _GlobalInvViewProjMatrix
#endif

float GetDepthSample(float2 positionSS)
{
    return LOAD_TEXTURE2D_X(_DepthPyramid, positionSS).r;
}

float4 TransformViewToHScreen(float3 vpos, float2 screenSize)
{
    float4 cpos = mul(UNITY_MATRIX_P, float4(vpos, 0));
    cpos.xy = float2(cpos.x, cpos.y * _ProjectionParams.x) * 0.5 + 0.5 * cpos.w;
    cpos.xy *= screenSize;
    return cpos;
}

inline half distanceSquared(half2 A, half2 B)
{
    A -= B;
    return dot(A, A);
}

bool intersectsDepthBuffer(half rayZMin, half rayZMax, half sceneZ, half layerThickness)
{
    return (rayZMax >= sceneZ - layerThickness) && (rayZMin <= sceneZ);
}

bool rayIterations(Texture2D frontDepth, SamplerState DepthSampler,
                   inout half2 P,
                   inout half stepDirection, inout half end, inout int stepCount, inout int maxSteps,
                   bool intersecting,
                   inout half sceneZ, inout half2 dP, inout half3 Q, inout half3 dQ, inout half k, inout half dk,
                   inout half rayZMin, inout half rayZMax, inout half prevZMaxEstimate, bool permute,
                   inout half2 hitPixel,
                   half2 invSize, inout half layerThickness)
{
    bool stop = intersecting;
    UNITY_LOOP
    for (; (P.x * stepDirection) <= end && stepCount < maxSteps && !stop; P += dP, Q.z += dQ.z, k += dk, stepCount += 1)
    {
        rayZMin = prevZMaxEstimate;
        rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
        prevZMaxEstimate = rayZMax;

        if (rayZMin > rayZMax)
        {
            Swap(rayZMin, rayZMax);
        }

        hitPixel = permute ? P.yx : P;
        sceneZ = SAMPLE_TEXTURE2D_LOD(frontDepth, DepthSampler, half2(hitPixel * invSize), 0).r;
        sceneZ = -LinearEyeDepth(sceneZ, _ZBufferParams);
        bool isBehind = (rayZMin <= sceneZ);
        intersecting = isBehind && (rayZMax >= sceneZ - layerThickness);
        stop = isBehind;
    }

    P -= dP, Q.z -= dQ.z, k -= dk;
    return intersecting;
}

bool Linear2D_Trace(Texture2D frontDepth,
                    SamplerState depthSampler,
                    half3 csOrigin,
                    half3 csDirection,
                    half4x4 projectMatrix,
                    half2 csZBufferSize,
                    half jitter,
                    int maxSteps,
                    half layerThickness,
                    half traceDistance,
                    in out half2 hitPixel,
                    int stepSize,
                    in out half3 csHitPoint,
                    in out half stepCount)
{
    half2 invSize = half2(1 / csZBufferSize.x, 1 / csZBufferSize.y);
    hitPixel = half2(-1, -1);

    half nearPlaneZ = -0.01;
    half rayLength = ((csOrigin.z + csDirection.z * traceDistance) > nearPlaneZ)
                         ? ((nearPlaneZ - csOrigin.z) / csDirection.z)
                         : traceDistance;
    half3 csEndPoint = csDirection * rayLength + csOrigin;
    half4 H0 = TransformViewToHScreen(csOrigin, csZBufferSize);
    half4 H1 = TransformViewToHScreen(csEndPoint, csZBufferSize);


    half k0 = 1 / H0.w;
    half k1 = 1 / H1.w;
    half2 P0 = H0.xy * k0;
    half2 P1 = H1.xy * k1;
    half3 Q0 = csOrigin * k0;
    half3 Q1 = csEndPoint * k1;

    half yMax = csZBufferSize.y - 0.5;
    half yMin = 0.5;
    half xMax = csZBufferSize.x - 0.5;
    half xMin = 0.5;
    half alpha = 0;

    if (P1.y > yMax || P1.y < yMin)
    {
        half yClip = (P1.y > yMax) ? yMax : yMin;
        half yAlpha = (P1.y - yClip) / (P1.y - P0.y);
        alpha = yAlpha;
    }
    if (P1.x > xMax || P1.x < xMin)
    {
        half xClip = (P1.x > xMax) ? xMax : xMin;
        half xAlpha = (P1.x - xClip) / (P1.x - P0.x);
        alpha = max(alpha, xAlpha);
    }

    P1 = lerp(P1, P0, alpha);
    k1 = lerp(k1, k0, alpha);
    Q1 = lerp(Q1, Q0, alpha);

    P1 = (distanceSquared(P0, P1) < 0.0001) ? P0 + half2(0.01, 0.01) : P1;
    half2 delta = P1 - P0;
    bool permute = false;

    if (abs(delta.x) < abs(delta.y))
    {
        permute = true;
        delta = delta.yx;
        P1 = P1.yx;
        P0 = P0.yx;
    }

    half stepDirection = sign(delta.x);
    half invdx = stepDirection / delta.x;
    half2 dP = half2(stepDirection, invdx * delta.y);
    half3 dQ = (Q1 - Q0) * invdx;
    half dk = (k1 - k0) * invdx;
    
    dP *= stepSize;
    dQ *= stepSize;
    dk *= stepSize;
    P0 += dP * jitter;
    Q0 += dQ * jitter;
    k0 += dk * jitter;

    half3 Q = Q0;
    half k = k0;
    half prevZMaxEstimate = csOrigin.z;
    stepCount = 0;
    half rayZMax = prevZMaxEstimate, rayZMin = prevZMaxEstimate;
    half sceneZ = 10000;
    half end = P1.x * stepDirection;
    bool intersecting = intersectsDepthBuffer(rayZMin, rayZMax, sceneZ, layerThickness);
    half2 P = P0;
    int originalStepCount = 0;

    intersecting = rayIterations(frontDepth, depthSampler, P, stepDirection, end, originalStepCount,
                  maxSteps,
                  intersecting, sceneZ, dP, Q, dQ, k, dk, rayZMin, rayZMax, prevZMaxEstimate, permute, hitPixel,
                  invSize, layerThickness);

    stepCount = originalStepCount;
    Q.xy += dQ.xy * stepCount;
    csHitPoint = Q * (1 / k);
    return intersecting;
}

bool IsInfinityFar(float rawDepth)
{
    #if UNITY_REVERSED_Z
    // Case for platforms with REVERSED_Z, such as D3D.
    if (rawDepth < 0.00001)
        return true;
    #else
    // Case for platforms without REVERSED_Z, such as OpenGL.
    if (rawDepth > 0.9999)
        return true;
    #endif
    return false;
}

bool BinarySearchVS(float3 rayStep, inout float3 samplePositionVS, inout float2 reflectUV,
                    inout float diff, float oneMinusViewReflectDot)
{
    UNITY_LOOP
    for (int i = 0; i < BinaryStepCount; i++)
    {
        rayStep *= 0.5f;
        [flatten]
        if (diff > 0)
        {
            samplePositionVS -= rayStep;
        }
        else if (diff < 0)
        {
            samplePositionVS += rayStep;
        }
        else
        {
            break;
        }

        float4 sampleUV = mul(UNITY_MATRIX_P, float4(samplePositionVS.x, samplePositionVS.y * -1,
                                                     samplePositionVS.z * -1, 1));
        sampleUV /= sampleUV.w;
        sampleUV.x *= 0.5f;
        sampleUV.y *= 0.5f;
        sampleUV.x += 0.5f;
        sampleUV.y += 0.5f;
        reflectUV = sampleUV.xy;
        float sampledDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, sampleUV.xy, 0).r;
        float eyeDepth = LinearEyeDepth(sampledDepth, _ZBufferParams);
        diff = samplePositionVS.z - eyeDepth;
        float minv = 1 / max((oneMinusViewReflectDot * float(i)), 0.001);
        if (abs(diff) > minv)
        {
            return false;
        }
    }
    return true;
}

bool LinearTraceRayVS(Texture2D _DepthTexture, SamplerState sampler_DepthTexture, int NumSteps, float stepSize,
                      float2 BlueNoise, float rawDepth, inout float diff, inout float2 reflectUV,
                      inout float3 rayPos, float3 rayDir)
{
    float2 jitter = BlueNoise + 0.5;
    float StepSize = stepSize;
    //StepSize = StepSize * (jitter.x + jitter.y) + StepSize;

    float oldDepth = 0;
    float oldDiff = 0;
    float2 oldPos = float2(0, 0);
    reflectUV = 0;

    UNITY_LOOP
    for (int i = 0; i < NumSteps; i++)
    {
        rayPos += rayDir * StepSize;
        float4 sampleUV = mul(UNITY_MATRIX_P, float4(rayPos.x, rayPos.y * -1,
                                                     rayPos.z * -1, 1));
        sampleUV /= sampleUV.w;
        sampleUV.x *= 0.5f;
        sampleUV.y *= 0.5f;
        sampleUV.x += 0.5f;
        sampleUV.y += 0.5f;
        [branch]
        if (sampleUV.x >= 1 || sampleUV.x < 0 || sampleUV.y >= 1 || sampleUV.y < 0)
        {
            break;
        }

        float sampledDepth = SAMPLE_TEXTURE2D_X_LOD(_DepthTexture, sampler_DepthTexture, sampleUV.xy, 0).r;
        UNITY_BRANCH
            float eyeDepth = LinearEyeDepth(sampledDepth, _ZBufferParams);
        diff = rayPos.z - eyeDepth;
        if (diff > 0)
        {
            if (diff < StepSize)
            {
                reflectUV = sampleUV.xy;
                return true;
            }
            if (eyeDepth - oldDepth > -StepSize * 10)
            {
                float blend = (oldDiff - diff) / max(oldDiff, diff) * 0.5 + 0.5;
                reflectUV = lerp(sampleUV.xy, oldPos, blend);
                return true;
            }
        }
        else if (diff < 2)
        {
            oldDiff = diff;
            oldDepth = eyeDepth;
            oldPos.xy = sampleUV.xy;
        }
    }
    return false;
}


//--------------------------------------------------------------------------------------------------
// Helpers
//--------------------------------------------------------------------------------------------------

// Weight for SSR where Fresnel == 1 (returns value/pdf)
float GetSSRSampleWeight(float3 V, float3 L, float roughness)
{
    // Simplification:
    // value = D_GGX / (lambdaVPlusOne + lambdaL);
    // pdf = D_GGX / lambdaVPlusOne;

    const float lambdaVPlusOne = Lambda_GGX(roughness, V) + 1.0;
    const float lambdaL = Lambda_GGX(roughness, L);

    return lambdaVPlusOne / (lambdaVPlusOne + lambdaL);
}

float Normalize01(float value, float minValue, float maxValue)
{
    return (value - minValue) / (maxValue - minValue);
}

// Specialization without Fresnel (see PathTracingBSDF.hlsl for the reference implementation)
bool SampleGGX_VNDF(float roughness_,
                    float3x3 localToWorld,
                    float3 V,
                    float2 inputSample,
                out float3 outgoingDir,
                out float weight)
{
    weight = 0.0f;

    float roughness = clamp(roughness_, MIN_GGX_ROUGHNESS, MAX_GGX_ROUGHNESS);

    float VdotH;
    float3 localV, localH;
    SampleGGXVisibleNormal(inputSample.xy, V, localToWorld, roughness, localV, localH, VdotH);

    // Compute the reflection direction
    float3 localL = 2.0 * VdotH * localH - localV;
    outgoingDir = mul(localL, localToWorld);

    if (localL.z < 0.001)
    {
        return false;
    }

    weight = GetSSRSampleWeight(localV, localL, roughness);

    if (weight < 0.001)
        return false;

    return true;
}

float PerceptualRoughnessFade(float perceptualRoughness, float fadeRcpLength, float fadeEndTimesRcpLength)
{
    float t = Remap10(perceptualRoughness, fadeRcpLength, fadeEndTimesRcpLength);
    return Smoothstep01(t);
}

void GetHitInfos(uint2 positionSS, out float srcPerceptualRoughness, out float3 positionWS,
    out float weight, out float3 N, out float3 L, out float3 V,
    out float NdotL, out float NdotH, out float VdotH, out float NdotV)
{
    // float2 uv = float2(positionSS) * _ScreenParams.xy;

    float2 Xi;
    Xi.x = GetBNDSequenceSample(positionSS, _FrameCount, 0);
    Xi.y = GetBNDSequenceSample(positionSS, _FrameCount, 1);
    
    float perceptualRoughness;
    GetNormalAndPerceptualRoughness(positionSS, N, perceptualRoughness);

    srcPerceptualRoughness = perceptualRoughness;

    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    float3x3 localToWorld = GetLocalFrame(N);

    float coefBias = _SsrPBRBias / roughness;
    Xi.x = lerp(Xi.x, 0.0f, roughness * coefBias);

    float  deviceDepth = GetDepthSample(positionSS);

    float2 positionNDC = positionSS * _ScreenSize.zw + (0.5 * _ScreenSize.zw);
    positionWS = ComputeWorldSpacePosition(positionNDC, deviceDepth, SSR_MATRIX_I_VP);
    V = GetWorldSpaceNormalizeViewDir(positionWS);

#ifdef SAMPLES_VNDF
    SampleGGX_VNDF(roughness,
        localToWorld,
        V,
        Xi,
        L,
        weight);

    NdotV = dot(N, V);
    NdotL = dot(N, L);
    float3 H = normalize(V + L);
    NdotH = dot(N, H);
    VdotH = dot(V, H);
#else
    SampleGGXDir(Xi, V, localToWorld, roughness, L, NdotL, NdotH, VdotH);

    NdotV = dot(N, V);
    float Vg = V_SmithJointGGX(NdotL, NdotV, roughness);

    weight = 4.0f * NdotL * VdotH * Vg / NdotH;
#endif
}

// Ported from HDRP
half4 ScreenSpaceReflection(uint2 positionSS)
{
    // TODO: Add stencil prepass in deferred rendering path
#ifndef _DEFERRED_RENDERING_PATH
    bool doesntReceiveSSR = false;
    uint stencilValue = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, positionSS.xy));
    doesntReceiveSSR = (stencilValue & STENCIL_USAGE_IS_SSR) == 0;
    if (doesntReceiveSSR)
    {
        return half4(0, 0, 0, 0);
    }
#endif

    float deviceDepth = GetDepthSample(positionSS);

#ifdef SSR_APPROX
    float3 N;
    float perceptualRoughness;
    GetNormalAndPerceptualRoughness(positionSS, N, perceptualRoughness);
    float2 positionNDC = positionSS * _ScreenSize.zw + (0.5 * _ScreenSize.zw); // Should we precompute the half-texel bias? We seem to use it a lot.
    float3 positionWS = ComputeWorldSpacePosition(positionNDC, deviceDepth, SSR_MATRIX_I_VP); // Jittered
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);

    float3 R = reflect(-V, N);
#else
    float weight;
    float NdotL, NdotH, VdotH, NdotV;
    float3 R, V, N;
    float3 positionWS;
    float perceptualRoughness;
    GetHitInfos(positionSS, perceptualRoughness, positionWS, weight, N, R, V, NdotL, NdotH, VdotH, NdotV);

    if (NdotL < 0.001f || weight < 0.001f)
    {
        float4(0, 0, 0, 0);
    }
#endif

    float3 camPosWS = GetCurrentViewPosition();

    // Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
    positionWS = camPosWS + (positionWS - camPosWS) * (1 - 0.001 * rcp(max(dot(N, V), FLT_EPS)));
    deviceDepth = ComputeNormalizedDeviceCoordinatesWithZ(positionWS, SSR_MATRIX_VP).z;
    bool killRay = deviceDepth == UNITY_RAW_FAR_CLIP_VALUE;

    // Ref. #1: Michal Drobot - Quadtree Displacement Mapping with Height Blending.
    // Ref. #2: Yasin Uludag  - Hi-Z Screen-Space Cone-Traced Reflections.
    // Ref. #3: Jean-Philippe Grenier - Notes On Screen Space HIZ Tracing.
    // Warning: virtually all the code below assumes reverse Z.

    // We start tracing from the center of the current pixel, and do so up to the far plane.
    float3 rayOrigin = float3(positionSS + 0.5, deviceDepth);

    float3 reflPosWS  = positionWS + R;
    float3 reflPosNDC = ComputeNormalizedDeviceCoordinatesWithZ(reflPosWS, SSR_MATRIX_VP); // Jittered
    float3 reflPosSS  = float3(reflPosNDC.xy * _ScreenSize.xy, reflPosNDC.z);
    float3 rayDir     = reflPosSS - rayOrigin;
    float3 rcpRayDir  = rcp(rayDir);
    int2   rayStep    = int2(rcpRayDir.x >= 0 ? 1 : 0,
                             rcpRayDir.y >= 0 ? 1 : 0);
    float3 raySign  = float3(rcpRayDir.x >= 0 ? 1 : -1,
                             rcpRayDir.y >= 0 ? 1 : -1,
                             rcpRayDir.z >= 0 ? 1 : -1);
    bool   rayTowardsEye  =  rcpRayDir.z >= 0;

    // Note that we don't need to store or read the perceptualRoughness value
    // if we mark stencil during the G-Buffer pass with pixels which should receive SSR,
    // and sample the color pyramid during the lighting pass.
    killRay = killRay || (reflPosSS.z <= 0);
    killRay = killRay || (dot(N, V) <= 0);
    killRay = killRay || (perceptualRoughness > _SsrRoughnessFadeEnd);
#ifndef SSR_TRACE_TOWARDS_EYE
    killRay = killRay || rayTowardsEye;
#endif

    if (killRay)
    {
        return float4(0, 0, 0, 0);
    }

    // Extend and clip the end point to the frustum.
    float tMax;
    {
        // Shrink the frustum by half a texel for efficiency reasons.
        const float halfTexel = 0.5;

        float3 bounds;
        bounds.x = (rcpRayDir.x >= 0) ? _ScreenSize.x - halfTexel : halfTexel;
        bounds.y = (rcpRayDir.y >= 0) ? _ScreenSize.y - halfTexel : halfTexel;
        // If we do not want to intersect the skybox, it is more efficient to not trace too far.
        float maxDepth = _SsrReflectsSky != 0 ? -0.00000024 : 0.00000024; // 2^-22
        bounds.z = (rcpRayDir.z >= 0) ? 1 : maxDepth;

        float3 dist = bounds * rcpRayDir - (rayOrigin * rcpRayDir);
        tMax = Min3(dist.x, dist.y, dist.z);
    }

    // Clamp the MIP level to give the compiler more information to optimize.
    const int maxMipLevel = min(_SsrDepthPyramidMaxMip, 14);

    // Start ray marching from the next texel to avoid self-intersections.
    float t;
    {
        // 'rayOrigin' is the exact texel center.
        float2 dist = abs(0.5 * rcpRayDir.xy);
        t = min(dist.x, dist.y);
    }

    float3 rayPos;

    int  mipLevel  = 0;
    int  iterCount = 0;
    bool hit       = false;
    bool miss      = false;
    bool belowMip0 = false; // This value is set prior to entering the cell

    while (!(hit || miss) && t <= tMax && iterCount < SSR_STEPS)
    {
        rayPos = rayOrigin + t * rayDir;

        // Ray position often ends up on the edge. To determine (and look up) the right cell,
        // we need to bias the position by a small epsilon in the direction of the ray.
        float2 sgnEdgeDist = round(rayPos.xy) - rayPos.xy;
        float2 satEdgeDist = clamp(raySign.xy * sgnEdgeDist + SSR_TRACE_EPS, 0, SSR_TRACE_EPS);
        rayPos.xy += raySign.xy * satEdgeDist;

        int2 mipCoord  = (int2)rayPos.xy >> mipLevel;
        int2 mipOffset = _DepthPyramidMipLevelOffsets[mipLevel];
        // Bounds define 4 faces of a cube:
        // 2 walls in front of the ray, and a floor and a base below it.
        float4 bounds;

        bounds.xy = (mipCoord + rayStep) << mipLevel;
        bounds.z  = LOAD_TEXTURE2D_X(_DepthPyramid, mipOffset + mipCoord).r;

        // We define the depth of the base as the depth value as:
        // b = DeviceDepth((1 + thickness) * LinearDepth(d))
        // b = ((f - n) * d + n * (1 - (1 + thickness))) / ((f - n) * (1 + thickness))
        // b = ((f - n) * d - n * thickness) / ((f - n) * (1 + thickness))
        // b = d / (1 + thickness) - n / (f - n) * (thickness / (1 + thickness))
        // b = d * k_s + k_b
        bounds.w = bounds.z * _SsrThicknessScale + _SsrThicknessBias;

        float4 dist      = bounds * rcpRayDir.xyzz - (rayOrigin.xyzz * rcpRayDir.xyzz);
        float  distWall  = min(dist.x, dist.y);
        float  distFloor = dist.z;
        float  distBase  = dist.w;

        // Note: 'rayPos' given by 't' can correspond to one of several depth values:
        // - above or exactly on the floor
        // - inside the floor (between the floor and the base)
        // - below the base
    #if 0
        bool belowFloor  = (raySign.z * (t - distFloor)) <  0;
        bool aboveBase   = (raySign.z * (t - distBase )) >= 0;
    #else
        bool belowFloor  = rayPos.z  < bounds.z;
        bool aboveBase   = rayPos.z >= bounds.w;
    #endif
        bool insideFloor = belowFloor && aboveBase;
        bool hitFloor    = (t <= distFloor) && (distFloor <= distWall);

        // Game rules:
        // * if the closest intersection is with the wall of the cell, switch to the coarser MIP, and advance the ray.
        // * if the closest intersection is with the heightmap below,  switch to the finer   MIP, and advance the ray.
        // * if the closest intersection is with the heightmap above,  switch to the finer   MIP, and do NOT advance the ray.
        // Victory conditions:
        // * See below. Do NOT reorder the statements!

    #ifdef SSR_TRACE_BEHIND_OBJECTS
        miss      = belowMip0 && insideFloor;
    #else
        miss      = belowMip0;
    #endif
        hit       = (mipLevel == 0) && (hitFloor || insideFloor);
        belowMip0 = (mipLevel == 0) && belowFloor;

        // 'distFloor' can be smaller than the current distance 't'.
        // We can also safely ignore 'distBase'.
        // If we hit the floor, it's always safe to jump there.
        // If we are at (mipLevel != 0) and we are below the floor, we should not move.
        t = hitFloor ? distFloor : (((mipLevel != 0) && belowFloor) ? t : distWall);
        rayPos.z = bounds.z; // Retain the depth of the potential intersection

        // Warning: both rays towards the eye, and tracing behind objects has linear
        // rather than logarithmic complexity! This is due to the fact that we only store
        // the maximum value of depth, and not the min-max.
        mipLevel += (hitFloor || belowFloor || rayTowardsEye) ? -1 : 1;
        mipLevel  = clamp(mipLevel, 0, maxMipLevel);
        
        iterCount++;
    }

    // Treat intersections with the sky as misses.
    miss = miss || ((_SsrReflectsSky == 0) && (rayPos.z == 0));
    hit  = hit && !miss;

    if (hit)
    {
        // Note that we are using 'rayPos' from the penultimate iteration, rather than
        // recompute it using the last value of 't', which would result in an overshoot.
        // It also needs to be precisely at the center of the pixel to avoid artifacts.
        float2 hitPositionNDC = floor(rayPos.xy) * _ScreenSize.zw + 0.5 * _ScreenSize.zw; // Should we precompute the half-texel bias? We seem to use it a lot.
        return float4(hitPositionNDC.xy, 0, 0);
    }

    return float4(0, 0, 0, 0);
}

#if defined(SSR_REPROJECT) || defined(SSR_ACCUMULATE)

float2 GetHitNDC(float2 positionNDC)
{
    // TODO: it's important to account for occlusion/disocclusion to avoid artifacts in motion.
    // This would require keeping the depth buffer from the previous frame.
    float2 motionVectorNDC;
    DecodeMotionVector(SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, min(positionNDC, 1.0f - 0.5f * _ScreenSize.zw) * _RTHandleScale.xy, 0), motionVectorNDC);
    float2 prevFrameNDC = positionNDC - motionVectorNDC;
    return prevFrameNDC;
}
#endif // defined(SSR_REPROJECT) || defined(SSR_ACCUMULATE)

#ifdef SSR_REPROJECT
float3 GetHitColor(float2 hitPositionNDC, float perceptualRoughness, out float opacity, int mipLevel = 0)
{
    float2 prevFrameNDC = GetHitNDC(hitPositionNDC);
    float2 prevFrameUV = prevFrameNDC * _ColorPyramidUvScaleAndLimitPrevFrame.xy;

    float tmpCoef = PerceptualRoughnessFade(perceptualRoughness, _SsrRoughnessFadeRcpLength, _SsrRoughnessFadeEndTimesRcpLength);
    opacity = EdgeOfScreenFade(prevFrameNDC, _SsrEdgeFadeRcpLength) * tmpCoef;
    return SAMPLE_TEXTURE2D_X_LOD(_ColorPyramidTexture, sampler_PointClamp, prevFrameUV, mipLevel).rgb;
}

float3 GetWorldSpacePosition(uint2 positionSS)
{
    float  deviceDepth = GetDepthSample(positionSS);
    float2 positionNDC = positionSS *_ScreenSize.zw + (0.5 * _ScreenSize.zw);
    return ComputeWorldSpacePosition(positionNDC, deviceDepth, SSR_MATRIX_I_VP);
}

float2 GetWorldSpacePoint(uint2 positionSS, out float3 positionSrcWS, out float3 positionDstWS)
{
    positionSrcWS = GetWorldSpacePosition(positionSS);

    float2 hitData = _SsrHitPointTexture[COORD_TEXTURE2D_X(positionSS) * DOWNSAMPLE].xy;
    uint2 positionDstSS = (hitData.xy - (0.5 * _ScreenSize.zw)) / _ScreenSize.zw;

    positionDstWS = GetWorldSpacePosition(positionDstSS);

    return hitData.xy;
}

float2 GetSampleInfo(uint2 positionSS, out float3 color, out float weight, out float opacity)
{
    float3 positionSrcWS;
    float3 positionDstWS;
    float2 hitData = GetWorldSpacePoint(positionSS, positionSrcWS, positionDstWS);

    float3 V = GetWorldSpaceNormalizeViewDir(positionSrcWS);
    float3 L = normalize(positionDstWS - positionSrcWS);
    float3 H = normalize(V + L);

    float3 N;
    float perceptualRoughness;
    GetNormalAndPerceptualRoughness(positionSS, N, perceptualRoughness);

    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);

    roughness = clamp(roughness, MIN_GGX_ROUGHNESS, MAX_GGX_ROUGHNESS);

    weight = GetSSRSampleWeight(V, L, roughness);

    color = GetHitColor(hitData.xy, perceptualRoughness, opacity, 0);

    return hitData;
}

float4 ScreenSpaceReflectionReprojection(uint2 positionSS0)
{
    float3 N;
    float perceptualRoughness;
    GetNormalAndPerceptualRoughness(positionSS0, N, perceptualRoughness);

    // Compute the actual roughness
    // float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    // roughness = clamp(roughness, MIN_GGX_ROUGHNESS, MAX_GGX_ROUGHNESS);

    float2 hitPositionNDC = LOAD_TEXTURE2D_X(_SsrHitPointTexture, positionSS0 * DOWNSAMPLE).xy;

    if (max(hitPositionNDC.x, hitPositionNDC.y) == 0)
    {
        // Miss.
        return 0;
    }

    // float  depthOrigin = GetDepthSample(positionSS0.xy);
    // PositionInputs posInputOrigin = GetPositionInput(positionSS0.xy, _ScreenSize.zw, depthOrigin, SSR_MATRIX_I_VP, SSR_MATRIX_VP, uint2(8, 8));
    // float3 originWS = posInputOrigin.positionWS + _WorldSpaceCameraPos;

    // TODO: this texture is sparse (mostly black). Can we avoid reading every texel? How about using Hi-S?
    float2 motionVectorNDC;
    DecodeMotionVector(SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, min(hitPositionNDC, 1.0f - 0.5f * _ScreenSize.zw) * _RTHandleScale.xy, 0), motionVectorNDC);
    float2 prevFrameNDC = hitPositionNDC - motionVectorNDC;
    float2 prevFrameUV = prevFrameNDC * _ColorPyramidUvScaleAndLimitPrevFrame.xy;

    // TODO: filtering is quite awful. Needs to be non-Gaussian, bilateral and anisotropic.
    float  mipLevel = lerp(0, _SsrColorPyramidMaxMip, perceptualRoughness);
    
    float2 diffLimit = _ColorPyramidUvScaleAndLimitPrevFrame.xy - _ColorPyramidUvScaleAndLimitPrevFrame.zw;
    float2 diffLimitMipAdjusted = diffLimit * pow(2.0,1.5 + ceil(abs(mipLevel)));
    float2 limit = _ColorPyramidUvScaleAndLimitPrevFrame.xy - diffLimitMipAdjusted;
    if (any(prevFrameUV < float2(0.0,0.0)) || any(prevFrameUV > limit))
    {
        // Off-Screen.
        return 0;
    }
    float  opacity  = EdgeOfScreenFade(prevFrameNDC, _SsrEdgeFadeRcpLength)
                    * PerceptualRoughnessFade(perceptualRoughness, _SsrRoughnessFadeRcpLength, _SsrRoughnessFadeEndTimesRcpLength);

#ifdef SSR_APPROX
    // Note that the color pyramid uses it's own viewport scale, since it lives on the camera.
    float3 color = SAMPLE_TEXTURE2D_X_LOD(_ColorPyramidTexture, sampler_PointClamp, prevFrameUV, mipLevel).rgb; /* s_trilinear_clamp_sampler in URP? */

    // Disable SSR for negative, infinite and NaN history values.
    uint3 intCol   = asuint(color);
    bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;

    color   = isPosFin ? color   : 0;
    opacity = isPosFin ? opacity : 0;
    
    return float4(color * _SSRIntensity, 1.0f) * opacity;
#else
    float3 color = 0.0f;
    
#define BLOCK_SAMPLE_RADIUS 1
    int samplesCount = 0;
    float4 outputs = 0.0f;
    float wAll = 0.0f;
    for (int y = -BLOCK_SAMPLE_RADIUS; y <= BLOCK_SAMPLE_RADIUS; ++y)
    {
        for (int x = -BLOCK_SAMPLE_RADIUS; x <= BLOCK_SAMPLE_RADIUS; ++x)
        {
            if (abs(x) == abs(y) && abs(x) == 1)
                continue;

            uint2 positionSS = uint2(int2(positionSS0) + int2(x, y));

            float3 color;
            float opacity;
            float weight;
            float2 hitData = GetSampleInfo(positionSS, color, weight, opacity);
            if (max(hitData.x, hitData.y) != 0.0f && opacity > 0.0f)
            {
                //// Note that the color pyramid uses it's own viewport scale, since it lives on the camera.
                // Disable SSR for negative, infinite and NaN history values.
                uint3 intCol   = asuint(color);
                bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;

                float2 prevFrameUV = hitData * _ColorPyramidUvScaleAndLimitPrevFrame.xy;

                color   = isPosFin ? color : 0;

                outputs += weight * float4(color, 1.0f);
                wAll += weight;
            }
        }
    }
#undef BLOCK_SAMPLE_RADIUS

    if (wAll > 0.0f)
    {
        uint3 intCol = asuint(outputs.rgb);
        bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;

        outputs.rgb = isPosFin ? outputs.rgb : 0;
        opacity     = isPosFin ? opacity : 0;
        wAll = isPosFin ? wAll : 0;

        half4 ssrColor = opacity * outputs / wAll;
        ssrColor.rgb *= _SSRIntensity;
        return ssrColor;
    }
    return 0;
#endif
}
#endif // defined(SSR_REPROJECT)

float3 Colorize(float3 current, float3 color)
{
    float3 tmp = color * current;
    return tmp / max(tmp.r, max(tmp.g, tmp.b));
}
#endif