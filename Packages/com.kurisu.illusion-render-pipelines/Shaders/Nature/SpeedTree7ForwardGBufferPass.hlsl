#ifndef ILLUSION_SPEEDTREE7_FORWARD_GBUFFER_PASS_INCLUDED
#define ILLUSION_SPEEDTREE7_FORWARD_GBUFFER_PASS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/WetSurfaceResponse.hlsl"
float _WetSurfacePackedEnabled;

void SpeedTree7ForwardGBufferFrag(SpeedTreeVertexDepthNormalOutput input,
    out half4 outSmoothness : SV_Target0, out half4 outNormalWS : SV_Target1)
{
#ifdef UNIVERSAL_SPEEDTREE7BILLBOARD_PASSES_INCLUDED
    outNormalWS = SpeedTree7FragDepthNormalBillboard(input);
#else
    outNormalWS = SpeedTree7FragDepthNormal(input);
#endif
    outSmoothness = _WetSurfacePackedEnabled > 0.5
        ? PackWetSurfaceForwardData(0, 0, 0) : half4(0, 0, 0, 0);
}

#endif
