#ifndef EVALUATE_SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED
#define EVALUATE_SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"

#ifndef _SCREEN_SPACE_GLOBAL_ILLUMINATION
    #define _SCREEN_SPACE_GLOBAL_ILLUMINATION 0
#endif

#define TRANSPARENT_RECEIVE_SSGI   (defined(_SURFACE_TYPE_TRANSPARENT) && defined(_TRANSPARENT_WRITE_DEPTH))

// Treat hair always receive ssgi when using OIT.
#define SURFACE_TYPE_RECEIVE_SSGI  (!defined(_SURFACE_TYPE_TRANSPARENT) || TRANSPARENT_RECEIVE_SSGI || defined(_HAIR_ORDER_INDEPENDENT))

TEXTURE2D(_IndirectDiffuseTexture);

half4 SampleScreenSpaceGlobalIllumination(float2 normalizedScreenSpaceUV) 
{
    float2 positionSS = normalizedScreenSpaceUV * _ScreenSize.xy;
    float4 ssgiLighting = LOAD_TEXTURE2D(_IndirectDiffuseTexture, positionSS);
    ssgiLighting.rgb *= GetInverseCurrentExposureMultiplier();
    return ssgiLighting;
}
#endif