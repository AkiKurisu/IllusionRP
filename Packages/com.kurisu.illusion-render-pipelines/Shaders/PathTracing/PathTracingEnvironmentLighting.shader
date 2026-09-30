/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

Shader "Hidden/Illusion/PathTracingEnvironmentLighting"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float3 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 direction : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.direction = input.positionOS;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "SphericalHarmonics"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 _PathTracingRadianceSH[9];

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float3 radiance = _PathTracingRadianceSH[0].rgb * 0.282095
                    + _PathTracingRadianceSH[1].rgb * (0.488603 * d.y)
                    + _PathTracingRadianceSH[2].rgb * (0.488603 * d.z)
                    + _PathTracingRadianceSH[3].rgb * (0.488603 * d.x)
                    + _PathTracingRadianceSH[4].rgb * (1.092548 * d.x * d.y)
                    + _PathTracingRadianceSH[5].rgb * (1.092548 * d.y * d.z)
                    + _PathTracingRadianceSH[6].rgb * (0.315392 * (3.0 * d.z * d.z - 1.0))
                    + _PathTracingRadianceSH[7].rgb * (1.092548 * d.x * d.z)
                    + _PathTracingRadianceSH[8].rgb * (0.546274 * (d.x * d.x - d.y * d.y));
                return half4(max(radiance, 0.0), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "EnvironmentAndDirectionalLights"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #define K_PI PI
            struct EMB_DirectionalLight
            {
                float4 ColorIntensity;
                float3 Direction;
                float AngularSize;
            };
            StructuredBuffer<EMB_DirectionalLight> _PathTracingDirectionalLights;
            uint _PathTracingDirectionalLightCount;
            uint _PathTracingCubeDim;
            uint _PathTracingCubeFace;
            TEXTURECUBE(_PathTracingSourceCube);
            SAMPLER(sampler_PathTracingSourceCube);
            float _PathTracingSourceScale;

float3 CubemapGetDirectionFor(uint face, float2 uv)
{
    float cx = (uv.x * 2.0) - 1;
    float cy = 1 - (uv.y * 2.0);

    float3 dir;
    const float l = sqrt(cx * cx + cy * cy + 1);
    switch (face)
    {
        case 0:  dir = float3(   1, cy, -cx ); break;
        case 1:  dir = float3(  -1, cy,  cx ); break;
        case 2:  dir = float3(  cx,  1, -cy ); break;
        case 3:  dir = float3(  cx, -1,  cy ); break;
        case 4:  dir = float3(  cx, cy,   1 ); break;
        case 5:  dir = float3( -cx, cy,  -1 ); break;
        default: dir = 0.0.xxx; break;
    }
    return dir * (1 / l);
}

float3 ComputeLightContribution( uint2 pixel, uint face, const EMB_DirectionalLight light )
{
#if 0 // this provides a binary "either in or out of cone" coverage that is a.) incorrect and b.) aliased
    float3 direction = CubemapGetDirectionFor( face, (float2(pixel) + 0.5.xx ) / float(_PathTracingCubeDim).xx );
    float angle = acos( clamp( dot(-light.Direction, direction), -1.0, 1.0 ) );
    float pixelCoverage = angle < (light.AngularSize*0.5);
#else
    const float fadeRangeInTexels = 1.1;
    float3 direction0 = CubemapGetDirectionFor( face, (float2(pixel) + 0.5.xx + 0.5 * float2(-fadeRangeInTexels, -fadeRangeInTexels)) / float(_PathTracingCubeDim).xx );
    float3 direction1 = CubemapGetDirectionFor( face, (float2(pixel) + 0.5.xx + 0.5 * float2(+fadeRangeInTexels, -fadeRangeInTexels)) / float(_PathTracingCubeDim).xx );
    float3 direction2 = CubemapGetDirectionFor( face, (float2(pixel) + 0.5.xx + 0.5 * float2(-fadeRangeInTexels, +fadeRangeInTexels)) / float(_PathTracingCubeDim).xx );
    float3 direction3 = CubemapGetDirectionFor( face, (float2(pixel) + 0.5.xx + 0.5 * float2(+fadeRangeInTexels, +fadeRangeInTexels)) / float(_PathTracingCubeDim).xx );
    float dotMin = min( min( dot(-light.Direction, direction0), dot(-light.Direction, direction1) ), min( dot(-light.Direction, direction2), dot(-light.Direction, direction3) ) );
    float dotMax = max( max( dot(-light.Direction, direction0), dot(-light.Direction, direction1) ), max( dot(-light.Direction, direction2), dot(-light.Direction, direction3) ) );

    float angleMin = acos( clamp( dotMax, -1.0, 1.0 ) );
    float angleMax = acos( clamp( dotMin, -1.0, 1.0 ) );

    float pixelCoverage = saturate( ((light.AngularSize*0.5)-angleMin) / (angleMax-angleMin+1e-24) );
    pixelCoverage = pow(pixelCoverage, 4);
#endif

    float lightSolidAngle = 2 * K_PI * ( 1 - cos(light.AngularSize*0.5) );

    return pixelCoverage * light.ColorIntensity.rgb * (light.ColorIntensity.a / lightSolidAngle);
}

            float4 Frag(Varyings input) : SV_Target
            {
                uint2 pixel = uint2(input.positionCS.xy);
                float3 direction = CubemapGetDirectionFor(_PathTracingCubeFace, (float2(pixel) + 0.5) / float(_PathTracingCubeDim));
                float3 radiance = SAMPLE_TEXTURECUBE_LOD(_PathTracingSourceCube, sampler_PathTracingSourceCube, direction, 0).rgb * _PathTracingSourceScale;
                for (uint i = 0; i < _PathTracingDirectionalLightCount; i++)
                    radiance += ComputeLightContribution(pixel, _PathTracingCubeFace, _PathTracingDirectionalLights[i]);
                return float4(clamp(radiance, 0.0, 65504.0), 1);
            }
            ENDHLSL
        }
    }
}
