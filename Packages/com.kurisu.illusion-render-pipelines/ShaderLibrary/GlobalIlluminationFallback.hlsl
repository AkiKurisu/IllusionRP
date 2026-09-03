// Reference: https://github.com/jiaozi158/UnitySSGIURP
#ifndef GLOBAL_ILLUMINATION_FALLBACK_INCLUDED
#define GLOBAL_ILLUMINATION_FALLBACK_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"

// Forward+ or Deferred+ Reflection Probe Atlas
#if defined(_CLUSTER_LIGHT_LOOP)

// used by Forward+
half3 SampleReflectionProbesAtlas(half3 reflectVector, float3 positionWS, float2 normalizedScreenSpaceUV,
    half mipLevel, inout float totalWeight)
{
    half3 irradiance = half3(0.0h, 0.0h, 0.0h);
    
    uint probeIndex;
    ClusterIterator it = ClusterInit(normalizedScreenSpaceUV, positionWS, 1);
    [loop] while (ClusterNext(it, probeIndex) && totalWeight < 0.99f)
    {
        probeIndex -= URP_FP_PROBES_BEGIN;

        float weight = CalculateProbeWeight(positionWS, urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
        weight = min(weight, 1.0f - totalWeight);

        half3 sampleVector = reflectVector;
#ifdef _REFLECTION_PROBE_BOX_PROJECTION
        sampleVector = BoxProjectedCubemapDirection(reflectVector, positionWS, urp_ReflProbes_ProbePosition[probeIndex], urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
#endif // _REFLECTION_PROBE_BOX_PROJECTION
        uint maxMip = (uint)abs(urp_ReflProbes_ProbePosition[probeIndex].w) - 1;
        half probeMip = min(mipLevel, maxMip);
        float2 uv = saturate(PackNormalOctQuadEncode(sampleVector) * 0.5 + 0.5);

        float mip0 = floor(probeMip);
        float mip1 = mip0 + 1;
        float mipBlend = probeMip - mip0;
        float4 scaleOffset0 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip0];
        float4 scaleOffset1 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip1];

        float2 uv0 = uv * scaleOffset0.xy + scaleOffset0.zw;
        float2 uv1 = uv * scaleOffset1.xy + scaleOffset1.zw;

        half3 encodedIrradiance0 = half3(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, samplerurp_ReflProbes_Atlas, uv0, 0).rgb);
        half3 encodedIrradiance1 = half3(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, samplerurp_ReflProbes_Atlas, uv1, 0).rgb);
        irradiance += weight * lerp(encodedIrradiance0, encodedIrradiance1, mipBlend);
        totalWeight += weight;
    }

    return irradiance;
}

#else // (_CLUSTER_LIGHT_LOOP)

// used by Forward or Deferred
half3 SampleReflectionProbesCubemap(half3 reflectVector, half mipLevel, inout float totalWeight)
{
    totalWeight = 1.0f;
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, reflectVector, mipLevel));
    half3 color = DecodeHDREnvironment(encodedIrradiance, _GlossyEnvironmentCubeMap_HDR).rgb;
    return color;
}
#endif

half3 SampleReflectionProbes(half3 reflectVector, float3 positionWS, float2 normalizedScreenSpaceUV,
    half mipLevel, inout float totalWeight)
{
    half3 color = half3(0.0, 0.0, 0.0);

#if defined(_CLUSTER_LIGHT_LOOP)
    color = ClampToFloat16Max(SampleReflectionProbesAtlas(reflectVector, positionWS, normalizedScreenSpaceUV, mipLevel, totalWeight));
#else
    color = SampleReflectionProbesCubemap(reflectVector, mipLevel, totalWeight);
#endif
    
    return color;
}

StructuredBuffer<float4> _AmbientProbeData;

real3 SampleAmbientProbe(real3 normalWS)
{
    return SampleSH9(_AmbientProbeData, normalWS);
}
#endif