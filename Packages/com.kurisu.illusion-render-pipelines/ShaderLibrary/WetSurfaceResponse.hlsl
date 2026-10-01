#ifndef ILLUSION_WET_SURFACE_RESPONSE_INCLUDED
#define ILLUSION_WET_SURFACE_RESPONSE_INCLUDED

struct WetSurfaceResponse
{
    float darkness;
    float specularWeight;
    float3 specular;
    float smoothness;
};

float WetSurfaceQuantize8(float value)
{
    return round(saturate(value) * 255) / 255;
}

float3 WetSurfaceQuantize8(float3 value)
{
    return round(saturate(value) * 255) / 255;
}

float WetSurfaceSpecularGate(float3 quantizedSpecular)
{
    return 1 - saturate((max(max(quantizedSpecular.r, quantizedSpecular.g), quantizedSpecular.b) - 0.5) * 1000);
}

void WetSurfaceWeights(float smoothness, float wetness, float gate,
    out float darkness, out float specularWeight)
{
    darkness = 0.8 * gate * saturate((0.8 - smoothness) * 1.111111);
    darkness *= gate > 0 ? saturate(wetness / (0.8 * gate)) : 0;
    specularWeight = saturate((wetness - 0.8 * gate) / (1 - 0.8 * gate));
}

float4 PackWetSurfaceForwardData(float screenSmoothness, float sourceSmoothness, float3 sourceSpecular)
{
    float gate = WetSurfaceSpecularGate(WetSurfaceQuantize8(sourceSpecular));
    return float4(screenSmoothness, WetSurfaceQuantize8(sourceSmoothness), gate, 1 - gate);
}

bool IsWetSurfaceForwardDataValid(float4 packed)
{
    return (packed.b > 0.5 && packed.a < 0.5) ||
        (packed.a > 0.5 && packed.b < 0.5);
}

float ApplyWetSurfaceSmoothness(float smoothness, float darkness, float specularWeight)
{
    float baseWetSmoothness = 1 - (1 - smoothness) * (1 - darkness);
    return lerp(baseWetSmoothness, 1, specularWeight);
}

WetSurfaceResponse EvaluateWetSurfaceResponse(float3 specular, float smoothness, float wetness)
{
    WetSurfaceResponse response;
    float gate = WetSurfaceSpecularGate(specular);
    WetSurfaceWeights(smoothness, wetness, gate, response.darkness, response.specularWeight);
    response.specular = lerp(specular, max(specular, 0.3), response.specularWeight);
    response.smoothness = ApplyWetSurfaceSmoothness(smoothness, response.darkness, response.specularWeight);
    return response;
}

float WetSurfaceSmoothnessFromPacked(float4 packed, float wetness)
{
    float darkness;
    float specularWeight;
    WetSurfaceWeights(packed.g, wetness, packed.b > 0.5 ? 1 : 0, darkness, specularWeight);
    return ApplyWetSurfaceSmoothness(packed.g, darkness, specularWeight);
}

#endif
