// Modified from https://zhuanlan.zhihu.com/p/583108480
Shader "Hidden/SubsurfaceScattering"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/SubsurfaceScattering/SubsurfaceScattering.hlsl"
        #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"
        
        half4 Fragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            
            float2 uv = input.texcoord.xy;
            float2 posSS = uv * _ScreenSize.xy;
            
            float depth = SampleSceneDepth(uv).r;
            
            PositionInputs posInput = GetPositionInput(posSS, _ScreenSize.zw);

            // The result of the stencil test allows us to statically determine the material type (SSS).
            SSSData sssData;
            DecodeFromSSSBuffer(posInput.positionSS, sssData);
            
           // Reconstruct the view-space position corresponding to the central sample.
            float2 centerPosNDC = posInput.positionNDC;
            float2 cornerPosNDC = centerPosNDC + 0.5 * _ScreenSize.zw;
            
            float3 centerPosVS = ComputeViewSpacePosition(centerPosNDC, depth, _GlobalInvProjMatrix);
            float3 cornerPosVS = ComputeViewSpacePosition(cornerPosNDC, depth, _GlobalInvProjMatrix);
            
            float linearDepth = LinearEyeDepth(depth, _ZBufferParams);

            float metersPerUnit = _WorldScalesAndFilterRadiiAndThicknessRemaps[sssData.diffusionProfileIndex].x;
            float filterRadius = _WorldScalesAndFilterRadiiAndThicknessRemaps[sssData.diffusionProfileIndex].y;
            
            // Rescaling the filter is equivalent to inversely scaling the world.
            float mmPerUnit  = MILLIMETERS_PER_METER * metersPerUnit;
            float unitsPerMm = rcp(mmPerUnit);
            
            // Compute the view-space dimensions of the pixel as a quad projected onto geometry.
            // Assuming square pixels, both X and Y are have the same dimensions.
            float unitsPerPixel = max(0.0001f, 2.0 * abs(cornerPosVS.x - centerPosVS.x));
            float pixelsPerMm = rcp(unitsPerPixel) * unitsPerMm;

            // Area of a disk.
            float filterArea = PI * Sq(filterRadius * pixelsPerMm);
            uint sampleCount = (uint)(filterArea / SSS_PIXELS_PER_SAMPLE);
            uint sampleBudget = 40; // Medium
            
            uint n = min(sampleCount, sampleBudget);
            float3 S = _ShapeParamsAndMaxScatterDists[sssData.diffusionProfileIndex].rgb;
            float d = _ShapeParamsAndMaxScatterDists[sssData.diffusionProfileIndex].a;
            float2 pixelCoord = posSS;
            float3 totalIrradiance = 0;
            float3 totalWeight = 0;
            
            float3 albedo = sssData.diffuseColor;
#ifdef _ILLUSION_RENDER_PASS_ENABLED
			float3 centerIrradiance = LOAD_FRAMEBUFFER_INPUT(SUBSURFACE_DIFFUSE, input.positionCS.xy).rgb;
#else
            float3 centerIrradiance  = SAMPLE_TEXTURE2D_X(_SubsurfaceDiffuse, sampler_SubsurfaceDiffuse, uv).rgb;
#endif
            
#if SSS_RANDOM_ROTATION
            // Note that GenerateHashedRandomFloat() only uses the 23 low bits, hence the 2^24 factor.
            float phase = TWO_PI * GenerateHashedRandomFloat(uint3(pixelCoord, (uint)(depth * 16777216)));
#else
            float phase = 0;
#endif
            
            UNITY_UNROLL
            for (uint i = 0; i < n; i++)
            {
                // Integrate over the image or tangent plane in the view space.
                EvaluateSample(i, n, pixelCoord,
                    0, S, d,
                       mmPerUnit, pixelsPerMm, phase, 
                       totalIrradiance, totalWeight, linearDepth);
            }
            
            if (dot(totalIrradiance, float3(1, 1, 1)) == 0.0)
            {
                return float4(centerIrradiance * albedo, 1.0);
            }
            totalWeight = max(totalWeight, FLT_MIN);
            return float4(albedo * totalIrradiance / totalWeight, 1.0); // Post-Scatter which also save one sampler in forward pass.
        }
        ENDHLSL

        Pass
        {
            // 0
            Name "Subsurface Scattering Pass"

            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5
            #pragma vertex   Vert
            #pragma fragment Fragment

            #pragma multi_compile_fragment _ _ILLUSION_RENDER_PASS_ENABLED
            
            ENDHLSL
        }
    }
}