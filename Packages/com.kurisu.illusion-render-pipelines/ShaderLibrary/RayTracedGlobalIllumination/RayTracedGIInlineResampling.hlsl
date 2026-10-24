#ifndef ILLUSION_RAY_TRACED_GI_INLINE_RESAMPLING_INCLUDED
#define ILLUSION_RAY_TRACED_GI_INLINE_RESAMPLING_INCLUDED

#define USE_RAY_QUERY 1
#define RTXDI_SCREEN_SPACE_GROUP_SIZE 8
#define WITH_NRD 1

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/Inline/PathTracingInline.hlsl"

#define main RayTracedGITemporalResampling
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/FullSample/LightingPasses/GI/TemporalResampling.hlsl"
#undef main

#define main RayTracedGISpatialResampling
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/FullSample/LightingPasses/GI/SpatialResampling.hlsl"
#undef main

#define main RayTracedGIFinalShading
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/FullSample/LightingPasses/GI/FinalShading.hlsl"
#undef main

void RayTracedGITemporalResamplingPixel(uint2 pixel) { RayTracedGITemporalResampling(pixel, pixel % RTXDI_SCREEN_SPACE_GROUP_SIZE); }
void RayTracedGISpatialResamplingPixel(uint2 pixel) { RayTracedGISpatialResampling(pixel); }
void RayTracedGIFinalShadingPixel(uint2 pixel) { RayTracedGIFinalShading(pixel); }

#endif
