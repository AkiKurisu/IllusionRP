// Hair lighting
#ifndef UNIVERSAL_LIGHTING_INCLUDED
#define UNIVERSAL_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/Core.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/EvaluateMaterial.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/Hair/HairDefine.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PreIntegratedFGD/PreIntegratedFGD.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/LightingData.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/ForwardLightLoop.hlsl"

///////////////////////////////////////////////////////////////////////////////
//                     Math Functions                                        //
///////////////////////////////////////////////////////////////////////////////

// max absolute error 9.0x10^-3
// Eberly's polynomial degree 1 - respect bounds
// 4 VGPR, 12 FR (8 FR, 1 QR), 1 scalar
// input [-1, 1] and output [0, PI]
half acosFast(half inX) 
{
    half x = abs(inX);
    half res = -0.156583f * x + 0.5 * half(PI);
    res *= sqrt(1.0f - x);
    return inX >= 0 ? res : half(PI) - res;
}


// Same cost as acosFast + 1 FR
// Same error
// input [-1, 1] and output [-PI/2, PI/2]
half asinFast(half x)
{
    return 0.5 * half(PI) - acosFast(x);
}

inline half Pow2(half x)
{
    return x * x;
}

///////////////////////////////////////////////////////////////////////////////
//                      Lighting Functions                                   //
///////////////////////////////////////////////////////////////////////////////

// Ref: "Light Scattering from Human Hair Fibers"
// Longitudinal scattering as modeled by a normal distribution.
// To be used as an approximation to d'Eon et al's Energy Conserving Longitudinal Scattering Function.
// TODO: Move me to BSDF.hlsl

real3 D_LongitudinalScatteringGaussian(real3 thetaH, real3 beta)
{
    beta = max(beta, 1e-5); // zero-div guard

    const real sqrtTwoPi = 2.50662827463100050241;
    return rcp(beta * sqrtTwoPi) * exp(-Sq(thetaH) / (2 * Sq(beta)));
}

float ModifiedRefractionIndex(float cosThetaD)
{
    // Original derivation of modified refraction index for arbitrary IOR.
    // float sinThetaD = sqrt(1 - Sq(cosThetaD));
    // return sqrt(Sq(eta) - Sq(sinThetaD)) / cosThetaD;

    // Karis approximation for the modified refraction index for human hair (1.55)
    return 1.19 / cosThetaD + (0.36 * cosThetaD);
}

// Gaussian Distribution for M term
inline float Hair_G(float B, float Theta)
{
    // Clamp B for the denominator term, as otherwise the Gaussian normalization returns too high value.
    // This clamps allow to prevent large value for low roughness, while keeping the highlight shape/sharpness 
    // similar.
    const float DenominatorB = max(B, 0.01f); // @IllusionRP: Very important when light intensity is very high.
    const float SQRT2PI  = 2.50663f;
    return exp(-0.5 * Pow2(Theta) / (B * B)) / (SQRT2PI * DenominatorB);
}

// Const IOR for Hair Fresnel
inline half Hair_F(half u)
{
    // const half n = 1.55;
    // const half F0 = Pow2((1 - n) / (1 + n));
    // return F0 + (1 - F0) * pow(1 - vDotH, 5);
    
    return F_Schlick(DEFAULT_HAIR_SPECULAR_VALUE, u);
}

// Reference: [Hair Rendering and Shading]
half KajiyaKay(half3 T, half3 H, half specularExponent)
{
    half TdotH = dot(T, H);
    half sinTHSq = saturate(1.0 - TdotH * TdotH);

    half dirAttention = smoothstep(-1.0, 0.0, sinTHSq);

    // No energy conservation
    return dirAttention * pow(sinTHSq, specularExponent);
}

bool ShouldEvaluateThickObjectTransmission(half3 L, half3 normalWS)
{
    // Currently, we don't consider (NdotV < 0) as transmission.
    // TODO: ignore normal map? What about double sided-surfaces with one-sided normals?
    float NdotL = dot(normalWS, L);
    return NdotL < float(0.0);
}

