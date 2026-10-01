#ifndef ILLUSION_AREA_LIGHT_EMISSIVE_MESH_INPUT_INCLUDED
#define ILLUSION_AREA_LIGHT_EMISSIVE_MESH_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _EmissiveColor;
    float4 _EmissiveColorMap_ST;
CBUFFER_END

TEXTURE2D(_EmissiveColorMap);
SAMPLER(sampler_EmissiveColorMap);

float3 AreaLightEmissiveRadiance(float2 uv)
{
    return _EmissiveColor.rgb * SAMPLE_TEXTURE2D_LOD(_EmissiveColorMap, sampler_EmissiveColorMap, TRANSFORM_TEX(uv, _EmissiveColorMap), 0).rgb;
}

#endif
