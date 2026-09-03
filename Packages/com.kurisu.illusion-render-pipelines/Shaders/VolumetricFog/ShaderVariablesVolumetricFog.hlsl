#ifndef VOLUMETRIC_VARIABLES_INCLUDED
#define VOLUMETRIC_VARIABLES_INCLUDED

CBUFFER_START(ShaderVariablesVolumetricFog)
int _FrameCount;
uint _CustomAdditionalLightsCount;
float _Distance;
float _BaseHeight;

float _MaximumHeight;
float _GroundHeight;
float _Density;
float _Absortion;

float _ProbeVolumeContributionWeight;
float3 _Tint;

#if defined(SHADER_STAGE_COMPUTE)
    float4 _Anisotropies[MAX_VISIBLE_LIGHTS / 4];
    float4 _Scatterings[MAX_VISIBLE_LIGHTS / 4];
    float4 _RadiiSq[MAX_VISIBLE_LIGHTS / 4];
#else
    float _Anisotropies[MAX_VISIBLE_LIGHTS];
    float _Scatterings[MAX_VISIBLE_LIGHTS];
    float _RadiiSq[MAX_VISIBLE_LIGHTS];
#endif

int _MaxSteps;
float _TransmittanceThreshold;
CBUFFER_END

#if defined(SHADER_STAGE_COMPUTE)
float SampleAnisotropy(uint lightIndex)
{
    return _Anisotropies[lightIndex / 4][lightIndex % 4];
}

float SampleScattering(uint lightIndex)
{
    return _Scatterings[lightIndex / 4][lightIndex % 4];
}

float SampleRadiiSq(uint lightIndex)
{
    return _RadiiSq[lightIndex / 4][lightIndex % 4];
}
#else
float SampleAnisotropy(uint lightIndex)
{
    return _Anisotropies[lightIndex];
}

float SampleScattering(uint lightIndex)
{
    return _Scatterings[lightIndex];
}

float SampleRadiiSq(uint lightIndex)
{
    return _RadiiSq[lightIndex];
}
#endif

#endif