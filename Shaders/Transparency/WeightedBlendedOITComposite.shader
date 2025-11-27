Shader "Hidden/WeightedBlendedOITComposite"
{
	SubShader
	{
		Pass
		{
			Name "Blend Color"
			
			Blend OneMinusSrcAlpha SrcAlpha
			ZTest Off
			ZWrite Off
			ZClip Off
			Cull Off
			
			HLSLPROGRAM
			#pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5
			
			#pragma vertex Vert
			#pragma fragment Frag

			#pragma multi_compile_fragment _ _ILLUSION_RENDER_PASS_ENABLED
			
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
			#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ShaderVariables.hlsl"

#ifdef _ILLUSION_RENDER_PASS_ENABLED
		    #define ACCUM		0
		    #define REVEALAGE	1

		    FRAMEBUFFER_INPUT_FLOAT(ACCUM);
		    FRAMEBUFFER_INPUT_FLOAT(REVEALAGE);
#else
			TEXTURE2D(_AccumTex);
			SAMPLER(sampler_AccumTex);

			TEXTURE2D(_RevealageTex);
			SAMPLER(sampler_RevealageTex);
#endif
			
			float4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				
#ifdef _ILLUSION_RENDER_PASS_ENABLED
				float4 accum = LOAD_FRAMEBUFFER_INPUT(ACCUM, input.positionCS.xy).xyzw;
				float reveal = LOAD_FRAMEBUFFER_INPUT(REVEALAGE, input.positionCS.xy).x;
#else
				float4 accum = SAMPLE_TEXTURE2D_X(_AccumTex, sampler_AccumTex, input.texcoord).xyzw;
				float reveal = SAMPLE_TEXTURE2D_X(_RevealageTex, sampler_RevealageTex, input.texcoord).x;
#endif
				
				// Blend Func: GL_ONE_MINUS_SRC_ALPHA, GL_SRC_ALPHA
				float3 finalColor = accum.rgb * (GetCurrentExposureMultiplier() / clamp(accum.a, float(1e-4), float(5e4)));
				return half4(finalColor, reveal);
			}
			ENDHLSL
		}
	} 
}
