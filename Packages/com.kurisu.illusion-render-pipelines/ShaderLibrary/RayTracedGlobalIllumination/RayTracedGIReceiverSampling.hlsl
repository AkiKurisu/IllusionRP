#ifndef ILLUSION_RAY_TRACED_GI_RECEIVER_SAMPLING_INCLUDED
#define ILLUSION_RAY_TRACED_GI_RECEIVER_SAMPLING_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIReceiver.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGISecondarySurface.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/RandomSamplerState.hlsli"

static const float kMinRoughness = 0.03;

float3 SampleCosineHemisphere(float2 u, float3 normal, out float pdf)
{
    float r = sqrt(u.x);
    float phi = 2.0 * RTXDI_GI_PI * u.y;
    float3 local = float3(r * cos(phi), r * sin(phi), sqrt(max(0.0, 1.0 - u.x)));
    pdf = local.z / RTXDI_GI_PI;

    float sign = normal.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (sign + normal.z);
    float b = normal.x * normal.y * a;
    float3 tangent = float3(1.0 + sign * normal.x * normal.x * a, sign * b, -sign * normal.x);
    float3 bitangent = float3(b, sign + normal.y * normal.y * a, -normal.y);
    return normalize(tangent * local.x + bitangent * local.y + normal * local.z);
}

float G1_Smith(float roughness, float NdotL)
{
    float alpha = roughness * roughness;
    return 2.0 * NdotL / (NdotL + sqrt(alpha * alpha + (1.0 - alpha * alpha) * NdotL * NdotL));
}

float3 SampleGGX_VNDF(float3 Ve, float roughness, float2 u)
{
    float alpha = roughness * roughness;
    float3 Vh = normalize(float3(alpha * Ve.x, alpha * Ve.y, Ve.z));
    float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
    float3 T1 = lensq > 0.0 ? float3(-Vh.y, Vh.x, 0.0) / sqrt(lensq) : float3(1.0, 0.0, 0.0);
    float3 T2 = cross(Vh, T1);
    float r = sqrt(u.x);
    float phi = 2.0 * RTXDI_GI_PI * u.y;
    float t1 = r * cos(phi);
    float t2 = r * sin(phi);
    float s = 0.5 * (1.0 + Vh.z);
    t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;
    float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;
    return float3(alpha * Nh.x, alpha * Nh.y, max(0.0, Nh.z));
}

// The density of a reflection sampled from GGX's visible normals.
float GGXVisibleNormalPdf(float roughness, float3 N, float3 V, float3 L)
{
    float NoV = dot(N, V);
    if (NoV <= 0.0)
        return 0.0;
    float NoH = saturate(dot(N, normalize(L + V)));
    float alpha2 = roughness * roughness * roughness * roughness;
    float d = NoH * NoH * (alpha2 - 1.0) + 1.0;
    float D = alpha2 / (RTXDI_GI_PI * d * d);
    return G1_Smith(roughness, NoV) * D / (4.0 * NoV);
}

// Samples the receiver's lobes as the RTXDI sample's BRDF rays do, with the white albedo and reflectance of the bridge:
// the specular lobe is chosen by its reflectance against the diffuse albedo, and the density is the mixture of both lobes.
// The lobe roughness is the perceptual roughness of the sampled lobe, 1 for the diffuse lobe.
float3 SampleReceiverLobes(inout RTXDI_RandomSamplerState rng, RayTracedGIReceiver receiver, bool specular, out float pdf,
    out float lobeRoughness)
{
    float2 u = float2(RTXDI_GetNextRandom(rng), RTXDI_GetNextRandom(rng));
    lobeRoughness = 1.0;
    if (!specular)
        return SampleCosineHemisphere(u, receiver.normalWS, pdf);

    float3 N = receiver.normalWS;
    float3 V = receiver.viewDirWS;
    float roughness = max(receiver.perceptualRoughness, kMinRoughness);
    float NoV = saturate(dot(N, V));
    float G1 = NoV > 0.0 ? G1_Smith(roughness, NoV) : 0.0;
    float specularProbability = G1 / (G1 + 1.0);

    float3 direction;
    if (RTXDI_GetNextRandom(rng) < specularProbability)
    {
        float sign = N.z >= 0.0 ? 1.0 : -1.0;
        float a = -1.0 / (sign + N.z);
        float b = N.x * N.y * a;
        float3 T = float3(1.0 + sign * N.x * N.x * a, sign * b, -sign * N.x);
        float3 B = float3(b, sign + N.y * N.y * a, -N.y);
        float3 He = SampleGGX_VNDF(float3(dot(V, T), dot(V, B), NoV), roughness, u);
        direction = reflect(-V, normalize(He.x * T + He.y * B + He.z * N));
        lobeRoughness = roughness;
    }
    else
    {
        float cosinePdf;
        direction = SampleCosineHemisphere(u, N, cosinePdf);
    }

    float NoL = dot(N, direction);
    pdf = NoL > 0.0 ? lerp(NoL / RTXDI_GI_PI, GGXVisibleNormalPdf(roughness, N, V, direction), specularProbability) : 0.0;
    return direction;
}

#endif
