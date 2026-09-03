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

    static const float2 offset[9] =
    {
        float2(0, 0),
        float2(-1, 0),
        float2(1, 0),
        float2(0, -1),
        float2(0, 1),
        float2(-1, 1),
        float2(1, 1),
        float2(-1, -1),
        float2(1, -1)
    };

    float SampleHardScreenSpaceShadow(float2 uv)
    {
        float sampleDepth = SampleSceneDepth(uv);
#if !UNITY_REVERSED_Z
        sampleDepth = sampleDepth * 2.0 - 1.0;
#endif

        float3 samplePositionWS = ComputeWorldSpacePosition(uv, sampleDepth, unity_MatrixInvVP);
        float3 biasPositionWS = samplePositionWS;
        float3 lightDir = GetMainLight().direction;
        float3 normalWS = 0;
#if APPLY_SHADOW_BIAS_FRAGMENT
        float2 positionCS = uv * _ScreenSize.xy;
        normalWS = LoadSceneNormals(positionCS);
        biasPositionWS = IllusionApplyShadowBias(samplePositionWS, normalWS, lightDir);
#endif
        float4 shadowCoord = TransformWorldToShadowCoord(biasPositionWS);

        float realtimeShadow = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, shadowCoord.xyz);
        float perObjShadow = MainLightPerObjectSceneShadow(samplePositionWS, normalWS, lightDir);
        float screenSpaceShadow = min(realtimeShadow, perObjShadow);

        return lerp(1, screenSpaceShadow, step(Eps_float(), sampleDepth));
    }

    // Fade conservative mask expansion with camera distance. Close receivers need a wider
    // screen-space guard band; distant receivers can use a tighter mask to save PCSS work.
    int ComputePenumbraMaskDilation(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float fade = saturate((eyeDepth - _PenumbraMaskDilationParams.z) * _PenumbraMaskDilationParams.w);
        return (int)round(lerp(_PenumbraMaskDilationParams.y, _PenumbraMaskDilationParams.x, fade));
    }

    float PCSSPenumbraMaskFrag(Varyings input) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float minShadow = 1;
        float maxShadow = 0;
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        float2 maskTexelRadius = 0.5 * _PenumbraMaskTexelSize.xy;

        // Build a conservative edge mask over the full low-res mask texel footprint.
        // Missing an edge is worse than overmarking one, because misses fall back to hard shadow.
        for(int i = 0; i < 9; ++i)
        {
            float shadow = SampleHardScreenSpaceShadow(uv + offset[i] * maskTexelRadius);
            minShadow = min(minShadow, shadow);
            maxShadow = max(maxShadow, shadow);
        }
        
        return minShadow < 1 - Eps_float() && maxShadow > Eps_float() ? 1 : 0;
    }

    float PCSSBlurHorizontalFrag(Varyings input) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        float texelSize = _PenumbraMaskTexelSize.x;
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        int dilation = ComputePenumbraMaskDilation(uv);
        float result = 0;

        // Max dilation keeps the mask binary/conservative. Gaussian blur would create
        // fractional coverage that becomes a visible gate when sampled with an epsilon test.
        UNITY_LOOP
        for (int i = -16; i <= 16; ++i)
        {
            if (abs(i) <= dilation)
            {
                result = max(result, SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(texelSize * i, 0.0)).r);
            }
        }

        return result;
    }

    float PCSSBlurVerticalFrag(Varyings input) : SV_TARGET
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        float texelSize = _PenumbraMaskTexelSize.y;
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        int dilation = ComputePenumbraMaskDilation(uv);
        float result = 0;

        // Match the horizontal max dilation to form a rectangular conservative guard band.
        UNITY_LOOP
        for (int i = -16; i <= 16; ++i)
        {
            if (abs(i) <= dilation)
            {
                result = max(result, SAMPLE_TEXTURE2D_X(_PenumbraMaskTex, sampler_LinearClamp, uv + float2(0.0, texelSize * i)).r);
            }
        }

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