// Reference: [The Process of Creating Volumetric-based Materials in Uncharted 4]
half3 HairVolumetricBacklitScatter(half3 albedo, half3 L, half3 V, half3 N, HairData hairData)
{
    const half ScatterPower = 9;
    const half lightScale = 1;

    half3 NoisedL = normalize(L + hairData.Noise * 0.25);
    half NdotL = dot(N, NoisedL);
    half NdotV = dot(N, V);
    half VdotL = dot(V, NoisedL);
    half CosThetaL = saturate(abs(NdotL));
    half CosThetaV = saturate(abs(NdotV));
    
    half3 scatterFresnel = pow(1.0 - CosThetaV, ScatterPower);
    half3 scatterLight = pow(saturate(-VdotL), ScatterPower) * 
                        (1.0 - CosThetaV) *
                        saturate(1.0 - CosThetaL);
    
    half3 transAmount = scatterFresnel + lightScale * scatterLight;
    return lerp(1, albedo, 0.8) * hairData.Tint * transAmount * hairData.Backlit;
}

half3 KajiyaKayDiffuseAttenuation(half3 Albedo, half3 L, half3 V, half3 N, half Scatter, half Shadow)
{
    half KajiyaDiffuse = 1 - abs(dot(N, L));
    
    half3 FakeNormal = normalize(V - N * dot(V, N));
    N = FakeNormal;
    
    // Hack approximation for multiple scattering.
    float MinValue = 0.0001f;
    half Wrap = 1;
    half NoL = saturate((dot(N, L) + Wrap) / Pow2(1 + Wrap));
    half DiffuseScatter = lerp(NoL, KajiyaDiffuse, 0.33) * Scatter; // * INV_PI
    half Luma = Luminance(Albedo);
    half3 BaseOverLuma = abs(Albedo / max(Luma, MinValue));
    half3 ScatterTint = Shadow < 1 ? pow(BaseOverLuma, 1 - Shadow) : 1;
    return sqrt(abs(Albedo)) * DiffuseScatter * ScatterTint;
}

half3 KajiyaKayDiffuseAttenuation(half3 Albedo, half3 L, half3 V, half3 N, HairData HairData)
{
    const half Scatter = 0.5f;
    Albedo = saturate(ComputeDiffuseColor(Albedo, HairData.Metallic));
    return KajiyaKayDiffuseAttenuation(Albedo, L, V, HairData.Tangent, Scatter, HairData.Shadow);
}

// Reference: [The Process of Creating Volumetric-based Materials in Uncharted 4]
half3 UnchartedDiffuseAttenuation(half3 Albedo, half3 L, half3 V, half3 N, HairData HairData)
{
    Albedo = saturate(ComputeDiffuseColor(Albedo, HairData.Metallic));
    float NdotL = dot(N, L);
    half Wrap = 0.5f;
    half3 scatterColor = lerp(float3(0.992, 0.808, 0.518), Albedo, 0.5); 
    half3 diffuse = saturate(NdotL + Wrap) / (1 + Wrap); // 0 < w <1
    half3 scatterLight = saturate(scatterColor + saturate(NdotL)) * diffuse;
    return Albedo * scatterLight * HairData.Shadow; // * INV_PI
}

half3 HairKajiyaKay(BRDFData brdfData, half3 L, half3 V, float3 N, HairData HairData)
{
    float3 T = HairData.Tangent;
    float clampedNdotL = saturate(dot(N, L));
    half LdotV = dot(L, V);
    half invLenLV = rsqrt(max(2.0 * LdotV + 2.0, FLT_EPS));    // invLenLV = rcp(length(L + V)), clamp to avoid rsqrt(0) = inf, inf * 0 = NaN
    half LdotH = saturate(invLenLV * LdotV + invLenLV);
    half3 H = normalize(L + V);
    half Shift1 = 0.015;
    half Shift2 = 0.015 * 2;
    half3 T1 = ShiftTangent(T, N, Shift1 + HairData.Noise);
    half3 T2 = ShiftTangent(T, N, Shift2 + HairData.Noise);

    half F = Hair_F(LdotH);
    
    half3 spec1 = D_KajiyaKay(T1, H, 50) * 0.1f;
    half3 spec2 = D_KajiyaKay(T2, H,  HairData.HighLight * 4000) * 0.12f;
    
    // Bypass the normal map...
    float geomNdotV = dot(HairData.GeomNormal, V);

    // G = NdotL * NdotV.
    half3 specR = 0.25 * F * (spec1 + spec2) * clampedNdotL * saturate(geomNdotV * FLT_MAX);

    specR *= HairData.Tint;
    specR = -min(-specR, 0);
    
#if REAL_IS_HALF
    specR = specR - HALF_MIN;
    specR = clamp(specR, 0.0, 1000.0); // Prevent FP16 overflow on mobiles
#endif

    return specR;
}

