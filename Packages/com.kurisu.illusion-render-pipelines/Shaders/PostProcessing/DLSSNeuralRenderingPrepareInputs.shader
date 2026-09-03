Shader "Hidden/IllusionRP/DLSSNeuralRendering/PrepareInputs"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "PrepareInputs"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _USE_DRAW_PROCEDURAL

            // URP's Core include defines the XR-aware TEXTURE2D_X sampling
            // macros and includes the shared point/linear clamp samplers.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D_X(_DLSSNeuralRenderingInputColor);
            TEXTURE2D_X(_DLSSNeuralRenderingInputDepth);
            TEXTURE2D_X(_DLSSNeuralRenderingInputMotion);

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct Outputs
            {
                float4 color : SV_Target0;
                float2 motion : SV_Target1;
                float depth : SV_Target2;
                float4 fallback : SV_Target3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            Outputs Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                Outputs output;
                output.color = SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingInputColor,
                    sampler_LinearClamp, input.uv);
                output.color.rgb = LinearToSRGB(saturate(output.color.rgb));
                output.fallback = output.color;
                // Preserve URP's raw device depth. Depth inversion is communicated
                // separately to NGX through SystemInfo.usesReversedZBuffer.
                output.depth = SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingInputDepth,
                    sampler_PointClamp, input.uv).r;
                // URP motion is previous-to-current UV/NDC. The C# dispatch scale
                // flips direction and converts it to full-resolution pixels.
                output.motion = SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingInputMotion,
                    sampler_PointClamp, input.uv).xy;
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ResolveOutput"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertResolve
            #pragma fragment FragResolve
            #pragma multi_compile _ _USE_DRAW_PROCEDURAL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D_X(_DLSSNeuralRenderingOutput);

            struct ResolveAttributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ResolveVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ResolveVaryings VertResolve(ResolveAttributes input)
            {
                ResolveVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 FragResolve(ResolveVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingOutput,
                    sampler_LinearClamp, input.uv);
                color.rgb = SRGBToLinear(saturate(color.rgb));
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DebugInputs"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertDebug
            #pragma fragment FragDebug
            #pragma multi_compile _ _USE_DRAW_PROCEDURAL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_DLSSNeuralRenderingInputDepth);
            TEXTURE2D_X(_DLSSNeuralRenderingInputMotion);
            TEXTURE2D_X(_DLSSNeuralRenderingInputColor);

            int _DLSSNeuralRenderingDebugMode;
            float _DLSSNeuralRenderingDebugMotionScaleX;
            float _DLSSNeuralRenderingDebugMotionScaleY;
            float _DLSSNeuralRenderingDebugMotionRange;
            float _DLSSNeuralRenderingDebugDepthRange;

            struct DebugAttributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DebugVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DebugVaryings VertDebug(DebugAttributes input)
            {
                DebugVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float3 MotionHeatmap(float value)
            {
                value = saturate(value);
                return saturate(float3(
                    1.5 - abs(4.0 * value - 3.0),
                    1.5 - abs(4.0 * value - 2.0),
                    1.5 - abs(4.0 * value - 1.0)));
            }

            float4 FragDebug(DebugVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                if (_DLSSNeuralRenderingDebugMode == 1)
                {
                    return SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingInputColor,
                        sampler_LinearClamp, input.uv);
                }

                if (_DLSSNeuralRenderingDebugMode == 2 || _DLSSNeuralRenderingDebugMode == 3)
                {
                    float2 rawMotion = SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingInputMotion,
                        sampler_PointClamp, input.uv).xy;
                    float2 motionPixels = rawMotion * float2(
                        _DLSSNeuralRenderingDebugMotionScaleX, _DLSSNeuralRenderingDebugMotionScaleY);
                    float range = max(_DLSSNeuralRenderingDebugMotionRange, 1e-4);
                    float magnitude = length(motionPixels) / range;

                    if (_DLSSNeuralRenderingDebugMode == 2)
                    {
                        // Zero motion is neutral gray. Red/green encode signed
                        // current-to-previous X/Y motion; blue encodes magnitude.
                        return float4(
                            saturate(0.5 + motionPixels.x / (2.0 * range)),
                            saturate(0.5 + motionPixels.y / (2.0 * range)),
                            saturate(magnitude), 1.0);
                    }

                    return float4(MotionHeatmap(magnitude), 1.0);
                }

                float rawDepth = SAMPLE_TEXTURE2D_X(_DLSSNeuralRenderingInputDepth,
                    sampler_PointClamp, input.uv).r;
                if (_DLSSNeuralRenderingDebugMode == 4)
                    return float4(rawDepth.xxx, 1.0);

                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float normalizedDepth = saturate(eyeDepth / max(_DLSSNeuralRenderingDebugDepthRange, 1e-4));
                return float4(normalizedDepth.xxx, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
