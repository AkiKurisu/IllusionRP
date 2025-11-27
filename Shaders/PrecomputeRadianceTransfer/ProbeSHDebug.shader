Shader "Hidden/ProbeSHDebug"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
			"RenderType"="Opaque"
			"Queue"="Geometry+0"
			"UniversalMaterialType"="Lit"
        }
        LOD 100

        Pass
        {
            Name "Forward"
			Tags
			{
				"LightMode" = "UniversalForward"
			}
			
			ZWrite On
			ZTest LEqual
			
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalHarmonics.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
            StructuredBuffer<float> _coefficientSH9; // array size: 3x9=27
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.normal = normalize(o.normal);
                return o;
            }

            float4 frag (v2f input) : SV_Target
            {
                float3 dir = input.normal;

                // decode sh
                float3 c[9];
                for (int i = 0; i < 9; i++)
                {
                    c[i].x = _coefficientSH9[i * 3 + 0];
                    c[i].y = _coefficientSH9[i * 3 + 1];
                    c[i].z = _coefficientSH9[i * 3 + 2];
                }

                // decode irradiance
                float3 Lo = IrradianceSH9(c, dir.xzy); // PI is pre-divided
                return float4(Lo, 1.0);
            }
            ENDHLSL
        }
		
		Pass
		{
			Name "DepthNormals"
			Tags 
			{ 
				"LightMode" = "DepthNormals" 
		    }
			
			Blend One Zero
			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Hair/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				float4 ase_tangent : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
				float4 worldTangent : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			
			
			VertexOutput VertexFunction( VertexInput v)
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				
				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				float3 normalWS = TransformObjectToWorldNormal( v.ase_normal );
				float4 tangentWS = float4(TransformObjectToWorldDir( v.ase_tangent.xyz), v.ase_tangent.w);
				float4 positionCS = TransformWorldToHClip( positionWS );
				o.worldNormal = normalWS;
				o.worldTangent = tangentWS;
				o.clipPos = positionCS;
				o.clipPosV = positionCS;
				return o;
			}

			
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}

			half4 frag(	VertexOutput IN) : SV_TARGET
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );
				
				float3 WorldNormal = IN.worldNormal;
				return half4(NormalizeNormalPerPixel(WorldNormal), 0.0);
			}
			ENDHLSL
		}
    }
}