// Ref: A Practical and Controllable Hair and Fur Model for Production Path Tracing Eq. 9
float3 AbsorptionFromReflectance(float3 diffuseColor, float azimuthalRoughness)
{
    float beta  = azimuthalRoughness;
    float beta2 = beta  * beta;
    float beta3 = beta2 * beta;
    float beta4 = beta3 * beta;
    float beta5 = beta4 * beta;

    // Least squares fit of an inverse mapping between scattering parameters and scattering albedo.
    float denom = 5.969 - (0.215 * beta) + (2.532 * beta2) - (10.73 * beta3) + (5.574 * beta4) + (0.245 * beta5);
    
    return Pow2(log(diffuseColor) / denom);
}

half3 HairMarschnerNoPI(BRDFData brdfData, half3 L, half3 V, float3 N, HairData HairData)
{
    float3 T = ShiftTangent(HairData.Tangent, N, HairData.Noise);

    float3 specR = 0;
    half ClampedRoughness = clamp(HairData.Roughness, 1 / 255.0f, 1.0f);
    half VoL = dot(V, L);
    half SinThetaL = clamp(dot(T, L), -1.0f, 1.0f);
    half SinThetaV = clamp(dot(T, V), -1.0f, 1.0f);
    float CosThetaD = cos(0.5 * abs(asinFast(SinThetaV) - asinFast(SinThetaL)));

    float3 Lp = L - SinThetaL * T;
    float3 Vp = V - SinThetaV * T;
    float CosPhi = dot(Lp, Vp) * rsqrt(dot(Lp, Lp) * dot(Vp, Vp) + 1e-4);
    float CosHalfPhi = sqrt(saturate(0.5 + 0.5 * CosPhi));

    // half n = 1.55;
    float n_prime = 1.19 / CosThetaD + 0.36 * CosThetaD;

    half Shift = HAIR_SHIFT_VALUE;
    half Alpha[] =
    {
        -Shift * 2,
        Shift,
        Shift * 4
    };
    
    half Roughness2 = Pow2(ClampedRoughness);
    half B[] =
    {
        HairData.Area + Roughness2,
        HairData.Area + Roughness2 / 2,
        HairData.Area + Roughness2 * 2
    };

    float3 Tp;
    float Mp, Np, Fp, a, h, f;
    half ThetaH = SinThetaL + SinThetaV;

    // R
#if HAIR_MARSCHNER_R
    half sa = sin(Alpha[0]);
    half ca = cos(Alpha[0]);
    float ShiftR = 2 * sa * (ca * CosHalfPhi * sqrt(1 - SinThetaV * SinThetaV) + sa * SinThetaV);
    #if 0 // Use Separable R
        float BScale = sqrt(2.0) * CosHalfPhi;
    #else
        float BScale = 1;
    #endif
    Mp = Hair_G(B[0] * BScale, ThetaH - ShiftR);
    Np = 0.25 * CosHalfPhi;
    Fp = Hair_F(sqrt(saturate(0.5 + 0.5 * VoL)));
    specR += Mp * Np * Fp * (HairData.Tint * 4) * lerp(1, HairData.Backlit, saturate(-VoL));
#endif

    // TT
#if HAIR_MARSCHNER_TT
    Mp = Hair_G(B[1], ThetaH - Alpha[1]);
    a = 1 / n_prime;
    h = CosHalfPhi * (1 + a * (0.6 - 0.8 * CosPhi));
    f = Hair_F( CosThetaD * sqrt(saturate(1 - h * h)));
    Fp = Pow2(1 - f);
    
    Tp = PositivePow(abs(brdfData.albedo), 0.5 * sqrt(1 - Pow2(h * a)) / CosThetaD);
    
    Np = exp(-3.65 * CosPhi - 3.98);
    specR += Mp * Np * Fp * Tp * HairData.Backlit * 0.2f;
#endif

    // TRT
#if HAIR_MARSCHNER_TRT
    Mp = Hair_G(B[2], ThetaH - Alpha[2]);
    f = Hair_F( CosThetaD * 0.5f);
    Fp = Pow2(1 - f) * f;
    Tp = pow(abs(brdfData.albedo), 0.8 / CosThetaD);
    Np = exp(17 * CosPhi - 16.78);
    specR += Mp * Np * Fp * Tp;
#endif
    
#if REAL_IS_HALF
    specR = specR - HALF_MIN;
    specR = clamp(specR, 0.0, 1000.0); // Prevent FP16 overflow on mobiles
#endif
    
    return half3(specR);
}

