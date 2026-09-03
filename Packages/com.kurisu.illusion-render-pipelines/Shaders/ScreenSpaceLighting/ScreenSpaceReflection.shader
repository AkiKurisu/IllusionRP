// Modified from https://github.com/EricHu33/URP_SSR
Shader "Hidden/ScreenSpaceReflection"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "RenderPipeline" = "UniversalPipeline"
        }
        
        LOD 100
        
        ZWrite Off
        Cull Off
        ZTest Always
        
        Pass
        {
            Name "LinearVS"

            HLSLPROGRAM
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _DEFERRED_RENDERING_PATH
            #define SSR_TRACE 1
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ScreenSpaceReflection.hlsl"
            
            #pragma vertex Vert
            #pragma fragment FragSSRLinearVS
            ENDHLSL
        }

        Pass
        {
            Name "LinearSS"

            HLSLPROGRAM
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _DEFERRED_RENDERING_PATH
            #define SSR_TRACE 1
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ScreenSpaceReflection.hlsl"
            
            #pragma vertex Vert
            #pragma fragment FragSSRLinearSS
            ENDHLSL
        }

        Pass
        {
            Name "HizSS"

            HLSLPROGRAM
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _DEFERRED_RENDERING_PATH
            #define SSR_TRACE   1
            #define SSR_APPROX  1            // PBR mode not support in Fragment Shader.
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ScreenSpaceReflection.hlsl"
            
            #pragma vertex Vert
            #pragma fragment FragSSRHizSS
            ENDHLSL
        }

        Pass
        {
            Name "Reprojection"

            HLSLPROGRAM
            #define SSR_APPROX    1            // PBR mode not support in Fragment Shader.
            #define SSR_REPROJECT 1
            
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _DEFERRED_RENDERING_PATH
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ScreenSpaceReflection.hlsl"
            
            
            #pragma vertex Vert
            #pragma fragment FragSSRReprojection
            ENDHLSL
        }
    }
}