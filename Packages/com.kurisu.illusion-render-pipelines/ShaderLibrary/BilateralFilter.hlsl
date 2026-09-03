#ifndef BILATERAL_FILTER_INCLUDED
#define BILATERAL_FILTER_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#ifdef _DEFERRED_RENDERING_PATH
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#endif
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

#ifndef _DEFERRED_RENDERING_PATH
    #define _DEFERRED_RENDERING_PATH 0
#endif

// Depth buffer of the current frame
TEXTURE2D_X(_DepthTexture);
// TEXTURE2D_X_UINT2(_StencilTexture);

#if !_DEFERRED_RENDERING_PATH && defined(BILATERAL_ROUGHNESS)
    TEXTURE2D_X_HALF(_ForwardGBuffer);
#endif

// ----------------------------------------------------------------------------
// Denoising Kernel
// ----------------------------------------------------------------------------

// Couple helper functions
float sqr(float value)
{
    return value * value;
}
float gaussian(float radius, float sigma)
{
    return exp(-sqr(radius / sigma));
}

// Bilateral filter parameters
#define NORMAL_WEIGHT   1.0
#define PLANE_WEIGHT    1.0
#define DEPTH_WEIGHT    1.0

struct BilateralData
{
    float3 position;
    float  z01;
    float  zNF;
    float3 normal;
#if defined(BILATERAL_ROUGHNESS)
    float roughness;
#endif
#if defined(BILATERLAL_UNLIT)
    bool isUnlit;
#endif
};

// We have fixed it in URP version since we store smoothness instead of perceptual roughness
void GetNormalAndPerceptualRoughness(uint2 positionSS, out float3 N, out float perceptualRoughness)
{
#if _DEFERRED_RENDERING_PATH
    half4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, positionSS);
    N = normalize(UnpackNormal(gbuffer2));
#else
    N = LoadSceneNormals(positionSS);
#endif
    
#ifdef BILATERAL_ROUGHNESS
    #if _DEFERRED_RENDERING_PATH
        float smoothness = gbuffer2.a;
    #else
        float4 gbuffer = LOAD_TEXTURE2D_X(_ForwardGBuffer, positionSS);
        float smoothness = gbuffer.r;
    #endif
    perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
#else
    perceptualRoughness = 0;
#endif
}

BilateralData TapBilateralData(uint2 coordSS)
{
    BilateralData key;
    PositionInputs posInput;

    if (DEPTH_WEIGHT > 0.0 || PLANE_WEIGHT > 0.0)
    {
        posInput.deviceDepth = LOAD_TEXTURE2D_X(_DepthTexture, coordSS).r;
        key.z01 = Linear01Depth(posInput.deviceDepth, _ZBufferParams);
        key.zNF = LinearEyeDepth(posInput.deviceDepth, _ZBufferParams);
    }

    // We need to define if this pixel is unlit
#if defined(BILATERLAL_UNLIT)
    // uint stencilValue = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, coordSS));
    // key.isUnlit = (stencilValue & STENCILUSAGE_IS_UNLIT) != 0;
    key.isUnlit = false;
#endif

    if (PLANE_WEIGHT > 0.0)
    {
        posInput = GetPositionInput(coordSS, _ScreenSize.zw, posInput.deviceDepth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        key.position = posInput.positionWS;
    }

    if (NORMAL_WEIGHT > 0.0 || PLANE_WEIGHT > 0.0)
    {
        float3 normal;
        float perceptualRoughness;
        GetNormalAndPerceptualRoughness(coordSS, normal, perceptualRoughness);
        key.normal = normal;
#ifdef BILATERAL_ROUGHNESS
        key.roughness = perceptualRoughness;
#endif
    }

    return key;
}

float ComputeBilateralWeight(BilateralData center, BilateralData tap)
{
    float depthWeight    = 1.0;
    float normalWeight   = 1.0;
    float planeWeight    = 1.0;

    if (DEPTH_WEIGHT > 0.0)
    {
        depthWeight = max(0.0, 1.0 - abs(tap.z01 - center.z01) * DEPTH_WEIGHT);
    }

    if (NORMAL_WEIGHT > 0.0)
    {
        const float normalCloseness = sqr(sqr(max(0.0, dot(tap.normal, center.normal))));
        const float normalError = 1.0 - normalCloseness;
        normalWeight = max(0.0, (1.0 - normalError * NORMAL_WEIGHT));
    }

    if (PLANE_WEIGHT > 0.0)
    {
        // Change in position in camera space
        const float3 dq = center.position - tap.position;

        // How far away is this point from the original sample
        // in camera space? (Max value is unbounded)
        const float distance2 = dot(dq, dq);

        // How far off the expected plane (on the perpendicular) is this point? Max value is unbounded.
        const float planeError = max(abs(dot(dq, tap.normal)), abs(dot(dq, center.normal)));

        planeWeight = distance2 < 0.0001 ? 1.0 :
            pow(max(0.0, 1.0 - 2.0 * PLANE_WEIGHT * planeError / sqrt(distance2)), 2.0);
    }

    return depthWeight * normalWeight * planeWeight;
}
#endif