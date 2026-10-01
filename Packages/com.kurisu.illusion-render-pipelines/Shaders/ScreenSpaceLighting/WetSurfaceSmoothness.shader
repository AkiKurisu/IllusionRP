Shader "Hidden/IllusionRP/WetSurfaceSmoothness"
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
        #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/WetSurfaceResponse.hlsl"

        TEXTURE2D_X_FLOAT(_WetSurfaceMask);
        TEXTURE2D_X_FLOAT(_WetSurfaceForwardSource);

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

        float4 Modify(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            uint2 pixel = uint2(input.positionCS.xy);
            float4 packed = LOAD_TEXTURE2D_X(_WetSurfaceForwardSource, pixel);
            float smoothness = packed.r;
            float wetness = LOAD_TEXTURE2D_X(_WetSurfaceMask, pixel).r;
            if (wetness >= 0.001 && IsWetSurfaceForwardDataValid(packed))
                smoothness = WetSurfaceSmoothnessFromPacked(packed, wetness);
            return smoothness.xxxx;
        }

        ENDHLSL

        Pass
        {
            Name "Modify"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Modify
            ENDHLSL
        }
    }
}
