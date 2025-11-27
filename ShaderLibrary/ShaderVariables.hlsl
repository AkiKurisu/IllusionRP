#ifndef ILLUSION_SHADER_VARIABLES_INCLUDED
#define ILLUSION_SHADER_VARIABLES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Params
GLOBAL_CBUFFER_START(ShaderVariablesGlobal, b1)
    float4x4 _GlobalViewMatrix;
    float4x4 _GlobalViewProjMatrix;
    float4x4 _GlobalInvViewProjMatrix;
    float4x4 _GlobalPrevInvViewProjMatrix;
    float4 _RTHandleScale;
    float4 _RTHandleScaleHistory;
    float4 _TaaFrameInfo;
    float4 _ColorPyramidUvScaleAndLimitPrevFrame;

    int _IndirectDiffuseMode;
    float _IndirectDiffuseLightingMultiplier;
    uint _IndirectDiffuseLightingLayers;
CBUFFER_END

// Matrix override
#define G_MATRIX_VP      _GlobalViewProjMatrix      // Jittered
#define G_MATRIX_I_VP    _GlobalInvViewProjMatrix
#define G_MATRIX_V       _GlobalViewMatrix

// Exposure texture - 1x1 RG16F (r: exposure mult, g: exposure EV100)
TEXTURE2D(_ExposureTexture);
TEXTURE2D(_PrevExposureTexture);

#define SHADEROPTIONS_PRE_EXPOSITION (1)

// Functions to clamp UVs to use when RTHandle system is used.
float2 ClampAndScaleUV(float2 UV, float2 texelSize, float numberOfTexels, float2 scale)
{
    float2 maxCoord = 1.0f - numberOfTexels * texelSize;
    return min(UV, maxCoord) * scale;
}

float2 ClampAndScaleUV(float2 UV, float2 texelSize, float numberOfTexels)
{
    return ClampAndScaleUV(UV, texelSize, numberOfTexels, _RTHandleScale.xy);
}

// This is assuming half a texel offset in the clamp.
float2 ClampAndScaleUVForBilinear(float2 UV, float2 texelSize)
{
    return ClampAndScaleUV(UV, texelSize, 0.5f);
}

// This is assuming full screen buffer and half a texel offset for the clamping.
float2 ClampAndScaleUVForBilinear(float2 UV)
{
    return ClampAndScaleUV(UV, _ScreenSize.zw, 0.5f);
}

float2 ClampAndScaleUVForPoint(float2 UV)
{
    return min(UV, 1.0f) * _RTHandleScale.xy;
}

float GetCurrentExposureMultiplier()
{
#if SHADEROPTIONS_PRE_EXPOSITION
    // _ProbeExposureScale is a scale used to perform range compression to avoid saturation of the content of the probes. It is 1.0 if we are not rendering probes.
    return LOAD_TEXTURE2D(_ExposureTexture, int2(0, 0)).x;
#else
    return 1.0f;
#endif
}

float GetPreviousExposureMultiplier()
{
#if SHADEROPTIONS_PRE_EXPOSITION
    // _ProbeExposureScale is a scale used to perform range compression to avoid saturation of the content of the probes. It is 1.0 if we are not rendering probes.
    return LOAD_TEXTURE2D(_PrevExposureTexture, int2(0, 0)).x;
#else
    return 1.0f;
#endif
}

float GetInverseCurrentExposureMultiplier()
{
    float exposure = GetCurrentExposureMultiplier();
    return rcp(exposure + (exposure == 0.0)); // zero-div guard
}

float GetInversePreviousExposureMultiplier()
{
    float exposure = GetPreviousExposureMultiplier();
    return rcp(exposure + (exposure == 0.0)); // zero-div guard
}

// This method should be used for rendering any full screen quad that uses an auto-scaling Render Targets (see RTHandle/HDCamera)
// It will account for the fact that the textures it samples are not necesarry using the full space of the render texture but only a partial viewport.
float2 GetNormalizedFullScreenTriangleTexCoord(uint vertexID)
{
    return GetFullScreenTriangleTexCoord(vertexID) * _RTHandleScale.xy;
}

// Helper function for indirect control volume
float GetIndirectDiffuseMultiplier(uint renderingLayers)
{
    return (_IndirectDiffuseLightingLayers & renderingLayers) ? _IndirectDiffuseLightingMultiplier : 1.0f;
}
#endif