Shader "Hidden/ProbeSHDebug"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+0"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalHarmonics.hlsl"

        // One instance per probe of the committed solver state.
        StructuredBuffer<float4> _prtDebugProbeSH;
        StructuredBuffer<uint> _prtDebugProbeMetadata;
        float4 _prtDebugGridOrigin;   // xyz: first probe, w: spacing
        float4 _prtDebugGridCount;    // xyz: probe count, w: hidden probe or -1
        float _prtDebugProbeScale;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            nointerpolation uint probe : TEXCOORD1;
        };

        Varyings Vertex(Attributes input, uint instanceID : SV_InstanceID)
        {
            Varyings output;
            uint3 count = (uint3)_prtDebugGridCount.xyz;
            uint3 coordinate = uint3(instanceID / (count.y * count.z), instanceID / count.z % count.y, instanceID % count.z);
            float3 center = _prtDebugGridOrigin.xyz + float3(coordinate) * _prtDebugGridOrigin.w;
            output.positionCS = TransformWorldToHClip(center + input.positionOS.xyz * _prtDebugProbeScale);
            if ((int)instanceID == (int)_prtDebugGridCount.w)
                output.positionCS = float4(2, 2, 2, 1);
            output.normalWS = input.normalOS;
            output.probe = instanceID;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            float4 Fragment(Varyings input) : SV_Target
            {
                float3 c[9];
                for (uint i = 0; i < 9; i++)
                    c[i] = _prtDebugProbeSH[input.probe * 9u + i].xyz;
                float3 irradiance = IrradianceSH9(c, normalize(input.normalWS).xzy); // PI is pre-divided
                // Invalid probes render black.
                return float4((_prtDebugProbeMetadata[input.probe] >> 24) > 127u ? irradiance : 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
