Shader "Hidden/PenumbraMask"
{
    HLSLINCLUDE
    
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/Shadows.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/ShaderVariablesPCSS.hlsl"
    #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Shadow/PerObjectShadow.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    static const float2 offset[4] = {float2(-1, 1), float2(1, 1), float2(-1, -1), float2(1, -1)};
    
    float PCSSPenumbraMaskFrag(Varyings input) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        float shadowAttenuation = 0;

        for(int i = 0; i < 4; ++i)
        {
            float2 coord = input.texcoord.xy + offset[i].xy * _ColorAttachmentTexelSize.xy;
            
            float sampleDepth = SampleSceneDepth(coord.xy);
#if !UNITY_REVERSED_Z
            sampleDepth = sampleDepth * 2.0 - 1.0;
#endif
            
            float3 samplePositionWS = ComputeWorldSpacePosition(coord.xy, sampleDepth, unity_MatrixInvVP);
            float3 biasPositionWS = samplePositionWS;
            float3 lightDir = GetMainLight().direction;
            float3 normalWS = 0;
#if APPLY_SHADOW_BIAS_FRAGMENT
            normalWS = LoadSceneNormals(input.positionCS.xy);
            biasPositionWS = IllusionApplyShadowBias(samplePositionWS, normalWS, lightDir);
#endif
            float4 shadowCoord = TransformWorldToShadowCoord(biasPositionWS);

            
            float realtimeShadow = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, shadowCoord.xyz);
            float perObjShadow = MainLightPerObjectSceneShadow(samplePositionWS, normalWS, lightDir);
            float screenSpaceShadow = min(realtimeShadow, perObjShadow);

            // 注意考虑deviceDepth=0的情况下，我们认为shadowAttenuation为1。
            shadowAttenuation += 0.25f * lerp(1, screenSpaceShadow, step(Eps_float(), sampleDepth));
        }
        
        return shadowAttenuation < Eps_float() || shadowAttenuation > 1 - Eps_float() ? 0 : 1;
    }

    float PCSSBlurHorizontalFrag(Varyings input) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        float texelSize = _PenumbraMaskTex_TexelSize.x * 2.0;
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        // 9-tap gaussian blur on the downsampled source
        float m0 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0)).r;
        float m1 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0)).r;
        float m2 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0)).r;
        float m3 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0)).r;
        float m4 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv).r;
        float m5 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0)).r;
        float m6 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0)).r;
        float m7 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0)).r;
        float m8 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0)).r;

        float result =  m0 * 0.01621622 + m1 * 0.05405405 + m2 * 0.12162162 + m3 * 0.19459459
                        + m4 * 0.22702703
                        + m5 * 0.19459459 + m6 * 0.12162162 + m7 * 0.05405405 + m8 * 0.01621622;

        return result;
    }

    float PCSSBlurVerticalFrag(Varyings input) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        float texelSize = _PenumbraMaskTex_TexelSize.y;
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
        float m0 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.23076923)).r;
        float m1 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.38461538)).r;
        float m2 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv).r;
        float m3 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.38461538)).r;
        float m4 = SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.23076923)).r;

        float result =  m0 * 0.07027027 + m1 * 0.31621622
                        + m2 * 0.22702703
                        + m3 * 0.31621622 + m4 * 0.07027027;

        return result;
    }
    
    ENDHLSL
    
    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "Pcss Penumbra Mask"
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOW_BIAS_FRAGMENT
            #pragma vertex Vert
            #pragma fragment PCSSPenumbraMaskFrag
            
            ENDHLSL
        }
        
        Pass
        {
            Name "Pcss Blur Horizontal"
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment PCSSBlurHorizontalFrag
            
            ENDHLSL
        }
        
        Pass
        {
            Name "Pcss Blur Vertical"
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment PCSSBlurVerticalFrag
            
            ENDHLSL
        }
    }
}