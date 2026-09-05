#ifndef HD_SHADOW_CONTEXT_HLSL
#define HD_SHADOW_CONTEXT_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/ShaderVariablesAreaLights.hlsl"

// Say to LightloopDefs.hlsl that we have a sahdow context struct define
#define HAVE_HD_SHADOW_CONTEXT

struct HDShadowContext
{
    StructuredBuffer<HDShadowData>  shadowDatas;
};

// HD shadow sampling bindings
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/HDShadowSampling.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/HDShadowAlgorithms.hlsl"

// @IllusionRP: plain declarations instead of GLOBAL_TEXTURE2D / GLOBAL_RESOURCE (no ray tracing register bindings).
TEXTURE2D(_ShadowmapAreaAtlas);
TEXTURE2D(_CachedAreaLightShadowmapAtlas);

StructuredBuffer<HDShadowData> _HDShadowDatas;

HDShadowContext InitShadowContext()
{
    HDShadowContext         sc;

    sc.shadowDatas = _HDShadowDatas;

    return sc;
}

#endif // HD_SHADOW_CONTEXT_HLSL
