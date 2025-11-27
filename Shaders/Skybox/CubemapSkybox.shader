// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Universal Render Pipeline/Cubemap Skybox"
{
	Properties
	{
		_Tint ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
		[Gamma] _Exposure ("Exposure", Range(0, 8)) = 1
		_Rotation ("Rotation", Range(0, 360)) = 0
		[NoScaleOffset] _Tex ("Cubemap   (HDR)", Cube) = "grey" {}
	}
	SubShader
	{
		Tags
		{
			"PreviewType" = "Skybox"
			"Queue" = "Background"
			"RenderType" = "Background"
		}
		
		ZWrite Off
		Cull Off
		
		Pass
		{
			CGPROGRAM
			
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			
			#include "UnityCG.cginc"
			
			struct appdata
		    {
		        float4 position : POSITION;
		        float3 texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
		    };

			struct v2f
		    {
		        float4 position : SV_POSITION;
		        float3 texcoord : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
		    };
			
			samplerCUBE _Tex;
			float _Rotation;
			float4 _Tex_HDR;
			float4 _Tint;
			float _Exposure;

			sampler2D _ExposureTexture;

			float3 RotateAboutAxis_Radians(float3 In, float3 Axis, float Rotation)
			{
			    float s = sin(Rotation);
			    float c = cos(Rotation);
			    float one_minus_c = 1.0 - c;
			    Axis = normalize(Axis);
			    float3x3 rot_mat = {
			        one_minus_c * Axis.x * Axis.x + c,
			        one_minus_c * Axis.x * Axis.y - Axis.z * s,
			        one_minus_c * Axis.z * Axis.x + Axis.y * s,
			        one_minus_c * Axis.x * Axis.y + Axis.z * s,
			        one_minus_c * Axis.y * Axis.y + c,
			        one_minus_c * Axis.y * Axis.z - Axis.x * s,
			        one_minus_c * Axis.z * Axis.x - Axis.y * s,
			        one_minus_c * Axis.y * Axis.z + Axis.x * s,
			        one_minus_c * Axis.z * Axis.z + c
			    };
			    return  mul(rot_mat, In);
			}

			inline float GetCurrentExposureMultiplier()
			{
				return tex2D(_ExposureTexture, int2(0, 0)).x;
			}
			
			v2f vert(appdata v)
		    {
		        v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				const float3 axis = float3(0, 1, 0);
				v.position.xyz = RotateAboutAxis_Radians(v.position.xyz, axis, (360.0f - _Rotation) * 0.0174533);
		        o.position = UnityObjectToClipPos(v.position);
		        o.texcoord = v.texcoord;
		        return o;
		    }
			
			fixed4 frag (v2f i) : SV_Target
	        {
	            half4 tex = texCUBE (_Tex, i.texcoord);
	            half3 c = DecodeHDR (tex, _Tex_HDR);
	            c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
	            c *= _Exposure * GetCurrentExposureMultiplier();
	            return half4(c, 1);
	        }
			ENDCG
		}
	}
}