Shader "Hidden/Illusion/PathTracingRectangleLight"
{
    Properties { [HDR] _EmissionColor("Emission", Color) = (0,0,0,1) }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "PathTracingEmission" = "Constant" }
        Cull Off
        Pass
        {
            Name "PathTracing"
            Tags { "LightMode" = "PathTracing" }
            HLSLPROGRAM
            #pragma raytracing PathTracing
            #define _SPECULAR_SETUP
            #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingHit.hlsl"
            #include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingLitSurface.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
            CBUFFER_END
            [shader("closesthit")]
            void PathTracingClosestHit(inout IllusionPathPayload payload : SV_RayPayload, AttributeData attributes : SV_IntersectionAttributes)
            {
                PathTracingHitContext hit = PathTracingGetHitContext(attributes, payload);
                PathTracingLitSurface surface = PathTracingInitLitSurface();
                surface.normalWS = hit.vertexNormalWS;
                surface.albedo = 0;
                surface.specular = 0;
                surface.smoothness = 0;
                surface.emission = hit.frontFacing ? _EmissionColor.rgb : 0;
                PathTracingWriteLitSurface(payload, hit, surface);
            }
            ENDHLSL
        }
    }
}
