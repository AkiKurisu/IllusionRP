Shader "Hidden/IllusionRP/WetSurfaceMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D_X_FLOAT(_WetSourceDepth);
        TEXTURE2D_X_FLOAT(_WetSourceNormal);
        TEXTURE2D(_WetXLayer);
        SAMPLER(sampler_WetXLayer);
        TEXTURE2D(_WetYLayer);
        SAMPLER(sampler_WetYLayer);
        TEXTURE2D(_WetZLayer);
        SAMPLER(sampler_WetZLayer);
        TEXTURE2D(_WetBlueNoise);
        SAMPLER(sampler_WetBlueNoise);

        float4x4 _WetWorldToLocal;
        float4x4 _WetLocalToWorld;
        float4 _WetXScaleOffset, _WetYScaleOffset, _WetZScaleOffset;
        float4 _WetXInputStart, _WetYInputStart, _WetZInputStart;
        float4 _WetXInputExtent, _WetYInputExtent, _WetZInputExtent;
        float4 _WetXOutputStart, _WetYOutputStart, _WetZOutputStart;
        float4 _WetXOutputEnd, _WetYOutputEnd, _WetZOutputEnd;
        float4 _WetSampleJitter;
        float _WetWorldProjectionScale;
        int _WetLayerMode;
        int _WetProjectionMode;
        float _WetSaturation;
        float _WetEdgeFadeoff;
        float _WetFaceSharpness;

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            output.uv = GetFullScreenTriangleTexCoord(vertexID);
            return output;
        }

        struct FragmentOutput
        {
            float4 mask : SV_Target0;
            float4 coverage : SV_Target1;
        };

        float2 JitterUV(float2 uv)
        {
            float2 noise = SAMPLE_TEXTURE2D(_WetBlueNoise, sampler_WetBlueNoise, uv * float2(29, 31)).rg;
            return uv + (noise - 0.5) * _WetSampleJitter.xy;
        }

        float EvaluateChannels(float4 sampleValue, float4 start, float4 extent, float4 outputStart, float4 outputEnd)
        {
            // A zero extent disables the channel; never divide by it, saturate(NaN) is API-dependent.
            float4 enabled = step(0.000001, extent);
            float4 value = saturate((sampleValue - start) / max(extent, 0.0001));
            value = lerp(outputStart, outputEnd, value) * enabled;
            return max(max(value.r, value.g), max(value.b, value.a));
        }

        float EvaluateY(float3 coordinates)
        {
            float2 uv = coordinates.xz * _WetYScaleOffset.xy + _WetYScaleOffset.zw;
            return EvaluateChannels(SAMPLE_TEXTURE2D(_WetYLayer, sampler_WetYLayer, JitterUV(uv)),
                _WetYInputStart, _WetYInputExtent, _WetYOutputStart, _WetYOutputEnd);
        }

        float EvaluateLayers(float3 coordinates, float3 normalWS)
        {
            float result = 0;
            if (_WetLayerMode == 2)
            {
                float3 localNormal = mul(transpose((float3x3)_WetLocalToWorld), normalWS);
                // Divide by the matrix diagonal, not the axis scale: rotation intentionally changes the weights.
                localNormal /= float3(_WetLocalToWorld._m00, _WetLocalToWorld._m11, _WetLocalToWorld._m22);
                float3 weights = pow(abs(normalize(localNormal)), _WetFaceSharpness);
                weights /= dot(weights, float3(1, 1, 1));
                float2 uvX = coordinates.zy * _WetXScaleOffset.xy + _WetXScaleOffset.zw;
                float2 uvZ = coordinates.xy * _WetZScaleOffset.xy + _WetZScaleOffset.zw;
                float x = EvaluateChannels(SAMPLE_TEXTURE2D(_WetXLayer, sampler_WetXLayer, JitterUV(uvX)),
                    _WetXInputStart, _WetXInputExtent, _WetXOutputStart, _WetXOutputEnd);
                float z = EvaluateChannels(SAMPLE_TEXTURE2D(_WetZLayer, sampler_WetZLayer, JitterUV(uvZ)),
                    _WetZInputStart, _WetZInputExtent, _WetZOutputStart, _WetZOutputEnd);
                result = dot(float3(x, EvaluateY(coordinates), z), weights);
            }
            else
            {
                float mask = 1;
                if (_WetLayerMode == 1)
                    mask = EvaluateY(coordinates);
                float3 up = float3(_WetLocalToWorld._m01, _WetLocalToWorld._m11, _WetLocalToWorld._m21);
                result = mask * pow(saturate(dot(normalize(up), normalWS)), _WetFaceSharpness);
            }
            return result;
        }

        FragmentOutput EvaluateMask(Varyings input, bool sphere)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            uint2 pixel = uint2(input.positionCS.xy);
            float depth = LOAD_TEXTURE2D_X(_WetSourceDepth, pixel).r;
            float3 worldPosition = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
            float3 local = mul(_WetWorldToLocal, float4(worldPosition, 1)).xyz;
            clip(0.5 - max(max(abs(local.x), abs(local.y)), abs(local.z)));

            float radius = sphere ? length(local) : max(max(abs(local.x), abs(local.y)), abs(local.z));

            float edge = _WetEdgeFadeoff == 0 ? (radius < 0.5 ? 1 : 0) : saturate((1 - 2 * radius) / _WetEdgeFadeoff);
            edge = edge * edge * (3 - 2 * edge);
            float3 coordinates = local;
            if (_WetProjectionMode == 1)
                coordinates = worldPosition / float3(_WetWorldToLocal._m00, _WetWorldToLocal._m11, _WetWorldToLocal._m22)
                    * _WetWorldProjectionScale;
            float3 normalWS = normalize(LOAD_TEXTURE2D_X(_WetSourceNormal, pixel).xyz);
            float wetness = edge * EvaluateLayers(coordinates + 0.5, normalWS) * _WetSaturation;
            FragmentOutput output;
            output.mask = float4(wetness, wetness, wetness, wetness);
            output.coverage = 1;
            return output;
        }

        FragmentOutput FragCube(Varyings input)
        {
            return EvaluateMask(input, false);
        }

        FragmentOutput FragSphere(Varyings input)
        {
            return EvaluateMask(input, true);
        }
        ENDHLSL

        Pass
        {
            Name "WetCube"
            Blend 0 OneMinusDstColor One, OneMinusDstColor One
            Blend 1 One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCube
            ENDHLSL
        }
        Pass
        {
            Name "WetSphere"
            Blend 0 OneMinusDstColor One, OneMinusDstColor One
            Blend 1 One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSphere
            ENDHLSL
        }
        Pass
        {
            Name "DryCube"
            Blend 0 Zero OneMinusSrcColor, Zero OneMinusSrcColor
            Blend 1 One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCube
            ENDHLSL
        }
        Pass
        {
            Name "DrySphere"
            Blend 0 Zero OneMinusSrcColor, Zero OneMinusSrcColor
            Blend 1 One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSphere
            ENDHLSL
        }
    }
}
