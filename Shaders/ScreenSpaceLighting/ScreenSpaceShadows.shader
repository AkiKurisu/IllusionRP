/*
 * StarRailNPRShader - Fan-made shaders for Unity URP attempting to replicate
 * the shading of Honkai: Star Rail.
 * https://github.com/stalomeow/StarRailNPRShader
 *
 * Copyright (C) 2023 Stalo <stalowork@163.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

Shader "Hidden/ScreenSpaceShadows"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ScreenSpaceShadows"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _CONTACT_SHADOWS
            #pragma multi_compile_fragment _ _PCSS_SHADOWS
            #pragma multi_compile_fragment _ _DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT _DEBUG_SCREEN_SPACE_SHADOW_CONTACT
            #pragma multi_compile_fragment _ _SHADOW_BIAS_FRAGMENT

            #pragma vertex   Vert
            #pragma fragment Fragment

            // Keep compiler quiet about Shadows.hlsl.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ShaderVariablesContactShadows.hlsl"
            
            // Core.hlsl for XR dependencies
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/HDShadows.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/HDPerObjectShadow.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D(_ContactShadowMap);
            
            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float deviceDepth = LoadSceneDepth(input.positionCS.xy);
#if !UNITY_REVERSED_Z
                deviceDepth = deviceDepth * 2.0 - 1.0;
#endif
                
                // Fetch shadow coordinates for cascade.
                float3 positionWS = ComputeWorldSpacePosition(input.texcoord.xy, deviceDepth, unity_MatrixInvVP);

                float3 biasPositionWS = positionWS;
                float3 lightDir = GetMainLight().direction;
                float3 normalWS = 0;
#if APPLY_SHADOW_BIAS_FRAGMENT
                normalWS = LoadSceneNormals(input.positionCS.xy);
                biasPositionWS = IllusionApplyShadowBias(positionWS, normalWS, lightDir);
#endif

                // Screenspace shadowmap is only used for directional lights which use orthogonal projection.
                half realtimeShadow = IllusionMainLightRealtimeShadow(TransformWorldToShadowCoordCascade(biasPositionWS), input.texcoord.xy);
                float perObjShadow = MainLightPerObjectSceneShadow(positionWS, normalWS, lightDir, input.texcoord.xy);

                // TODO: Let contact shadow map always exist and be white default
#ifdef _CONTACT_SHADOWS
				float screenSpaceShadow = min(realtimeShadow, perObjShadow);
				float contactShadow = 1 - LOAD_TEXTURE2D_X(_ContactShadowMap, input.positionCS.xy).r;
            	float finalShadow = min(contactShadow, screenSpaceShadow);
#else
                float finalShadow = min(realtimeShadow, perObjShadow);
#endif

                // Debug mode
#if defined(_DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT)
                // Only display main light shadow (realtime shadow + per object shadow)
                return min(realtimeShadow, perObjShadow);
#elif defined(_DEBUG_SCREEN_SPACE_SHADOW_CONTACT)
                // Only display contact shadow
#ifdef _CONTACT_SHADOWS
                return 1 - LOAD_TEXTURE2D_X(_ContactShadowMap, input.positionCS.xy).r;
#else
                return 1.0; // No contact shadow, return white
#endif
#else
                // Normal mode
                return finalShadow;
#endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
