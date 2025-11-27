Shader "Hidden/GroundTruthAmbientOcclusion"
{
    HLSLINCLUDE
    #pragma editor_sync_compilation
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    ENDHLSL

    SubShader
    {
        Tags{ "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        Cull Off ZWrite Off ZTest Always

        // ------------------------------------------------------------------
        // Depth only passes
        // ------------------------------------------------------------------

        // 0 - Occlusion estimation with DepthPyramid
        Pass
        {
            Name "SSAO_Occlusion"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GTAOFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_local _SOURCE_DEPTH _SOURCE_DEPTH_NORMALS
            #pragma multi_compile_local _RECONSTRUCT_NORMAL_LOW _RECONSTRUCT_NORMAL_MEDIUM _RECONSTRUCT_NORMAL_HIGH
            #pragma multi_compile_local _ _ORTHOGRAPHIC
            #pragma multi_compile_local FULL_RES HALF_RES
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "GroundTruthAmbientOcclusion.hlsl"
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Bilateral Blur
        // ------------------------------------------------------------------

        // 1 - Horizontal Blur
        Pass
        {
            Name "SSAO_Bilateral_HorizontalBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment HorizontalBlur
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "GroundTruthAmbientOcclusion.hlsl"
            ENDHLSL
        }

        // 2 - Vertical Blur
        Pass
        {
            Name "SSAO_Bilateral_VerticalBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VerticalBlur
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "GroundTruthAmbientOcclusion.hlsl"
            ENDHLSL
        }

        // 3 - Final Blur
        Pass
        {
            Name "SSAO_Bilateral_FinalBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FinalBlur
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "GroundTruthAmbientOcclusion.hlsl"
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Gaussian Blur
        // ------------------------------------------------------------------

        // 4 - Horizontal
        Pass
        {
            Name "SSAO_Gaussian_HorizontalBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment HorizontalGaussianBlur
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "GroundTruthAmbientOcclusion.hlsl"
            ENDHLSL
        }

        // 5 - Vertical
        Pass
        {
            Name "SSAO_Gaussian_VerticalBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VerticalGaussianBlur
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "GroundTruthAmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}
