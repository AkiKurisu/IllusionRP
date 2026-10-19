Shader "Universal Render Pipeline/Hybrid SpeedTree7 Billboard"
{
    Properties
    {
        _Color("Main Color", Color) = (1,1,1,1)
        _HueVariation("Hue Variation", Color) = (1.0,0.5,0.0,0.1)
        _MainTex("Base (RGB) Trans (A)", 2D) = "white" {}
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.333
        [MaterialEnum(None,0,Fastest,1)] _WindQuality("Wind Quality", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
            "RenderType" = "TransparentCutout"
            "DisableBatching" = "LODFading"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }
        LOD 400

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_GLOBAL_ILLUMINATION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _SHADOW_BIAS_FRAGMENT
            #pragma multi_compile_fragment _ AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION

            #pragma vertex SpeedTree7Vert
            #pragma fragment SpeedTree7Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile __ BILLBOARD_FACE_CAMERA_POS
            #pragma multi_compile __ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ DEBUG_DISPLAY

            #pragma shader_feature_local EFFECT_BUMP
            #pragma shader_feature_local EFFECT_HUE_VARIATION

            #define ENABLE_WIND

            #if USE_DYNAMIC_BRANCH_FOG_KEYWORD && SHADER_API_VULKAN && SHADER_API_MOBILE
            #define SKIP_SHADOWS_LIGHT_INDEX_CHECK 1
            #endif

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ColorMask 0

            HLSLPROGRAM

            #pragma vertex SpeedTree7VertDepth
            #pragma fragment SpeedTree7FragDepth

            #pragma multi_compile __ BILLBOARD_FACE_CAMERA_POS
            #pragma multi_compile __ LOD_FADE_CROSSFADE

            #define ENABLE_WIND
            #define DEPTH_ONLY
            #define SHADOW_CASTER

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags{"LightMode" = "UniversalGBuffer"}

            HLSLPROGRAM
            #pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

            #pragma vertex SpeedTree7Vert
            #pragma fragment SpeedTree7Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile __ BILLBOARD_FACE_CAMERA_POS
            #pragma multi_compile __ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

            #pragma shader_feature_local EFFECT_BUMP
            #pragma shader_feature_local EFFECT_HUE_VARIATION

            #define ENABLE_WIND
            #define GBUFFER

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}

            ColorMask R

            HLSLPROGRAM

            #pragma vertex SpeedTree7VertDepth
            #pragma fragment SpeedTree7FragDepth

            #pragma multi_compile __ BILLBOARD_FACE_CAMERA_POS
            #pragma multi_compile __ LOD_FADE_CROSSFADE

            #define ENABLE_WIND
            #define DEPTH_ONLY

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode" = "DepthNormals"}

            ColorMask R

            HLSLPROGRAM

            #pragma vertex SpeedTree7VertDepthNormalBillboard
            #pragma fragment SpeedTree7FragDepthNormalBillboard

            #pragma shader_feature_local EFFECT_BUMP
            #pragma multi_compile __ BILLBOARD_FACE_CAMERA_POS
            #pragma multi_compile __ LOD_FADE_CROSSFADE

            #define ENABLE_WIND

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "ForwardGBuffer"
            Tags { "LightMode" = "ForwardGBuffer" }
            ZWrite On
            ZTest LEqual
            Stencil
            {
                WriteMask 5
                Ref 4
                Comp Always
                Pass Replace
            }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SpeedTree7VertDepthNormalBillboard
            #pragma fragment SpeedTree7ForwardGBufferFrag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer assumeuniformscaling maxcount:50
            #pragma multi_compile _ BILLBOARD_FACE_CAMERA_POS
            #pragma shader_feature_local EFFECT_BUMP
            #define ENABLE_WIND
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"
            #include "SpeedTree7ForwardGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SpeedTree7MotionVectorVert
            #pragma fragment SpeedTree7MotionVectorFrag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling maxcount:50
            #pragma multi_compile _ BILLBOARD_FACE_CAMERA_POS
            #define ENABLE_WIND
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Nature/SpeedTree7BillboardInput.hlsl"
            #include "SpeedTree7BillboardPasses.hlsl"
            #include "SpeedTree7MotionVectorsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
