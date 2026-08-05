#ifndef ILLUSION_WATER_GRAPHFUNCTIONS_INCLUDED
#define ILLUSION_WATER_GRAPHFUNCTIONS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Water/Water.hlsl"

#undef SHADERGRAPH_SAMPLE_SCENE_COLOR
#define SHADERGRAPH_SAMPLE_SCENE_COLOR(uv) shadergraph_WaterSampleSceneColor(ScreenPosNorm.xy, (uv), ScreenPos.w)

float3 shadergraph_WaterSampleSceneColor(float2 currentUV, float2 distortedUV, float surfaceEyeDepth)
{
    return SamplePreRefractionColorDistorted(currentUV, distortedUV, surfaceEyeDepth);
}

#endif // ILLUSION_WATER_GRAPHFUNCTIONS_INCLUDED