half3 HairMarschner(BRDFData brdfData, half3 L, half3 V, float3 N, HairData HairData)
{
    half3 specR = HairMarschnerNoPI(brdfData, L, V, N, HairData);
    specR *= INV_PI;
    
#if REAL_IS_HALF
    specR = specR - HALF_MIN;
    specR = clamp(specR, 0.0, 1000.0); // Prevent FP16 overflow on mobiles
#endif
    
    return specR;
}

half3 HairLighting(BRDFData brdfData, half3 lightColor, half3 lightDirectionWS, float lightAttenuation,
                            float3 normalWS, half3 viewDirectionWS, float shadow,
                            HairData HairData, BRDFOcclusionFactor aoFactor)
{
    half NdotL = saturate(dot(normalWS, lightDirectionWS));
    half3 radiance = lightColor * aoFactor.directSpecularOcclusion * lightAttenuation;
    
    half3 directSpecularR = 0;
    half3 directDiffuseR = 0;
    half3 clearCoatSpecularR = LitSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS) * NdotL;

#if _MARSCHNER_HAIR
    directSpecularR = HairMarschner(brdfData, lightDirectionWS, viewDirectionWS, normalWS, HairData);
#else
    directSpecularR = HairKajiyaKay(brdfData, lightDirectionWS, viewDirectionWS, normalWS, HairData);
#endif
    
    directSpecularR = directSpecularR * (1 - HairData.Wet) + clearCoatSpecularR * HairData.Wet;
    
#if HAIR_MULTI_SCATTERING
    directDiffuseR += max(0.0, DIFFUSE_ATTENUATION(brdfData.albedo, lightDirectionWS, viewDirectionWS, normalWS, HairData));
#else
    // Double-sided Lambert.
    directDiffuseR += saturate(ComputeDiffuseColor(brdfData.albedo, HairData.Metallic)) * NdotL;
#endif
    
    half3 directSpecularT = HairVolumetricBacklitScatter(brdfData.albedo, lightDirectionWS, viewDirectionWS, HairData.GeomNormal, HairData);
    [branch]
    if (!ShouldEvaluateThickObjectTransmission(lightDirectionWS, HairData.GeomNormal))
    {
        directSpecularT *= shadow;
    }
    
    half3 brdf = (directSpecularR * shadow + directSpecularT) * radiance + directDiffuseR * radiance * shadow;
    brdf = -min(-brdf, 0);
    return brdf;
}


half3 HairLighting(BRDFData brdfData, Light light, float3 normalWS,
                            half3 viewDirectionWS, HairData HairData, BRDFOcclusionFactor aoFactor)
{
    return HairLighting(brdfData, light.color, light.direction,
                        light.distanceAttenuation, normalWS, viewDirectionWS, light.shadowAttenuation, HairData, aoFactor);
}

half3 HairGlobalIllumination(BRDFData brdfData, half3 bakedGI, BRDFOcclusionFactor aoFactor, float3 positionWS,
    half3 normalWS, half3 viewDirectionWS, float2 normalizedScreenSpaceUV, HairData hairData, uint renderingLayers)
{
    half3 indirectLighting = 0;
    float3 N = normalWS;
    
    half NoV = saturate(dot(N, viewDirectionWS));
    // Secondary lobe as it is often more rough (it is the colored one).
    half roughness = lerp(brdfData.perceptualRoughness, hairData.Roughness, 0.5f);
    
    half3 iblR = reflect(-viewDirectionWS, N);
    // ============================ Diffuse Part ================================== //
    half3 indirectDiffuse = EvaluateIndirectDiffuse(positionWS, normalWS, normalizedScreenSpaceUV, bakedGI);
    half normalizationFactor = SampleProbeVolumeReflectionNormalize(positionWS, normalWS, normalizedScreenSpaceUV, bakedGI, iblR);
    // ============================ Diffuse Part ================================== //
    
    half3 indirectSpecular = GlossyEnvironmentReflection(iblR, positionWS,
        roughness, 1.0h, normalizedScreenSpaceUV) * hairData.Tint * normalizationFactor;
    
#if HAIR_INDIRECT_MARSCHNER
    hairData.Backlit = half(0.0); // Skip TT
    hairData.Area = half(0.2);
    float3 L = normalize(viewDirectionWS - N * dot(viewDirectionWS, N));
    indirectSpecular += min(1.f, 2 * HairMarschnerNoPI(brdfData, L, viewDirectionWS, N, hairData));
#endif

#if PRE_INTEGRATED_FGD
    float3 specularFGD;
    float3 diffuseFGD;
    float3 reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(NoV, roughness, brdfData.specular,
        specularFGD, diffuseFGD, reflectivity);
    diffuseFGD = 1;
    indirectDiffuse *= diffuseFGD * brdfData.diffuse;
    indirectSpecular *= specularFGD;
#else
    indirectDiffuse *= brdfData.diffuse;
    // Reference: BRDF.hlsl EnvironmentBRDF
    half fresnelTerm = Pow4(1.0 - NoV);
    indirectSpecular *= EnvironmentBRDFSpecular(brdfData, fresnelTerm);
#endif
    
    indirectLighting = indirectDiffuse * aoFactor.indirectAmbientOcclusion * GetIndirectDiffuseMultiplier(renderingLayers)
                        + indirectSpecular * aoFactor.indirectSpecularOcclusion;
    if (IsOnlyAOLightingFeatureEnabled())
    {
        indirectLighting = aoFactor.indirectAmbientOcclusion + aoFactor.indirectSpecularOcclusion;
    }
    return indirectLighting;
}

