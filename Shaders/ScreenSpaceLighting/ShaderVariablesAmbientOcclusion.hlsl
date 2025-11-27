#ifndef AMBIENT_OCCLUSION_VARIABLES_INCLUDED
#define AMBIENT_OCCLUSION_VARIABLES_INCLUDED

// Reconstruct Normal
half4 _CameraViewTopLeftCorner[2];
half4x4 _CameraViewProjections[2]; // This is different from UNITY_MATRIX_VP (platform-agnostic projection matrix is used). Handle both non-XR and XR modes.
float4 _ProjectionParams2;
float4 _CameraViewXExtent[2];
float4 _CameraViewYExtent[2];
float4 _CameraViewZExtent[2];

// Params
CBUFFER_START(ShaderVariablesAmbientOcclusion)
    float4 _AOBufferSize;
    float4 _AOParams0;
    float4 _AOParams1;
    float4 _AOParams2;
    float4 _AOParams3;
    float4 _AOParams4;
    float4 _FirstTwoDepthMipOffsets;
    float4 _AODepthToViewParams;
CBUFFER_END

#endif