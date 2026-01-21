Shader "Hidden/StencilVRS"
{
	Properties
	{
		_ShadingRateColor1x1("_ShadingRateColor1x1", Color) = (0, 0, 0, 0)
        _ShadingRateColor2x2("_ShadingRateColor2x2", Color) = (0, 0, 0, 0)
        _ShadingRateColor4x4("_ShadingRateColor4x4", Color) = (0, 0, 0, 0)
	}
	SubShader
	{
		Pass
		{
			HLSLPROGRAM

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
			#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
			
			#pragma multi_compile_instancing
			#pragma vertex Vert
			#pragma fragment Frag
			
			CBUFFER_START(UnityPerMaterial)
	        float4 _ShadingRateColor1x1;
	        float4 _ShadingRateColor2x2;
	        float4 _ShadingRateColor4x4;
	        CBUFFER_END
			
			TEXTURE2D_X_UINT2(_StencilTexture);
			
			float4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				
				float2 screenPos = input.texcoord.xy * _ScreenSize.xy;
				uint stencilValue = GetStencilValue(LOAD_TEXTURE2D(_StencilTexture, screenPos));
			    bool noSSR = (stencilValue & STENCIL_USAGE_IS_SSR) == 0;
				bool noHair = (stencilValue & STENCIL_USAGE_IS_HAIR) == 0;
				bool noSkin = (stencilValue & STENCIL_USAGE_IS_SKIN) == 0;
				bool noSSS = (stencilValue & STENCIL_USAGE_SUBSURFACE_SCATTERING) == 0;
				bool lowRate = noSSR & noHair & noSkin && noSSS;
				bool mediumRate = noSSR && noSSS;
			    return lowRate ? _ShadingRateColor4x4 : mediumRate ? _ShadingRateColor2x2 : _ShadingRateColor1x1;
			}
			ENDHLSL
		}
	}
}