///////////////////////////////////////////////////////////////////////////////
//                      Fragment Functions                                   //
//       Used by ShaderGraph and others builtin renderers                    //
///////////////////////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////////////////////////
/// Hair lighting
////////////////////////////////////////////////////////////////////////////////

half4 HairPBR(InputData inputData, SurfaceData surfaceData, HairData HairData)
{
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
    #endif
    
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = IllusionCreateAmbientOcclusionFactor(inputData, surfaceData);
#if EVALUATE_AO_MULTI_BOUNCE
    float NdotV = max(saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 0.00001);
    half3 brdfDiffuse = ComputeDiffuseColor(brdfData.albedo, HairData.Metallic);
    BRDFOcclusionFactor brdfOcclusionFactor = CreateBRDFOcclusionFactorMultiBounce(aoFactor, NdotV, brdfData.perceptualRoughness,
        surfaceData.occlusion, brdfDiffuse, surfaceData.occlusion, brdfData.specular);
#else
    BRDFOcclusionFactor brdfOcclusionFactor = CreateBRDFOcclusionFactor(aoFactor);
#endif
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = IllusionGetMainLight(inputData, shadowMask);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = HairGlobalIllumination(brdfData, inputData.bakedGI, brdfOcclusionFactor,
                                              inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS,
                                              inputData.normalizedScreenSpaceUV, HairData, meshRenderingLayers);
    
    #ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
    #endif
    {
        lightingData.mainLightColor = HairLighting(brdfData, mainLight, inputData.normalWS,
                                                            inputData.viewDirectionWS, HairData, brdfOcclusionFactor);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            lightingData.additionalLightsColor += HairLighting(brdfData, light, inputData.normalWS, 
                                                                        inputData.viewDirectionWS, HairData, brdfOcclusionFactor);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = IllusionGetAdditionalLight(lightIndex, inputData, shadowMask);

        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        #endif
        {
            lightingData.additionalLightsColor += HairLighting(
                brdfData, light, inputData.normalWS, inputData.viewDirectionWS, HairData, brdfOcclusionFactor);
        }
    LIGHT_LOOP_END
    #endif

    #if REAL_IS_HALF
        // Clamp any half.inf+ to HALF_MAX
        return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
    #else
        return CalculateFinalColor(lightingData, surfaceData.alpha);
    #endif
}

inline float Dither8x8Bayer(int x, int y)
{
    const float dither[ 64 ] = {
        1, 49, 13, 61,  4, 52, 16, 64,
       33, 17, 45, 29, 36, 20, 48, 32,
        9, 57,  5, 53, 12, 60,  8, 56,
       41, 25, 37, 21, 44, 28, 40, 24,
        3, 51, 15, 63,  2, 50, 14, 62,
       35, 19, 47, 31, 34, 18, 46, 30,
       11, 59,  7, 55, 10, 58,  6, 54,
       43, 27, 39, 23, 42, 26, 38, 22};
    int r = y * 8 + x;
    return dither[r] / 64;
}

inline void ClipHair(in float4 screenPos, in float alpha, in float threshold)
{
#ifndef _HAIR_ORDER_INDEPENDENT
    float4 screenPosNorm = screenPos / screenPos.w;
    float2 screenUV = screenPosNorm.xy * _ScreenParams.xy;
    float dither = Dither8x8Bayer(fmod(screenUV.x, 8), fmod(screenUV.y, 8));
    threshold = lerp(threshold, dither, 0.5);
#endif
    clip(alpha - threshold);
}
#endif
