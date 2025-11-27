#ifndef WATER_LIGHTING_INCLUDED
#define WATER_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/DeclareMotionVectorTexture.hlsl"

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

TEXTURE2D_X(_HistoryColorTexture);

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

// Sample reflection from base pass raymarching
void Raymarch_half(float3 origin, float3 direction, half steps, half stepSize, half thickness, out half2 sampleUV, out half valid, out half outOfBounds, out half debug)
{
    sampleUV = 0;
    valid = 0;
    outOfBounds = 0;
    debug = 0;

    float3 baseOrigin = origin;
    
    direction *= stepSize;
    const half rcpStepCount = rcp(steps);
    
    [loop]
    for(int i = 0; i < steps; i++)
    {
        debug++;
        //if(valid == 0)
        {
            origin += direction;
            direction *= 1.5;
            sampleUV = ViewSpacePosToUV(origin);

            outOfBounds = OutOfBoundsFade(sampleUV);
            
            //return;
            
            if(!(sampleUV.x > 1 || sampleUV.x < 0 || sampleUV.y > 1 || sampleUV.y < 0))
            {
                float deviceDepth = SampleSceneDepth(sampleUV);
                float3 samplePos = ViewPosFromDepth(sampleUV, deviceDepth);

                if(distance(samplePos.z, origin.z) > length(direction) * thickness) continue;

                
        
                if(samplePos.z > origin.z)
                {
                    valid = 1;
                    return;
                }
                
            } else
            {
                //outOfBounds = OutOfBoundsFade(sampleUV);
                return;
            }
        }
    }
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

/*
half3 SampleReflections(float3 normalWS, float3 positionWS, float3 viewDirectionWS, half2 screenUV)
{
    half3 reflection = 0;
    half2 refOffset = 0;
    
    float2 uv = float2(0, 0);
    half valid = 1;

    float3 positionVS = TransformWorldToView(positionWS);
    float3 normalVS = TransformWorldToViewDir(normalWS);

    float3 positionVSnorm = normalize(positionVS);
    float3 pivot = reflect(positionVS, normalVS);
    half debug;
    RayMarch(positionVS, pivot, uv, valid, debug);
    half3 ssr = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_ScreenTextures_linear_clamp, uv).rgb;

    half3 backup = CubemapReflection(viewDirectionWS, positionWS, normalWS);
    reflection = lerp(backup, ssr, valid);
    //do backup
    return reflection;
}
*/

half4 SampleSceneColor_half(half2 screenUV)
{
    float2 forwardMotionVector;
    DecodeMotionVector(SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, screenUV, 0), forwardMotionVector);
    float2 prevFrameUV = screenUV - forwardMotionVector; // backward
    float2 limit = float2(1, 1);
    if (any(prevFrameUV < float2(0.0,0.0)) || any(prevFrameUV > limit))
    {
        // Off-Screen.
        return 0;
    }
    half4 preFrameColor = half4(SAMPLE_TEXTURE2D_X_LOD(_HistoryColorTexture, sampler_PointClamp, prevFrameUV, 0));
    return half4(preFrameColor.rgb, 1.0);
}


#endif // WATER_LIGHTING_INCLUDED
