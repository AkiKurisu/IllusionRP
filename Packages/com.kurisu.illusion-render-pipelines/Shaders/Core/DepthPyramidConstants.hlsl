#ifndef DEPTH_PYRAMID_CONSTANTS
#define DEPTH_PYRAMID_CONSTANTS

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// Referenced: UnityEngine.Rendering.HighDefinition.DepthPyramidConstants
CBUFFER_START(DepthPyramidConstants)
    uint _MinDstCount;
    uint _CbDstCount;
    uint _DepthPyramidPad0;
    uint _DepthPyramidPad1;
    int2 _SrcOffset;
    int2 _SrcLimit;
    int2 _DstSize0;
    int2 _DstSize1;
    int2 _DstSize2;
    int2 _DstSize3;
    int2 _MinDstOffset0;
    int2 _MinDstOffset1;
    int2 _MinDstOffset2;
    int2 _MinDstOffset3;
    int2 _CbDstOffset0;
    int2 _CbDstOffset1;
CBUFFER_END
#endif