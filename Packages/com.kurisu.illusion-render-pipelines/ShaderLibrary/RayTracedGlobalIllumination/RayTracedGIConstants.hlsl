#ifndef ILLUSION_RAY_TRACED_GI_CONSTANTS_INCLUDED
#define ILLUSION_RAY_TRACED_GI_CONSTANTS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/GI/ReSTIRGIParameters.h"

#define DENOISER_MODE_OFF       0
#define DENOISER_MODE_REBLUR    1
#define DENOISER_MODE_RELAX     2

// Keep in sync with RayTracedGIConstants.cs.
struct RayTracedGIView
{
    // The projection the camera depth was rendered with, between camera-relative world positions and camera texel positions
    // with texel centers at +0.5 and the device depth as z.
    row_major float4x4 worldToTexel;
    row_major float4x4 texelToWorld;
    float3 cameraPosition;
    float rayBias;
    float3 cameraForward;
    float farDepth;
    float environmentDistance;
    // The width of a pixel's cone at a view depth is pixelConeWidth + viewDepth * pixelConeSpreadAngle.
    float pixelConeWidth;
    float pixelConeSpreadAngle;
    uint orthographic;
    float2 viewportSize;
    float2 viewportSizeInv;
    // xy: the camera texture size, z: camera texels per traced pixel.
    float4 textureSizeAndScale;
};

struct RayTracedGIConstants
{
    RayTracedGIView view;
    RayTracedGIView prevView;
    RTXDI_RuntimeParameters runtimeParams;
    RTXDI_GIParameters restirGI;
    uint denoiserMode;
    uint enableAccumulation;
    uint historyValid;
    uint motionVectors;
    float4 reblurHitDistParams;
    uint specular;
    float radianceClamp;
    uint pad2;
    uint pad3;
};

StructuredBuffer<RayTracedGIConstants> t_RayTracedGIConstants;

#endif
