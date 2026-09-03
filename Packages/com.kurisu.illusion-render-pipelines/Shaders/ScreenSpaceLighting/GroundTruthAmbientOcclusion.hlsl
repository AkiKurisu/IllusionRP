#ifndef GROUND_TRUTH_AMBIENT_OCCLUSION_INCLUDED
#define GROUND_TRUTH_AMBIENT_OCCLUSION_INCLUDED

// Includes
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/GTAOCommon.hlsl"

// Textures & Samplers
SAMPLER(sampler_BlitTexture);
#define _SourceSize                 _ScreenSize // Non-scaled

// Function defines
#define SAMPLE_BASEMAP(uv)          SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, UnityStereoTransformScreenSpaceTex(uv));
#define SAMPLE_BASEMAP_R(uv)        half(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, UnityStereoTransformScreenSpaceTex(uv)).r);

// Reference: HDRP.GTAO.compute
half4 GTAOFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    uint2 positionSS = GetScreenSpacePosition(input.texcoord);
    return half4(GTAO(positionSS));
}

// Geometry-aware separable bilateral filter
half4 Blur(const float2 uv, const float2 delta) : SV_Target
{
    half4 p0 =  SAMPLE_BASEMAP(uv                 );
    half4 p1a = SAMPLE_BASEMAP(uv - delta * 1.3846153846);
    half4 p1b = SAMPLE_BASEMAP(uv + delta * 1.3846153846);
    half4 p2a = SAMPLE_BASEMAP(uv - delta * 3.2307692308);
    half4 p2b = SAMPLE_BASEMAP(uv + delta * 3.2307692308);

    half3 n0 = GetPackedNormal(p0);

    half w0  =                                           half(0.2270270270);
    half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * half(0.3162162162);
    half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * half(0.3162162162);
    half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * half(0.0702702703);
    half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * half(0.0702702703);

    half s = HALF_ZERO;
    s += GetPackedAO(p0)  * w0;
    s += GetPackedAO(p1a) * w1a;
    s += GetPackedAO(p1b) * w1b;
    s += GetPackedAO(p2a) * w2a;
    s += GetPackedAO(p2b) * w2b;
    s *= rcp(w0 + w1a + w1b + w2a + w2b);

    return PackAONormal(s, n0);
}

// Geometry-aware bilateral filter (single pass/small kernel)
half BlurSmall(const float2 uv, const float2 delta)
{
    half4 p0 = SAMPLE_BASEMAP(uv                            );
    half4 p1 = SAMPLE_BASEMAP(uv + float2(-delta.x, -delta.y));
    half4 p2 = SAMPLE_BASEMAP(uv + float2( delta.x, -delta.y));
    half4 p3 = SAMPLE_BASEMAP(uv + float2(-delta.x,  delta.y));
    half4 p4 = SAMPLE_BASEMAP(uv + float2( delta.x,  delta.y));

    half3 n0 = GetPackedNormal(p0);

    half w0 = HALF_ONE;
    half w1 = CompareNormal(n0, GetPackedNormal(p1));
    half w2 = CompareNormal(n0, GetPackedNormal(p2));
    half w3 = CompareNormal(n0, GetPackedNormal(p3));
    half w4 = CompareNormal(n0, GetPackedNormal(p4));

    half s = HALF_ZERO;
    s += GetPackedAO(p0) * w0;
    s += GetPackedAO(p1) * w1;
    s += GetPackedAO(p2) * w2;
    s += GetPackedAO(p3) * w3;
    s += GetPackedAO(p4) * w4;

    return s *= rcp(w0 + w1 + w2 + w3 + w4);
}

half4 HorizontalBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;
    const float2 delta = float2(_SourceSize.z * rcp(DOWNSAMPLE), 0.0);
    return Blur(uv, delta);
}

half4 VerticalBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;
    const float2 delta = float2(0.0, _SourceSize.w * rcp(DOWNSAMPLE));
    return Blur(uv, delta);
}

half4 FinalBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    
    const float2 uv = input.texcoord;
    const float2 delta = _SourceSize.zw * rcp(DOWNSAMPLE);
    return OutputFinalAO(BlurSmall(uv, delta));
}


// ------------------------------------------------------------------
// Gaussian Blur
// ------------------------------------------------------------------

// https://software.intel.com/content/www/us/en/develop/blogs/an-investigation-of-fast-real-time-gpu-based-image-blur-algorithms.html
half GaussianBlur(half2 uv, half2 pixelOffset)
{
    half colOut = 0;

    // Kernel width 7 x 7
    const int stepCount = 2;

    const half gWeights[stepCount] ={
       0.44908,
       0.05092
    };
    const half gOffsets[stepCount] ={
       0.53805,
       2.06278
    };

    UNITY_UNROLL
    for( int i = 0; i < stepCount; i++ )
    {
        half2 texCoordOffset = gOffsets[i] * pixelOffset;
        half4 p1 = SAMPLE_BASEMAP(uv + texCoordOffset);
        half4 p2 = SAMPLE_BASEMAP(uv - texCoordOffset);
        half col = p1.r + p2.r;
        colOut += gWeights[i] * col;
    }

    return colOut;
}

half HorizontalGaussianBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half2 uv = input.texcoord;
    half2 delta = half2(_SourceSize.z * rcp(DOWNSAMPLE), HALF_ZERO);

    return GaussianBlur(uv, delta);
}

half VerticalGaussianBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half2 uv = input.texcoord;
    half2 delta = half2(HALF_ZERO, _SourceSize.w * rcp(DOWNSAMPLE));

    return OutputFinalAO(GaussianBlur(uv, delta));
}

#endif //UNIVERSAL_GTAO_INCLUDED
