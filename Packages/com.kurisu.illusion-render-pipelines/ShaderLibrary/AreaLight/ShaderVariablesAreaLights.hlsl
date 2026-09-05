#ifndef ILLUSION_SHADER_VARIABLES_AREA_LIGHTS_INCLUDED
#define ILLUSION_SHADER_VARIABLES_AREA_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/AreaLightDefinition.hlsl"

CBUFFER_START(ShaderVariablesAreaLights)
    int _AreaLightCount;
    int _AreaLightPadding0;
    int _AreaLightPadding1;
    int _AreaLightPadding2;
    float4 _AreaShadowAtlasSize;        // Depth atlas size, also used for the half resolution moment atlas
    float4 _CachedAreaShadowAtlasSize;  // @IllusionRP: no cached atlas, kept so HDRP sampling code compiles unchanged
    float4 _CookieAtlasSize;
    float4 _CookieAtlasData;
CBUFFER_END

StructuredBuffer<AreaLightData> _AreaLightDatas;

// @IllusionRP: HDRP samplers come from its ShaderVariables.hlsl, map them to the Core / URP global samplers.
#define s_linear_clamp_sampler sampler_LinearClamp
#define s_point_clamp_sampler sampler_PointClamp
#define s_trilinear_clamp_sampler sampler_TrilinearClamp
#define s_linear_clamp_compare_sampler sampler_LinearClampCompare

#ifndef SHADEROPTIONS_BARN_DOOR
    #define SHADEROPTIONS_BARN_DOOR 0
#endif

// Area shadow filtering is selected by the global keyword pair AREA_SHADOW_MEDIUM / AREA_SHADOW_HIGH,
// the off variant of that pair also disables area lights entirely.
#if (defined(AREA_SHADOW_MEDIUM) || defined(AREA_SHADOW_HIGH)) && defined(SHADER_STAGE_FRAGMENT)
    #define _AREA_LIGHTS 1
#endif

#endif // ILLUSION_SHADER_VARIABLES_AREA_LIGHTS_INCLUDED
