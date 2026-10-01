Shader "Hidden/IllusionRP/WetSurfaceBlurNormals"
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

        TEXTURE2D_X_FLOAT(_WetBlurInput);
        TEXTURE2D_X_FLOAT(_WetBlurDepth);
        TEXTURE2D_X_FLOAT(_WetBlurMask);
        TEXTURE2D_X_FLOAT(_WetBlurCoverage);
        float4 _WetBlurTexelSize;

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

        float3 SampleNormal(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_WetBlurInput, sampler_LinearClamp, uv).xyz;
        }

        float SampleDepth(float2 uv)
        {
            float raw = SAMPLE_TEXTURE2D_X(_WetBlurDepth, sampler_PointClamp, uv).r;
            return Linear01Depth(raw, _ZBufferParams);
        }

        float3 Neighbor(float2 uv, float centerDepth)
        {
            float depthWeight = 1 - saturate(abs(centerDepth - SampleDepth(uv)) * 100000);
            return SampleNormal(uv) * depthWeight;
        }

        float3 BlurNormal(float2 uv, float2 direction)
        {
            float coverage = SAMPLE_TEXTURE2D_X(_WetBlurCoverage, sampler_PointClamp, uv).r;
            clip(coverage - 0.5);

            float3 center = SampleNormal(uv);
            float depth = SampleDepth(uv);

            float3 total = center;
            total += Neighbor(uv + direction * 1.384615, depth);
            total += Neighbor(uv - direction * 1.384615, depth);
            total += Neighbor(uv + direction * 3.230769, depth);
            total += Neighbor(uv - direction * 3.230769, depth);
            return normalize(total);
        }

        float4 FragHorizontal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return float4(BlurNormal(input.uv, float2(_WetBlurTexelSize.x, 0)), 1);
        }

        float4 FragVertical(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float wetness = SAMPLE_TEXTURE2D_X(_WetBlurMask, sampler_PointClamp, input.uv).r;
            float alpha = saturate((wetness - 0.7) / 0.3);
            return float4(BlurNormal(input.uv, float2(0, _WetBlurTexelSize.y)), alpha);
        }
        ENDHLSL

        Pass
        {
            Name "Horizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHorizontal
            ENDHLSL
        }
        Pass
        {
            Name "Vertical"
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVertical
            ENDHLSL
        }
    }
}
