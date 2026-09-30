#ifndef ILLUSION_HDRP_LIT_INPUT_INCLUDED
#define ILLUSION_HDRP_LIT_INPUT_INCLUDED
#define REFRACTION_THIN_DISTANCE 0.005

void FillMaterialClearCoatData(float coatMask, inout BSDFData bsdfData)
{
    bsdfData.coatMask = coatMask;
    float ieta = lerp(1.0, CLEAR_COAT_IETA, bsdfData.coatMask);
    bsdfData.coatRoughness = CLEAR_COAT_ROUGHNESS;

    // Approx to deal with roughness appearance of base layer (should appear rougher)
    float coatRoughnessScale = Sq(ieta);
    float sigma = RoughnessToVariance(PerceptualRoughnessToRoughness(bsdfData.perceptualRoughness));
    bsdfData.perceptualRoughness = RoughnessToPerceptualRoughness(VarianceToRoughness(sigma * coatRoughnessScale));
}

void FillMaterialTransparencyData(float3 baseColor, float metallic, float ior, float3 transmittanceColor, float atDistance, float thickness, float transmittanceMask, inout BSDFData bsdfData)
{
    // Uses thickness from SSS's property set
    bsdfData.ior = ior;

    // IOR define the fresnel0 value, so update it also for consistency (and even if not physical we still need to take into account any metal mask)
    bsdfData.fresnel0 = lerp(IorToFresnel0(ior).xxx, baseColor, metallic);

    bsdfData.absorptionCoefficient = TransmittanceColorAtDistanceToAbsorption(transmittanceColor, atDistance);
    bsdfData.transmittanceMask = transmittanceMask;
    bsdfData.thickness = max(thickness, 0.0001);
}
#endif
