Shader "Hidden/DrawDiffusionProfile"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Cull   Off
            ZTest  Always
            ZWrite Off
            Blend  Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            //-------------------------------------------------------------------------------------
            // Inputs & outputs
            //-------------------------------------------------------------------------------------

            float4 _ShapeParam; float _MaxRadius; // See 'DiffusionProfile'

            //-------------------------------------------------------------------------------------
            // Implementation
            //-------------------------------------------------------------------------------------
            
            struct Attributes
            {
                float3 vertex     : POSITION;
                float2 texcoord   : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex      : SV_POSITION;
                float2 texcoord    : TEXCOORD0;
            };

            // Performs sampling of the Normalized Burley diffusion profile in polar coordinates.
            // The result must be multiplied by the albedo.
            float3 EvalBurleyDiffusionProfile(float r, float3 S)
            {
                float3 exp_13 = exp2(((LOG2_E * (-1.0/3.0)) * r) * S); // Exp[-S * r / 3]
                float3 expSum = exp_13 * (1 + exp_13 * exp_13);        // Exp[-S * r / 3] + Exp[-S * r]

                return (S * rcp(8 * PI)) * expSum; // S / (8 * Pi) * (Exp[-S * r / 3] + Exp[-S * r])
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // We still use the legacy matrices in the editor GUI
                output.vertex   = mul(unity_MatrixVP, float4(input.vertex, 1));
                output.texcoord = input.texcoord.xy;
                return output;
            }
            
            float4 Frag(Varyings input) : SV_Target
            {
                // Profile display does not use premultiplied S.
                float  r = _MaxRadius * 0.5 * length(input.texcoord - 0.5); // (-0.25 * R, 0.25 * R)
                float3 S = _ShapeParam.rgb;
                float3 M;

                // Gamma in previews is weird...
                S = S * S;
                M = EvalBurleyDiffusionProfile(r, S) / r; // Divide by 'r' since we are not integrating in polar coords
                return float4(sqrt(M), 1);
            }
            ENDHLSL
        }
    }
}
