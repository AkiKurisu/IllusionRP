#ifndef UNITY_SUB_SURFACE_INCLUDED
#define UNITY_SUB_SURFACE_INCLUDED

// This function does the remapping from scattering color and distance to sigmaS and sigmaT
void RemapSubSurfaceScatteringParameters(float3 albedo, float3 radius, out float3 sigmaS, out float3 sigmaT)
{
    float3 a = 1.0 - exp(albedo * (-5.09406 + albedo * (2.61188 - albedo * 4.31805)));
    float3 s = 1.9 - albedo + 3.5 * (albedo - 0.8) * (albedo - 0.8);

    sigmaT = 1.0 / max(radius * s, 1e-16);
    sigmaS = sigmaT * a;
}

// This function allows us to pick a color channel
int GetChannel(float u1, float3 channelWeight)
{
    if (channelWeight.x > u1)
        return 0;
    if ((channelWeight.x + channelWeight.y) > u1)
        return 1;
    return 2;
}

// Safe division to avoid nans
float3 SafeDivide(float3 val0, float3 val1)
{
    float3 result;
    result.x = val1.x != 0.0 ? val0.x / val1.x : 0.0;
    result.y = val1.y != 0.0 ? val0.y / val1.y : 0.0;
    result.z = val1.z != 0.0 ? val0.z / val1.z : 0.0;
    return result;
}


#endif
