#ifndef ILLUSION_RAY_TRACED_GI_RECEIVER_INCLUDED
#define ILLUSION_RAY_TRACED_GI_RECEIVER_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIConstants.hlsl"

// The opaque raster surface: depth pyramid mip 0, camera normals and the Forward GBuffer smoothness.
Texture2D<float> t_RayTracedGIDepth;
Texture2D<float4> t_RayTracedGINormals;
Texture2D<float4> t_RayTracedGIForwardGBuffer;

// The previous frame's receivers: device depth, and the normal with the perceptual roughness.
Texture2D<float> t_RayTracedGIPrevDepth;
Texture2D<float4> t_RayTracedGIPrevNormalRoughness;
// URP motion vectors: the current minus the previous camera texture UV.
Texture2D<float2> t_RayTracedGIMotion;

struct RayTracedGIReceiver
{
    bool valid;
    float3 positionWS;
    float3 normalWS;
    float3 viewDirWS;
    float viewDepth;
    float perceptualRoughness;
};

// A traced pixel represents the first camera texel of its block.
uint2 RayTracedGITexel(uint2 pixel, RayTracedGIView view)
{
    return min(pixel * uint(view.textureSizeAndScale.z), uint2(view.textureSizeAndScale.xy) - 1);
}

// Returns the camera-relative world position of a camera texel position at a device depth.
float3 RayTracedGIUnproject(float2 texel, float deviceDepth, RayTracedGIView view)
{
    float4 position = mul(float4(texel, deviceDepth, 1.0), view.texelToWorld);
    return position.xyz / position.w;
}

// Offsets a ray origin off the reconstructed receiver; the offset grows with distance as the depth precision drops.
float3 RayTracedGIRayOrigin(float3 positionWS, float3 normalWS, float viewDepth, RayTracedGIView view)
{
    return positionWS + normalWS * (view.rayBias + viewDepth * 1e-4);
}

// Both frames reconstruct receivers from their device depth through this function, so a receiver that did not move is the
// same point in both.
RayTracedGIReceiver MakeRayTracedGIReceiver(uint2 texel, float deviceDepth, float3 normalWS, float perceptualRoughness,
    RayTracedGIView view)
{
    float3 position = RayTracedGIUnproject(float2(texel) + 0.5, deviceDepth, view);
    RayTracedGIReceiver receiver;
    receiver.valid = true;
    receiver.positionWS = view.cameraPosition + position;
    receiver.normalWS = normalWS;
    // The view direction points toward the camera, or against its forward for an orthographic camera.
    receiver.viewDirWS = view.orthographic != 0 ? -view.cameraForward : -normalize(position);
    receiver.viewDepth = dot(position, view.cameraForward);
    receiver.perceptualRoughness = perceptualRoughness;
    return receiver;
}

RayTracedGIReceiver LoadPreviousRayTracedGIReceiver(int2 pixel, RayTracedGIView view)
{
    RayTracedGIReceiver receiver = (RayTracedGIReceiver)0;
    if (any(pixel < 0) || any(pixel >= int2(view.viewportSize)))
        return receiver;
    float deviceDepth = t_RayTracedGIPrevDepth.Load(int3(pixel, 0));
    if (deviceDepth == view.farDepth)
        return receiver;
    float4 normalRoughness = t_RayTracedGIPrevNormalRoughness.Load(int3(pixel, 0));
    return MakeRayTracedGIReceiver(RayTracedGITexel(uint2(pixel), view), deviceDepth, normalize(normalRoughness.xyz),
        normalRoughness.w, view);
}

// Projects a world position into a view; xy is the pixel position, z the linear view depth, or a negative depth behind it.
float3 RayTracedGIProject(float3 positionWS, RayTracedGIView view)
{
    float3 position = positionWS - view.cameraPosition;
    float viewDepth = dot(position, view.cameraForward);
    float4 texel = mul(float4(position, 1.0), view.worldToTexel);
    if (viewDepth <= 0.0 || texel.w <= 0.0)
        return float3(0.0, 0.0, -1.0);
    return float3((texel.xy / texel.w - 0.5) / view.textureSizeAndScale.z, viewDepth);
}

RayTracedGIReceiver LoadRayTracedGIReceiver(uint2 pixel, RayTracedGIView view)
{
    RayTracedGIReceiver receiver = (RayTracedGIReceiver)0;
    if (any(pixel >= uint2(view.viewportSize)))
        return receiver;

    uint2 texel = RayTracedGITexel(pixel, view);
    float deviceDepth = t_RayTracedGIDepth.Load(int3(texel, 0));
    if (deviceDepth == view.farDepth)
        return receiver;

    float3 normal = t_RayTracedGINormals.Load(int3(texel, 0)).xyz;
    float lengthSq = dot(normal, normal);
    if (lengthSq < 1e-6)
        return receiver;

    return MakeRayTracedGIReceiver(texel, deviceDepth, normal * rsqrt(lengthSq),
        1.0 - t_RayTracedGIForwardGBuffer.Load(int3(texel, 0)).r, view);
}

#endif
