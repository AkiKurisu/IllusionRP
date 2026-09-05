#ifndef ILLUSION_AREA_LIGHT_DEFINITION_INCLUDED
#define ILLUSION_AREA_LIGHT_DEFINITION_INCLUDED

//
// UnityEngine.Rendering.HighDefinition.CookieMode:  static fields
//
#define COOKIEMODE_NONE (0)
#define COOKIEMODE_CLAMP (1)
#define COOKIEMODE_REPEAT (2)

//
// UnityEngine.Rendering.HighDefinition.GPULightType:  static fields
//
#define GPULIGHTTYPE_RECTANGLE (6)

// Generated from UnityEngine.Rendering.HighDefinition.AreaLightData
// PackingRules = Exact
struct AreaLightData
{
    float3 positionRWS;
    uint lightLayers;
    float3 forward;
    int lightType;
    float3 right;
    float penumbraTint;
    float range;
    int cookieMode;
    int shadowIndex;
    float rangeAttenuationScale;
    float3 up;
    float rangeAttenuationBias;
    float3 color;
    float shadowDimmer;
    float4 cookieScaleOffset;
    float3 shadowTint;
    int nonLightMappedOnly;
    float minRoughness;
    int screenSpaceShadowIndex;
    float diffuseDimmer;
    float specularDimmer;
    float4 shadowMaskSelector;
    float4 size;
};

// Generated from UnityEngine.Rendering.HighDefinition.HDShadowData
// PackingRules = Exact
struct HDShadowData
{
    float3 rot0;
    float3 rot1;
    float3 rot2;
    float3 pos;
    float4 proj;
    float2 atlasOffset;
    float worldTexelSize;
    float normalBias;
    float4 zBufferParam; // @IllusionRP: real4 in HDRP, kept float so the structured buffer layout matches C# on mobile.
    float4 shadowMapSize;
    float4 shadowFilterParams0;
    float4 dirLightPCSSParams0;
    float4 dirLightPCSSParams1;
    float3 cacheTranslationDelta;
    float isInCachedAtlas;
    float4x4 shadowToWorld;
};

#endif // ILLUSION_AREA_LIGHT_DEFINITION_INCLUDED
