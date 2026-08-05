#ifndef WATER_LIGHTING_INCLUDED
#define WATER_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D_X(_PreRefractionColorTexture);

#if defined(_WATER_REFLECTION_LEGACY)
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/DeclareMotionVectorTexture.hlsl"
    TEXTURE2D_X(_HistoryColorTexture);
#endif

#ifdef _SHADOW_SAMPLES_LOW
    #define SHADOW_ITERATIONS 1
    #define SHADOW_VOLUME
#elif _SHADOW_SAMPLES_MEDIUM
    #define SHADOW_ITERATIONS 2
    #define SHADOW_VOLUME
#elif _SHADOW_SAMPLES_HIGH
    #define SHADOW_ITERATIONS 4
    #define SHADOW_VOLUME
#else
    #define SHADOW_ITERATIONS 0
#endif


#ifdef _SSR_SAMPLES_LOW
    #define SSR_ITERATIONS 8
#elif _SSR_SAMPLES_MEDIUM
    #define SSR_ITERATIONS 16
#elif _SSR_SAMPLES_HIGH
    #define SSR_ITERATIONS 32
#else
    #define SSR_ITERATIONS 4
#endif

float2 WaterScreenTextureUV(float2 normalizedScreenSpaceUV)
{
    return UnityStereoTransformScreenSpaceTex(normalizedScreenSpaceUV) * _RTHandleScale.xy;
}

half3 SamplePreRefractionColor(float2 normalizedScreenSpaceUV)
{
    bool inBounds = all(normalizedScreenSpaceUV >= 0.0) && all(normalizedScreenSpaceUV <= 1.0);
    if (!inBounds)
        return 0.0;

    half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
        _PreRefractionColorTexture, sampler_LinearClamp,
        WaterScreenTextureUV(normalizedScreenSpaceUV), 0);
    return all(isfinite(sceneColor)) ? sceneColor.rgb : 0.0;
}

half3 SamplePreRefractionColorDistorted(
    float2 currentUV,
    float2 distortedUV,
    float surfaceEyeDepth)
{
    bool distortedInBounds = all(distortedUV >= 0.0) && all(distortedUV <= 1.0);
    if (!distortedInBounds)
        return SamplePreRefractionColor(currentUV);

    float refractedEyeDepth = LinearEyeDepth(SampleSceneDepth(distortedUV), _ZBufferParams);
    float2 safeUV = refractedEyeDepth <= surfaceEyeDepth ? currentUV : distortedUV;
    return SamplePreRefractionColor(safeUV);
}

half4 SampleTransparentScreenSpaceReflection(float2 normalizedScreenSpaceUV)
{
    bool inBounds = all(normalizedScreenSpaceUV >= 0.0) && all(normalizedScreenSpaceUV <= 1.0);
    if (!inBounds)
        return 0.0;

    half4 reflection = SAMPLE_TEXTURE2D_X_LOD(
        _SsrLightingTexture, sampler_LinearClamp,
        WaterScreenTextureUV(normalizedScreenSpaceUV), 0);
    if (!all(isfinite(reflection)))
        return 0.0;

    reflection.a = saturate(reflection.a);
    reflection.rgb *= GetInverseCurrentExposureMultiplier();

    // The pipeline stores SSR lighting premultiplied by confidence, while the
    // existing Water graph expects a straight color and applies confidence in its lerp.
    reflection.rgb = reflection.a > 1e-4h ? reflection.rgb / reflection.a : 0.0h;
    return reflection;
}

///////////////////////////////////////////////////////////////////////////////
//                           Reflection Modes                                //
///////////////////////////////////////////////////////////////////////////////

void Reflection_half(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion, float2 normalizedScreenSpaceUV, out half3 output)
{
    output = GlossyEnvironmentReflection(reflectVector, positionWS, perceptualRoughness, occlusion, normalizedScreenSpaceUV);
}

float3 ViewPosFromDepth(float2 positionNDC, float deviceDepth)
{
    float4 positionCS  = ComputeClipSpacePosition(positionNDC, deviceDepth);
    float4 hpositionVS = mul(UNITY_MATRIX_I_P, positionCS);
    return hpositionVS.xyz / hpositionVS.w;
}

