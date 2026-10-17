#ifndef ILLUSION_RAY_TRACED_GI_APPLICATION_BRIDGE_INCLUDED
#define ILLUSION_RAY_TRACED_GI_APPLICATION_BRIDGE_INCLUDED

// The RTXDI application bridge for the resampling and final shading passes of the RTXDI sample.

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingLightTargets.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIReceiver.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGISecondarySurface.hlsl"

#define g_Const (t_RayTracedGIConstants[0])

RaytracingAccelerationStructure SceneBVH;
RWTexture2D<float4> u_DiffuseLighting;
RWTexture2D<float4> u_SpecularLighting;
Texture2D<float4> t_RayTracedGIPreIntegratedFGD;
SamplerState s_RayTracedGI_linear_clamp_sampler;

StructuredBuffer<float2> t_NeighborOffsets;

#define RTXDI_GI_RESERVOIR_BUFFER u_GIReservoirs
#define RTXDI_NEIGHBOR_OFFSETS_BUFFER t_NeighborOffsets
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/Checkerboard.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/Math.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/Color.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/RandomSamplerState.hlsli"

static const float kMinRoughness = 0.03f;

// Donut BRDF helpers used by the RTXDI sample.
float calcLuminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float Lambert(float3 normal, float3 lightIncident)
{
    return max(0, -dot(normal, lightIncident)) / RTXDI_GI_PI;
}

float3 Schlick_Fresnel(float3 F0, float VdotH)
{
    return F0 + (1 - F0) * pow(max(1 - VdotH, 0), 5);
}

float G_Smith_over_NdotV(float roughness, float NdotV, float NdotL)
{
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float g1 = NdotV * sqrt(alpha2 + (1.0 - alpha2) * NdotL * NdotL);
    float g2 = NdotL * sqrt(alpha2 + (1.0 - alpha2) * NdotV * NdotV);
    return 2.0 * NdotL / (g1 + g2);
}

float3 GGX_times_NdotL(float3 V, float3 L, float3 N, float roughness, float3 F0)
{
    float3 H = normalize(L + V);
    float NoL = saturate(dot(N, L));
    float VoH = saturate(dot(V, H));
    float NoV = saturate(dot(N, V));
    float NoH = saturate(dot(N, H));
    if (NoL > 0)
    {
        float G = G_Smith_over_NdotV(roughness, NoV, NoL);
        float alpha = roughness * roughness;
        float alpha2 = alpha * alpha;
        float d = NoH * NoH * alpha2 + (1 - NoH * NoH);
        float D = alpha2 / (RTXDI_GI_PI * d * d);
        return Schlick_Fresnel(F0, VoH) * (D * G / 4);
    }
    return 0;
}

struct RAB_Material
{
    float3 diffuseAlbedo;
    float3 specularF0;
    float roughness;
};

struct RAB_Surface
{
    float3 worldPos;
    float3 viewDir;
    float3 normal;
    float3 geoNormal;
    float viewDepth;
    float diffuseProbability;
    RAB_Material material;
};

RAB_Surface RAB_EmptySurface()
{
    RAB_Surface surface = (RAB_Surface)0;
    surface.viewDepth = 0;
    return surface;
}

bool RAB_IsSurfaceValid(RAB_Surface surface)
{
    return surface.viewDepth > 0;
}

// The receiver's material is applied by the Forward materials, so the signals are shaded with a white albedo and a white
// specular reflectance, at no less than the sample's minimum roughness.
RAB_Surface RAB_GetGBufferSurface(int2 pixelPosition, bool previousFrame)
{
    RAB_Surface surface = RAB_EmptySurface();
    if (previousFrame && !g_Const.historyValid)
        return surface;
    RayTracedGIReceiver receiver;
    if (previousFrame)
        receiver = LoadPreviousRayTracedGIReceiver(pixelPosition, g_Const.prevView);
    else
        receiver = LoadRayTracedGIReceiver(uint2(pixelPosition), g_Const.view);
    if (!receiver.valid)
        return surface;
    surface.worldPos = receiver.positionWS;
    surface.viewDir = receiver.viewDirWS;
    surface.normal = receiver.normalWS;
    surface.geoNormal = receiver.normalWS;
    surface.viewDepth = receiver.viewDepth;
    surface.diffuseProbability = 1.0;
    surface.material.diffuseAlbedo = 1.0;
    surface.material.specularF0 = g_Const.specular != 0 ? 1.0 : 0.0;
    surface.material.roughness = max(receiver.perceptualRoughness, kMinRoughness);
    return surface;
}

// Specular is published as SSR's is: the incident radiance averaged over the white GGX lobe, which materials weight with
// their own FGD.
float3 RayTracedGIDemodulateSpecular(RAB_Surface surface, float3 specular)
{
    float NoV = saturate(dot(surface.normal, surface.viewDir));
    float2 uv = 0.5 / 64.0 + float2(sqrt(NoV), surface.material.roughness) * (1.0 - 1.0 / 64.0);
    float energy = t_RayTracedGIPreIntegratedFGD.SampleLevel(s_RayTracedGI_linear_clamp_sampler, uv, 0).y;
    return specular / max(energy, 0.01);
}

float3 RAB_GetSurfaceWorldPos(RAB_Surface surface)
{
    return surface.worldPos;
}

float3 RAB_GetSurfaceNormal(RAB_Surface surface)
{
    return surface.normal;
}

float RAB_GetSurfaceLinearDepth(RAB_Surface surface)
{
    return surface.viewDepth;
}

