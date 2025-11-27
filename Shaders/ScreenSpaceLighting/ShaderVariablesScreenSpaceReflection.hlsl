#ifndef SCREEN_SPACE_REFLECTION_VARIABLES_INCLUDED
#define SCREEN_SPACE_REFLECTION_VARIABLES_INCLUDED

// Should match ScreenSpaceReflectionTracePass.ScreenSpaceReflectionVariables
CBUFFER_START(ShaderVariablesScreenSpaceReflection)
    float4x4 _SSR_ProjectionMatrix;

    float _SSRIntensity;
    float _Thickness;
    float _SsrThicknessScale;
    float _SsrThicknessBias;

    float _Steps;
    float _StepSize;
    float _SsrRoughnessFadeEnd;
    float _SsrRoughnessFadeRcpLength;

    float _SsrRoughnessFadeEndTimesRcpLength;
    float _SsrEdgeFadeRcpLength;
    int _SsrDepthPyramidMaxMip;
    float _SsrDownsamplingDivider;

    float _SsrAccumulationAmount;
    float _SsrPBRSpeedRejection;
    float _SsrPBRSpeedRejectionScalerFactor;
    float _SsrPBRBias;

    int _SsrColorPyramidMaxMip;
CBUFFER_END

#endif