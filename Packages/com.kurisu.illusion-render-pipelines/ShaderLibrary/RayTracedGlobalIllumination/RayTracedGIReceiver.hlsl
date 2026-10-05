#ifndef ILLUSION_RAY_TRACED_GI_RECEIVER_INCLUDED
#define ILLUSION_RAY_TRACED_GI_RECEIVER_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIConstants.hlsl"

// The opaque raster surface: depth pyramid mip 0, camera normals and the Forward GBuffer smoothness.
Texture2D<float> t_RayTracedGIDepth;
Texture2D<float4> t_RayTracedGINormals;
Texture2D<float4> t_RayTracedGIForwardGBuffer;

// The previous frame's receivers: linear view depth, and the normal with the perceptual roughness.
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

// Camera textures store the screen top first unless yFlip is -1.
float3 RayTracedGICameraDirection(uint2 pixel, RayTracedGIView view)
{
    float2 ndc = (float2(RayTracedGITexel(pixel, view)) + 0.5 + view.jitter) / view.textureSizeAndScale.xy * 2.0 - 1.0;
    return normalize(view.cameraW + view.cameraU * ndc.x + view.cameraV * (ndc.y * view.yFlip));
}

// Offsets a ray origin off the reconstructed receiver; the offset grows with distance as the depth precision drops.
float3 RayTracedGIRayOrigin(float3 positionWS, float3 normalWS, float viewDepth, RayTracedGIView view)
{
    return positionWS + normalWS * (view.rayBias + viewDepth * 1e-4);
}

RayTracedGIReceiver LoadPreviousRayTracedGIReceiver(int2 pixel, RayTracedGIView view)
{
    RayTracedGIReceiver receiver = (RayTracedGIReceiver)0;
    if (any(pixel < 0) || any(pixel >= int2(view.viewportSize)))
        return receiver;
    float viewDepth = t_RayTracedGIPrevDepth.Load(int3(pixel, 0));
    if (!(viewDepth > 0.0))
        return receiver;
    float4 normalRoughness = t_RayTracedGIPrevNormalRoughness.Load(int3(pixel, 0));
    float3 direction = RayTracedGICameraDirection(uint2(pixel), view);
    receiver.valid = true;
    receiver.viewDepth = viewDepth;
    receiver.positionWS = view.cameraPosition + direction * (viewDepth / dot(direction, view.cameraForward));
    receiver.normalWS = normalize(normalRoughness.xyz);
    receiver.viewDirWS = -direction;
    receiver.perceptualRoughness = normalRoughness.w;
    return receiver;
}

// Projects a world position into a view; xy is the pixel position, z the linear view depth, or a negative depth behind it.
float3 RayTracedGIProject(float3 positionWS, RayTracedGIView view)
{
    float3 d = positionWS - view.cameraPosition;
    float z = dot(d, view.cameraW) / dot(view.cameraW, view.cameraW);
    if (z <= 0.0)
        return float3(0.0, 0.0, -1.0);
    float2 ndc = float2(dot(d, view.cameraU) / dot(view.cameraU, view.cameraU),
        dot(d, view.cameraV) / dot(view.cameraV, view.cameraV) * view.yFlip) / z;
    float2 texel = (ndc + 1.0) * 0.5 * view.textureSizeAndScale.xy - 0.5 - view.jitter;
    return float3(texel / view.textureSizeAndScale.z, dot(d, view.cameraForward));
}

RayTracedGIReceiver LoadRayTracedGIReceiver(uint2 pixel, RayTracedGIView view)
{
    RayTracedGIReceiver receiver = (RayTracedGIReceiver)0;
    if (any(pixel >= uint2(view.viewportSize)))
        return receiver;

    uint2 texel = RayTracedGITexel(pixel, view);
    float deviceDepth = t_RayTracedGIDepth.Load(int3(texel, 0));
    float viewDepth = 1.0 / (view.zBufferParams.z * deviceDepth + view.zBufferParams.w);
    if (!(viewDepth < view.farPlane * 0.9999))
        return receiver;

    float3 direction = RayTracedGICameraDirection(pixel, view);
    float3 normal = t_RayTracedGINormals.Load(int3(texel, 0)).xyz;
    float lengthSq = dot(normal, normal);
    if (lengthSq < 1e-6)
        return receiver;

    receiver.valid = true;
    receiver.viewDepth = viewDepth;
    receiver.positionWS = view.cameraPosition + direction * (viewDepth / dot(direction, view.cameraForward));
    receiver.normalWS = normal * rsqrt(lengthSq);
    receiver.viewDirWS = -direction;
    receiver.perceptualRoughness = 1.0 - t_RayTracedGIForwardGBuffer.Load(int3(texel, 0)).r;
    return receiver;
}

#endif