RAB_Material RAB_GetMaterial(RAB_Surface surface)
{
    return surface.material;
}

bool RAB_AreMaterialsSimilar(RAB_Material a, RAB_Material b)
{
    const float roughnessThreshold = 0.5;
    const float reflectivityThreshold = 0.25;
    const float albedoThreshold = 0.25;

    if (!RTXDI_CompareRelativeDifference(a.roughness, b.roughness, roughnessThreshold))
        return false;

    if (abs(calcLuminance(a.specularF0) - calcLuminance(b.specularF0)) > reflectivityThreshold)
        return false;

    if (abs(calcLuminance(a.diffuseAlbedo) - calcLuminance(b.diffuseAlbedo)) > albedoThreshold)
        return false;

    return true;
}

int2 RAB_ClampSamplePositionIntoView(int2 pixelPosition, bool previousFrame)
{
    int width = int(g_Const.view.viewportSize.x);
    int height = int(g_Const.view.viewportSize.y);

    // Reflect the position across the screen edges.
    // Compared to simple clamping, this prevents the spread of colorful blobs from screen edges.
    if (pixelPosition.x < 0) pixelPosition.x = -pixelPosition.x;
    if (pixelPosition.y < 0) pixelPosition.y = -pixelPosition.y;
    if (pixelPosition.x >= width) pixelPosition.x = 2 * width - pixelPosition.x - 1;
    if (pixelPosition.y >= height) pixelPosition.y = 2 * height - pixelPosition.y - 1;

    return pixelPosition;
}

float3 RAB_SurfaceEvaluateBrdfTimesNoL(RAB_Surface surface, float3 L)
{
    float3 diffuse = Lambert(surface.normal, -L) * surface.material.diffuseAlbedo;
    float3 specular = surface.material.roughness < kMinRoughness
        ? 0.0
        : GGX_times_NdotL(surface.viewDir, L, surface.normal, max(surface.material.roughness, kMinRoughness), surface.material.specularF0);
    return diffuse + specular;
}

float RAB_GetGISampleTargetPdfForSurface(float3 samplePosition, float3 sampleRadiance, RAB_Surface surface)
{
    float3 L = normalize(samplePosition - surface.worldPos);
    return RTXDI_Luminance(sampleRadiance * RAB_SurfaceEvaluateBrdfTimesNoL(surface, L));
}

bool RAB_ValidateGISampleWithJacobian(inout float jacobian)
{
    // Sold angle ratio is too different. Discard the sample.
    if (jacobian > 10.0 || jacobian < 1 / 10.0) {
        return false;
    }

    // clamp Jacobian.
    jacobian = clamp(jacobian, 1 / 3.0, 3.0);

    return true;
}

// Visibility between the receiver and a sample follows path tracing coverage and returns its colored transmission.
float3 GetFinalVisibility(RaytracingAccelerationStructure accelStruct, RAB_Surface surface, float3 samplePosition)
{
    float3 origin = RayTracedGIRayOrigin(surface.worldPos, surface.geoNormal, surface.viewDepth, g_Const.view);
    float3 toSample = samplePosition - origin;
    float distance = length(toSample);
    if (distance <= g_Const.view.rayBias * 2.0)
        return 1.0;

    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = toSample / distance;
    ray.TMin = 0.0;
    ray.TMax = distance - g_Const.view.rayBias;

    uint seed = asuint(samplePosition.x) ^ (asuint(samplePosition.y) * 0x9E3779B9u) ^ (asuint(surface.worldPos.z) * 0x85EBCA6Bu);
    IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_VISIBILITY, seed, 0.0, 0.0);
    payload.hitT = 0.0;
    payload.diffuseOpacity = PathTracingPackHalf4(float4(1.0, 1.0, 1.0, 0.0));
    payload.parameters.x = 0xFFFFFFFFu;
    TraceRay(accelStruct, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER | PathTracingShadowRayFlags(payload.parameters.x), PT_INSTANCE_SCENE, 0, 1, 0, ray, payload);
    return payload.hitT < 0.0 ? PathTracingUnpackHalf4(payload.diffuseOpacity).rgb : 0.0;
}

bool RAB_GetConservativeVisibility(RAB_Surface surface, float3 samplePosition)
{
    return any(GetFinalVisibility(SceneBVH, surface, samplePosition) > 0.0);
}

// The scene keeps no previous acceleration structure, so temporal visibility is tested against the current one.
bool RAB_GetTemporalConservativeVisibility(RAB_Surface currentSurface, RAB_Surface previousSurface, float3 samplePosition)
{
    return RAB_GetConservativeVisibility(currentSurface, samplePosition);
}

// Screen motion from the motion vectors when the camera has them, otherwise from the previous camera; the depth change
// always assumes a static surface.
float3 RayTracedGIScreenSpaceMotion(int2 pixelPosition, RAB_Surface surface)
{
    float3 previous = RayTracedGIProject(surface.worldPos, g_Const.prevView);
    if (previous.z <= 0.0)
        return float3(-1e6, -1e6, 0.0);
    float3 motion = float3(previous.xy - float2(pixelPosition), previous.z - surface.viewDepth);
    if (g_Const.motionVectors != 0)
    {
        float2 uvMotion = t_RayTracedGIMotion.Load(int3(RayTracedGITexel(pixelPosition, g_Const.view), 0));
        motion.xy = -uvMotion * g_Const.view.textureSizeAndScale.xy / g_Const.view.textureSizeAndScale.z;
    }
    return motion;
}

#endif
