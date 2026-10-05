#ifndef ILLUSION_RAY_TRACED_GI_CONSTANTS_INCLUDED
#define ILLUSION_RAY_TRACED_GI_CONSTANTS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/GI/ReSTIRGIParameters.h"

#define DENOISER_MODE_OFF       0
#define DENOISER_MODE_REBLUR    1
#define DENOISER_MODE_RELAX     2

// Keep in sync with RayTracedGIConstants.cs.
struct RayTracedGIView
{
    float3 cameraPosition;
    float rayBias;
    float3 cameraU;
    float farPlane;
    float3 cameraV;
    float environmentDistance;
    float3 cameraW;
    float pixelConeSpreadAngle;
    float3 cameraForward;
    float yFlip;
    float4 zBufferParams;
    float2 viewportSize;
    float2 viewportSizeInv;
    float2 jitter;
    float2 pad1;
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
