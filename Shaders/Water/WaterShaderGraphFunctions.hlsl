#ifndef ILLUSION_WATER_GRAPHFUNCTIONS_INCLUDED
#define ILLUSION_WATER_GRAPHFUNCTIONS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Water/Water.hlsl"

#if defined(ILLUSION_WATER_FORWARD_PASS)
    #undef SHADERGRAPH_SAMPLE_SCENE_DEPTH
    #define SHADERGRAPH_SAMPLE_SCENE_DEPTH(uv) SampleWaterSceneDepth(uv)

    #if !defined(_WATER_REFLECTION_LEGACY)
        float3 shadergraph_WaterEnvironmentReflection(float3 viewDirWS, float3 normalWS, float mipLevel, float3 positionWS, float2 normalizedScreenSpaceUV)
        {
            float3 reflectVector = reflect(-viewDirWS, normalWS);
            half perceptualRoughness = MipmapLevelToPerceptualRoughness(mipLevel);
            half3 bakedGI = SampleSH(normalWS);
            half normalizationFactor = SampleProbeVolumeReflectionNormalize(
                positionWS, normalWS, normalizedScreenSpaceUV, bakedGI, reflectVector);
            return GlossyEnvironmentReflection(
                reflectVector, positionWS, perceptualRoughness, 1.0h, normalizedScreenSpaceUV)
                * normalizationFactor;
        }

        #undef SHADERGRAPH_REFLECTION_PROBE
        #define SHADERGRAPH_REFLECTION_PROBE(viewDir, normalWS, lod) shadergraph_WaterEnvironmentReflection((viewDir), (normalWS), (lod), PositionWS, ScreenPosNorm.xy)
    #endif
#endif

#undef SHADERGRAPH_SAMPLE_SCENE_COLOR
#define SHADERGRAPH_SAMPLE_SCENE_COLOR(uv) shadergraph_WaterSampleSceneColor(ScreenPosNorm.xy, (uv), ScreenPos.w)

float3 shadergraph_WaterSampleSceneColor(float2 currentUV, float2 distortedUV, float surfaceEyeDepth)
{
    return SamplePreRefractionColorDistorted(currentUV, distortedUV, surfaceEyeDepth);
}

#endif // ILLUSION_WATER_GRAPHFUNCTIONS_INCLUDED
