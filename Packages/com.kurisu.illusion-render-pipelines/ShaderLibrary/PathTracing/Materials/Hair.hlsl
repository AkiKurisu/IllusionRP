#ifndef ILLUSION_HDRP_HAIR_INPUT_INCLUDED
#define ILLUSION_HDRP_HAIR_INPUT_INCLUDED
#define DEFAULT_HAIR_SPECULAR_VALUE 0.0465
#define _ABSORPTION_FROM_COLOR 1

float GetAbsorptionDenominator(float azimuthalRoughness)
{
    const float beta = azimuthalRoughness;

#if 0
    float beta2 = beta  * beta;
    float beta3 = beta2 * beta;
    float beta4 = beta3 * beta;
    float beta5 = beta4 * beta;

    // Least squares fit of an inverse mapping between scattering parameters and scattering albedo.
    return 5.969 - (0.215 * beta) + (2.532 * beta2) - (10.73 * beta3) + (5.574 * beta4) + (0.245 * beta5);
#else
    // Simplified version of the above.
    return (((((0.245f * beta) + 5.574f) * beta - 10.73f) * beta + 2.532f) * beta - 0.215f) * beta + 5.969f;
#endif
}

float3 AbsorptionFromReflectance(float3 diffuseColor, float azimuthalRoughness)
{
    // Enforce a minimum value to prevent NaNs.
    diffuseColor = max(diffuseColor, 1e-3);

    return Sq(log(diffuseColor) / GetAbsorptionDenominator(azimuthalRoughness));
}

float RoughnessToBlinnPhongSpecularExponent(float roughness)
{
    return clamp(2 * rcp(max(roughness * roughness, FLT_EPS)) - 2, FLT_EPS, rcp(FLT_EPS));
}

BSDFData ConvertSurfaceDataToBSDFData(uint2 positionSS, SurfaceData surfaceData)
{
    BSDFData bsdfData;
    ZERO_INITIALIZE(BSDFData, bsdfData);

    // IMPORTANT: All enable flags are statically know at compile time, so the compiler can do compile time optimization
    bsdfData.materialFeatures = surfaceData.materialFeatures;

    bsdfData.ambientOcclusion = surfaceData.ambientOcclusion;
    bsdfData.specularOcclusion = surfaceData.specularOcclusion;

    bsdfData.diffuseColor = surfaceData.diffuseColor;

    bsdfData.normalWS = surfaceData.normalWS;
    bsdfData.geomNormalWS = surfaceData.geomNormalWS;

    // Enforce a maximum smoothness to prevent NaNs.
    bsdfData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(min(1.0 - 1e-2, surfaceData.perceptualSmoothness));

    // This value will be override by the value in diffusion profile
    bsdfData.fresnel0                 = DEFAULT_HAIR_SPECULAR_VALUE;
    bsdfData.transmittance            = surfaceData.transmittance;
    bsdfData.rimTransmissionIntensity = surfaceData.rimTransmissionIntensity;

    // This is the hair tangent (which represents the hair strand direction, root to tip).
    bsdfData.hairStrandDirectionWS = surfaceData.hairStrandDirectionWS;

    // Kajiya kay
    if (HasFlag(surfaceData.materialFeatures, MATERIALFEATUREFLAGS_HAIR_KAJIYA_KAY))
    {
        bsdfData.secondaryPerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.secondaryPerceptualSmoothness);
        bsdfData.specularTint = surfaceData.specularTint;
        bsdfData.secondarySpecularTint = surfaceData.secondarySpecularTint;
        bsdfData.specularShift = surfaceData.specularShift;
        bsdfData.secondarySpecularShift = surfaceData.secondarySpecularShift;

        float roughness1 = PerceptualRoughnessToRoughness(bsdfData.perceptualRoughness);
        float roughness2 = PerceptualRoughnessToRoughness(bsdfData.secondaryPerceptualRoughness);

        bsdfData.specularExponent          = RoughnessToBlinnPhongSpecularExponent(roughness1);
        bsdfData.secondarySpecularExponent = RoughnessToBlinnPhongSpecularExponent(roughness2);

        bsdfData.anisotropy = 0.8; // For hair we fix the anisotropy
    }

    // Marschner
    if (HasFlag(surfaceData.materialFeatures, MATERIALFEATUREFLAGS_HAIR_MARSCHNER) ||
        HasFlag(surfaceData.materialFeatures, MATERIALFEATUREFLAGS_HAIR_MARSCHNER_CINEMATIC))
    {
        // Cuticle Angle
        const float cuticleAngle = radians(surfaceData.cuticleAngle);
        bsdfData.cuticleAngle    = -cuticleAngle;
        bsdfData.cuticleAngleR   = -cuticleAngle;
        bsdfData.cuticleAngleTT  =  cuticleAngle * 0.5;
        bsdfData.cuticleAngleTRT =  cuticleAngle * 1.5;

        // Longitudinal Roughness
        const float roughnessL = bsdfData.perceptualRoughness;
        bsdfData.roughnessR    = PerceptualRoughnessToRoughness(roughnessL);
        bsdfData.roughnessTT   = PerceptualRoughnessToRoughness(roughnessL * 0.5);
        bsdfData.roughnessTRT  = PerceptualRoughnessToRoughness(roughnessL * 2.0);

        // Azimuthal Roughness
        bsdfData.perceptualRoughnessRadial = PerceptualSmoothnessToPerceptualRoughness(min(1.0 - 1e-2, surfaceData.perceptualRadialSmoothness));

        // Absorption. Note: We require diffuse color to parameterize LUTs and for approximation purposes.
    #if _ABSORPTION_FROM_COLOR
        bsdfData.absorption   = AbsorptionFromReflectance(surfaceData.diffuseColor, bsdfData.perceptualRoughnessRadial);
    #elif _ABSORPTION_FROM_MELANIN
        bsdfData.absorption   = AbsorptionFromMelanin(surfaceData.eumelanin, surfaceData.pheomelanin);
        bsdfData.diffuseColor = ReflectanceFromMelanin(surfaceData.eumelanin, surfaceData.pheomelanin, bsdfData.perceptualRoughnessRadial);
    #else
        bsdfData.absorption   = surfaceData.absorption;
        bsdfData.diffuseColor = ReflectanceFromAbsorption(bsdfData.absorption, bsdfData.perceptualRoughnessRadial);
    #endif

#if _MATERIAL_FEATURE_HAIR_MARSCHNER_CINEMATIC
        bsdfData.strandCountProbe = surfaceData.strandCountProbe;

    #if !_USE_SPLINE_VISIBILITY_FOR_MULTIPLE_SCATTERING
        // The user has specified that they would like to derive self-shadowing data only from the volumetric grid.
        bsdfData.visibility = -1;
    #endif
#endif

        // By default the normalization factor should be 1 and overridden by area lights.
        bsdfData.distributionNormalizationFactor = 1;

        // Only necesarry for reference.
        // bsdfData.h = -1 + 2 * InterleavedGradientNoise(positionSS, _TaaFrameInfo.z);
    }



    return bsdfData;
}
#undef _ABSORPTION_FROM_COLOR
#endif
