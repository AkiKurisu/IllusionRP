#ifndef BILATERAL_UPSAMPLE_VARIABLES_INCLUDED
#define BILATERAL_UPSAMPLE_VARIABLES_INCLUDED

// Params
CBUFFER_START(ShaderVariablesBilateralUpsample)
    float4 _HalfScreenSize;
    float4 _DistanceBasedWeights[12];
    float4 _TapOffsets[8];
CBUFFER_END

#endif
