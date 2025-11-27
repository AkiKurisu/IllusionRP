#ifndef ILLUSION_BRDF_INCLUDED
#define ILLUSION_BRDF_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"

#define GGX_UE             0           // Use Unreal Engine GGX model for comparison purpose.

inline half Pow5(half x)
{
    return x * x * x * x * x;
}

// ===================================== BRDF =============================== //

// [Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"]
float3 F_Schlick_UE4(float3 SpecularColor, float VoH)
{
    float Fc = Pow5(1 - VoH );					// 1 sub, 3 mul
    //return Fc + (1 - Fc) * SpecularColor;		    // 1 add, 3 mad
        
    // Anything less than 2% is physically impossible and is instead considered to be shadowing
    return saturate( 50.0 * SpecularColor.g) * Fc + (1 - Fc) * SpecularColor;
}

// [Walter et al. 2007, "Microfacet models for refraction through rough surfaces"]
float D_GGX_UE4( float a2, float NoH )
{
    float d = (NoH * a2 - NoH) * NoH + 1;	    // 2 mad
    return a2 / (PI * d * d );					// 4 mul, 1 rcp
}

// [Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"]
float Vis_SmithJointApprox(float a, float NoV, float NoL )
{
    float Vis_SmithV = NoL * (NoV * (1 - a) + a);
    float Vis_SmithL = NoV * (NoL * (1 - a) + a);
    return 0.5 * rcp(Vis_SmithV + Vis_SmithL);
}

// Reference: [Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"]
half V_SmithJointAniso(float ax, float ay, half NoV, half NoL, half XoV, half XoL, half YoV, half YoL)
{
    float Vis_SmithV = NoL * length(float3(ax * XoV, ay * YoV, NoV));
    float Vis_SmithL = NoV * length(float3(ax * XoL, ay * YoL, NoL));
    return 0.5 * INV_PI * rcp(Vis_SmithV + Vis_SmithL);
}

// Reference: https://www.slideshare.net/slideshow/custom-fabric-shader-for-unreal-engine-4/60751176#11
half3 Diffuse_OrenNayar(half NoV, half3 albedo, half roughness)
{
    // replace NoL with one
    half lambda = -0.5 * NoV + 1;
    half lambda2 = (1 - lambda);
    half fakey = (1 - lambda2 * lambda2) * 0.62;
    return lerp(1, fakey, roughness) * albedo;
}

half D_AshikhminNoPI(half NoH, half roughness2)
{
    // Ashikhmin 2007, "Distribution-based BRDFs"
    half cos2h = NoH * NoH;
    half sin2h = max(1.0 - cos2h, 0.0078125); // 2^(-14/2), so sin2h^2 > 0 in fp16
    half sin4h = sin2h * sin2h;
    half cot2 = -cos2h / (roughness2 * sin2h);
    return (4.0 * roughness2 + 1.0) * sin4h * (4.0 * exp(cot2) + sin4h);
}

half D_Ashikhmin(half NoH, half roughness2)
{
    return D_AshikhminNoPI(NoH, roughness2) * INV_PI;
}

half V_Neubelt(half NoL, half NoV)
{
    return rcp(4 * (NoL + NoV - NoL * NoV));
}

// [Burley 2012, "Physically-Based Shading at Disney"]
// Fix precision artifacts
// Inline D_GGXAniso() * V_SmithJointGGXAniso() together for better code generation.
half DV_SmithJointGGXAniso_Patch(half TdotH, half BdotH, half NdotH, half NdotV,
                                 half TdotL, half BdotL, half NdotL,
                                 half roughnessT, half roughnessB, half partLambdaV)
{
    float a2 = roughnessT * roughnessB;
    float3 v = float3(roughnessB * TdotH, roughnessT * BdotH, a2 * NdotH);
    float s = dot(v, v);

    float lambdaV = NdotL * partLambdaV;
    float lambdaL = NdotV * length(float3(roughnessT * TdotL, roughnessB * BdotL, NdotL));
    
    float2 D = float2(a2 * a2 * a2, s * s);     // Fraction without the multiplier (1/Pi)
    float2 G = float2(1, lambdaV + lambdaL);    // Fraction without the multiplier (1/2)

    // This function is only used for direct lighting.
    // If roughness is 0, the probability of hitting a punctual or directional light is also 0.
    // Therefore, we return 0. The most efficient way to do it is with a max().
    return half(INV_PI * 0.5 * (D.x * G.x) / max(D.y * G.y, FLT_MIN));
}

