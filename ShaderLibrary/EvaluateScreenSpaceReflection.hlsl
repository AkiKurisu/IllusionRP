#ifndef EVALUATE_SCREEN_SPACE_REFLECTION_INCLUDED
#define EVALUATE_SCREEN_SPACE_REFLECTION_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"

#ifndef _SCREEN_SPACE_REFLECTION
    #define _SCREEN_SPACE_REFLECTION 0
#endif

#define TRANSPARENT_RECEIVE_SSR   (defined(_SURFACE_TYPE_TRANSPARENT) && defined(_TRANSPARENT_WRITE_DEPTH))

#define SURFACE_TYPE_RECEIVE_SSR  (!defined(_SURFACE_TYPE_TRANSPARENT) || TRANSPARENT_RECEIVE_SSR)

TEXTURE2D(_SsrLightingTexture);

half4 SampleScreenSpaceReflection(float2 normalizedScreenSpaceUV) 
{
    float2 positionSS = normalizedScreenSpaceUV * _ScreenSize.xy;
    float4 ssrLighting = LOAD_TEXTURE2D(_SsrLightingTexture, positionSS);
    ssrLighting.rgb *= GetInverseCurrentExposureMultiplier();
    return ssrLighting;
}
#endif