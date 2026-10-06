Shader "Hidden/DebugPRTCascades"
{
    HLSLINCLUDE
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/EvaluateProbeVolume.hlsl"

    #pragma vertex Vert
    #pragma fragment Frag

    TEXTURE2D_X(_SourceTexture);
    TEXTURE2D_X_FLOAT(_CameraDepthTexture);

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetNormalizedFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            // Matches the cascade window gizmos.
            static const float3 CascadeColors[4] =
            {
                float3(1.0, 0.25, 0.2), float3(1.0, 0.8, 0.2), float3(0.3, 0.9, 0.4), float3(0.3, 0.7, 1.0)
            };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 scene = SAMPLE_TEXTURE2D_X_LOD(_SourceTexture, sampler_PointClamp, uv, 0).rgb;
                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r;
#if UNITY_REVERSED_Z
                if (depth <= 0)
#else
                if (depth >= 1)
#endif
                    return half4(scene * 0.5, 1);

                // Pixels lit by PRT take the colors of the cascades they blend; fallback pixels stay grey.
                float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 diffuse, secondaryDiffuse;
                float4 contributions;
                float3 tint = 0.5;
                if (TrySampleProbeVolumeCascades(positionWS, float3(0, 1, 0), float3(0, 1, 0), diffuse, secondaryDiffuse, contributions))
                    tint = contributions.x * CascadeColors[0] + contributions.y * CascadeColors[1] +
                        contributions.z * CascadeColors[2] + contributions.w * CascadeColors[3];
                return half4(tint * (0.25 + 0.75 * saturate(Luminance(scene))), 1);
            }
            ENDHLSL
        }
    }
}