// Patched version for half precision
half DV_SmithJointGGXAniso_Patch(half TdotH, half BdotH, float NdotH,
                                 half TdotV, half BdotV, half NdotV,
                                 half TdotL, half BdotL, half NdotL,
                                 half roughnessT, half roughnessB)
{
    half partLambdaV = GetSmithJointGGXAnisoPartLambdaV(TdotV, BdotV, NdotV, roughnessT, roughnessB);
    return DV_SmithJointGGXAniso_Patch(TdotH, BdotH, NdotH, NdotV,
                                       TdotL, BdotL, NdotL,
                                       roughnessT, roughnessB, partLambdaV);
}
// ===================================== BRDF =============================== //

// ================================= Vertex Lighting ======================== //

half3 LightingLambert(half3 lightColor, half3 lightDir, half3 normal)
{
    half NdotL = saturate(dot(normal, lightDir));
    return lightColor * NdotL;
}

half3 VertexLighting(float3 positionWS, half3 normalWS)
{
    half3 vertexLightColor = half3(0.0, 0.0, 0.0);

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    uint lightsCount = GetAdditionalLightsCount();
    uint meshRenderingLayers = GetMeshRenderingLayer();

    LIGHT_LOOP_BEGIN(lightsCount)
        Light light = GetAdditionalLight(lightIndex, positionWS);

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
 #endif
    {
        half3 lightColor = light.color * light.distanceAttenuation;
        vertexLightColor += LightingLambert(lightColor, light.direction, normalWS);
    }

    LIGHT_LOOP_END
#endif

    return vertexLightColor;
}
// ================================= Vertex Lighting ======================== //

// ================================= Direct Lighting ======================== //
half DirectBRDFDiffuseTermNoPI(float NdotL, float clampNdotV, float LdotV, half perceptualRoughness)
{
#if USE_DIFFUSE_LAMBERT_BRDF
    half diffTerm = 1;
#else
    half diffTerm = DisneyDiffuseNoPI(clampNdotV, abs(NdotL), LdotV, perceptualRoughness);
#endif
    return diffTerm;
}

half DirectBRDFDiffuseTerm(float NdotL, float clampNdotV, float LdotV, half perceptualRoughness)
{
#if USE_DIFFUSE_LAMBERT_BRDF
    half diffTerm = Lambert();
#else
    half diffTerm = DisneyDiffuse(clampNdotV, abs(NdotL), LdotV, perceptualRoughness);
#endif
    return diffTerm;
}

half3 GGXBRDFSpecular(BRDFData brdfData, half3 normalWS, half3 lightDirectionWS, half3 viewDirectionWS)
{
    float3 halfDir = SafeNormalize(float3(lightDirectionWS) + float3(viewDirectionWS));
    float NoH = saturate(dot(float3(normalWS), halfDir));
    float NdotL = dot(float3(normalWS), float3(lightDirectionWS));
    float NdotV = dot(float3(normalWS), float3(viewDirectionWS));
    float clampNdotV = ClampNdotV(NdotV);

#if GGX_UE
    float VoH = saturate(dot(viewDirectionWS, halfDir));
    float D = D_GGX_UE4(brdfData.roughness2, NoH);
    float V = Vis_SmithJointApprox(brdfData.roughness, clampNdotV, NdotL);
    float3 F = F_Schlick_UE4(brdfData.specular, VoH);
    half3 specularTerm = half3(D * V * F);
#else
    // Use HDRP reference
    float LoH = saturate(dot(lightDirectionWS, halfDir));
    float partLambdaV = GetSmithJointGGXPartLambdaV(clampNdotV, brdfData.roughness);
    float3 F = F_Schlick(brdfData.specular, LoH);
    float DV = DV_SmithJointGGX(NoH, abs(NdotL), clampNdotV, brdfData.roughness, partLambdaV);
    half3 specularTerm = half3(DV * F);
#endif

    // @IllusionRP:
    // Since URP does not multiply INV_PI in Lambert Diffuse for artistic design considerations.
    // Specular term need multiply PI to maintain energy conservation.
    specularTerm *= PI;

#if REAL_IS_HALF
    specularTerm = specularTerm - HALF_MIN;
    specularTerm = clamp(specularTerm, 0.0, 1000.0); // Prevent FP16 overflow on mobiles
#endif
    return specularTerm;
}

half3 LitSpecular(BRDFData brdfData, half3 normalWS, half3 lightDirectionWS, half3 viewDirectionWS)
{
#if GGX_BRDF
    return GGXBRDFSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS);
#else
    return DirectBRDFSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS) * brdfData.specular;
#endif
}
// ================================= Direct Lighting ======================== //
#endif