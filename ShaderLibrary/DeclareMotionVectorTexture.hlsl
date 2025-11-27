#ifndef DECLARE_MOTION_VECTOR_TEXTURE_INCLUDED
#define DECLARE_MOTION_VECTOR_TEXTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// Per-pixel camera backwards velocity
TEXTURE2D_X(_MotionVectorTexture);

half2 SampleMotionVector(float2 uv)
{
    return SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, uv, 0).xy;
}

half2 LoadMotionVector(uint2 positionSS)
{
    return LOAD_TEXTURE2D_X(_MotionVectorTexture, positionSS).xy;
}

bool PixelSetAsNoMotionVectors(in float4 inBuffer)
{
    return inBuffer.x > 1.0f;
}

void DecodeMotionVector(float4 inBuffer, out float2 motionVector)
{
    motionVector = PixelSetAsNoMotionVectors(inBuffer) ? 0.0f : inBuffer.xy;
}
#endif