float2 ViewSpacePosToUV(float3 pos)
{
    return ComputeNormalizedDeviceCoordinates(pos, UNITY_MATRIX_P);
}

half OutOfBoundsFade(half2 uv)
{
    half2 fade = 0;
    fade.x = saturate(1 - abs(uv.x - 0.5) * 2);
    fade.y = saturate(1 - abs(uv.y - 0.5) * 2);
    return fade.x * fade.y;
}

// Compatibility entry point used by the existing ASE graph. Transparent SSR
// tracing is owned by IllusionRP; this function only resolves the current
// water pixel and forwards the pipeline confidence to the graph.
void Raymarch_half(float3 origin, float3 direction, half steps, half stepSize, half thickness, out half2 sampleUV, out half valid, out half outOfBounds, out half debug)
{
#if defined(_WATER_REFLECTION_LEGACY)
    sampleUV = 0;
    valid = 0;
    outOfBounds = 0;
    debug = 0;

    direction *= stepSize;

    [loop]
    for (int i = 0; i < steps; i++)
    {
        debug++;
        origin += direction;
        direction *= 1.5;
        sampleUV = ViewSpacePosToUV(origin);
        outOfBounds = OutOfBoundsFade(sampleUV);

        if (sampleUV.x > 1 || sampleUV.x < 0 || sampleUV.y > 1 || sampleUV.y < 0)
            return;

        float deviceDepth = SampleSceneDepth(sampleUV);
        float3 samplePos = ViewPosFromDepth(sampleUV, deviceDepth);

        if (distance(samplePos.z, origin.z) > length(direction) * thickness)
            continue;

        if (samplePos.z > origin.z)
        {
            valid = 1;
            return;
        }
    }
#else
    sampleUV = ViewSpacePosToUV(origin);
    half4 reflection = SampleTransparentScreenSpaceReflection(sampleUV);
    valid = reflection.a;
    outOfBounds = 1.0;
    debug = 0;
#endif
}

struct WaveParams
{
    half2 origin;
    half amplitude;
    half length;
    half speed;
};



void RadialGerstnerWaves_half(float3 worldPos, half time, out half displacement)
{
    const int waveCount = 1;


    //Params should probably be moved to global scope
    
    WaveParams w1 = {
        half2(0, -164),
        1,
        1,
        2
    };

    WaveParams w2 = {
        half2(20, -130),
        0.7,
        1.7,
        4
    };

    WaveParams w3 = {
        half2(-21, -156),
        0.3,
        3,
        3
    };

    WaveParams w4 = {
        half2(-4, -200),
        1.4,
        0.6,
        1
    };

    WaveParams waveParams[waveCount] = {
        w1,
        //w2,
        //w3,
        //w4
    };
    
    displacement = 0;

    half summedAmplitude = 0;

    for(int i = 0; i < waveCount; i++)
    {
        WaveParams params = waveParams[i];


        half2 D = normalize(worldPos.xz - params.origin);
        //D = half2(1, 0);
        half w = 2/params.length;
        half phaseConstant = w * params.speed;

        displacement += sin( dot(D, worldPos.xz));
        
        summedAmplitude += params.amplitude;
    }

    //displacement /= summedAmplitude;
}

half4 SampleSceneColor_half(half2 screenUV)
{
#if defined(_WATER_REFLECTION_LEGACY)
    float2 forwardMotionVector;
    DecodeMotionVector(
        SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, screenUV, 0),
        forwardMotionVector);

    float2 prevFrameUV = screenUV - forwardMotionVector;
    if (any(prevFrameUV < 0.0) || any(prevFrameUV > 1.0))
        return 0.0;

    half3 previousColor = SAMPLE_TEXTURE2D_X_LOD(
        _HistoryColorTexture, sampler_PointClamp, prevFrameUV, 0).rgb;
    return half4(previousColor, 1.0);
#else
    return SampleTransparentScreenSpaceReflection(screenUV);
#endif
}


#endif // WATER_LIGHTING_INCLUDED
