
// Fabric Material Data:
//
// @IllusionRP: both modes use the lobes of UE's Cloth model, a GGX lobe blended into a cloth lobe by sheenAmount.
// Cotton/Wool mode has a Lambert diffuse and an isotropic GGX lobe, Silk mode a Burley diffuse and an anisotropic one.
// bsdfWeight0  Diffuse BRDF
// bsdfWeight1  Spec GGX BRDF
// bsdfWeight2  Diffuse BTDF
// bsdfWeight3  Cloth BRDF

void ProcessBSDFData(PathPayload payload, BuiltinData builtinData, inout BSDFData bsdfData)
{
    // Adjust roughness to reduce fireflies
    bsdfData.roughnessT = max(payload.maxRoughness, bsdfData.roughnessT);
    bsdfData.roughnessB = max(payload.maxRoughness, bsdfData.roughnessB);

    if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_COTTON_WOOL))
    {
        // This is hacky, but applied to match the raster implementation (Fabric.hlsl)
        bsdfData.diffuseColor *= FabricLambertNoPI(bsdfData.roughnessT);
    }
}

bool CreateMaterialData(PathPayload payload, BuiltinData builtinData, BSDFData bsdfData, inout float3 shadingPosition, inout float theSample, out MaterialData mtlData)
{
    // Alter values in the material's bsdfData struct, to better suit path tracing
    mtlData.bsdfData = bsdfData;
    ProcessBSDFData(payload, builtinData, mtlData.bsdfData);

    mtlData.bsdfWeight = 0.0;
    mtlData.V = -WorldRayDirection();
    mtlData.Nv = ComputeConsistentShadingNormal(mtlData.V, bsdfData.geomNormalWS, bsdfData.normalWS);

    if (!IsAbove(mtlData))
        return false;

    mtlData.bsdfWeight[0] = Luminance(mtlData.bsdfData.diffuseColor) * max(mtlData.bsdfData.ambientOcclusion, 0.001);

    // If N.V < 0 (can happen with normal mapping, or smooth normals on coarsely tesselated objects) we want to avoid spec sampling
    float NdotV = dot(GetSpecularNormal(mtlData), mtlData.V);
    if (NdotV > 0.001)
    {
        mtlData.bsdfWeight[1] = (1.0 - mtlData.bsdfData.sheenAmount) * Luminance(F_Schlick(mtlData.bsdfData.fresnel0, NdotV));
        mtlData.bsdfWeight[3] = mtlData.bsdfData.sheenAmount * Luminance(mtlData.bsdfData.sheenColor);
    }

    bool hasTransmission = HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_TRANSMISSION);
    if (hasTransmission)
        mtlData.bsdfWeight[2] = mtlData.bsdfWeight[0] * Luminance(mtlData.bsdfData.transmittance);

    // Normalize the weights
    float wSum = mtlData.bsdfWeight[0] + mtlData.bsdfWeight[1] + mtlData.bsdfWeight[2] + mtlData.bsdfWeight[3];

    if (wSum < BSDF_WEIGHT_EPSILON)
        return false;

    mtlData.bsdfWeight /= wSum;

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
    if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
    {
        float subsurfaceWeight = mtlData.bsdfWeight[0] * mtlData.bsdfData.subsurfaceMask * (1.0 - payload.maxRoughness);

        mtlData.isSubsurface = theSample < subsurfaceWeight;
        if (mtlData.isSubsurface)
        {
            // We do a full, ray-traced subsurface scattering computation here:
            // Let's try and change shading position and normal, and replace the diffuse color by the subsurface throughput
            mtlData.subsurfaceWeightFactor = subsurfaceWeight;

            SSS::Result subsurfaceResult;
            float3 meanFreePath = 0.001 / (_ShapeParamsAndMaxScatterDists[mtlData.bsdfData.diffusionProfileIndex].rgb * _WorldScalesAndFilterRadiiAndThicknessRemaps[mtlData.bsdfData.diffusionProfileIndex].x);

            if (!SSS::RandomWalk(shadingPosition, GetDiffuseNormal(mtlData), mtlData.bsdfData.diffuseColor, meanFreePath, payload.pixelCoord, subsurfaceResult, hasTransmission))
                return false;

            shadingPosition = subsurfaceResult.exitPosition;
            mtlData.bsdfData.normalWS = subsurfaceResult.exitNormal;
            mtlData.bsdfData.geomNormalWS = subsurfaceResult.exitNormal;
            mtlData.bsdfData.diffuseColor = subsurfaceResult.throughput;
        }
        else
        {
            // Otherwise, we just compute BSDFs as usual
            mtlData.subsurfaceWeightFactor = 1.0 - subsurfaceWeight;

            mtlData.bsdfWeight[0] = max(mtlData.bsdfWeight[0] - subsurfaceWeight, BSDF_WEIGHT_EPSILON);
            mtlData.bsdfWeight /= mtlData.subsurfaceWeightFactor;

            theSample -= subsurfaceWeight;
        }

        // Rescale the sample we used for the SSS selection test
        theSample /= mtlData.subsurfaceWeightFactor;
    }
#endif

    return true;
}

bool IsCottonWool(MaterialData mtlData)
{
    return HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_COTTON_WOOL);
}

// @IllusionRP: the reflection lobes are evaluated together, so every sampled direction gets the mixture PDF.
void EvaluateReflection(MaterialData mtlData, float3 sampleDir, inout MaterialResult result)
{
    // Reflection lobes cannot contribute to the opposite hemisphere selected by SampleMaterial.
    if (!IsAbove(GetDiffuseNormal(mtlData), sampleDir))
        return;

    if (mtlData.bsdfWeight[0] > BSDF_WEIGHT_EPSILON)
    {
        if (IsCottonWool(mtlData))
            BRDF::EvaluateLambert(mtlData, GetDiffuseNormal(mtlData), sampleDir, result.diffValue, result.diffPdf);
        else
            BRDF::EvaluateBurley(mtlData, GetDiffuseNormal(mtlData), sampleDir, result.diffValue, result.diffPdf);
        result.diffValue *= mtlData.bsdfData.ambientOcclusion; // Take into account AO the same way as in SampleMaterial
        result.diffPdf *= mtlData.bsdfWeight[0];
    }

    float3 value;
    float pdf;
    if (mtlData.bsdfWeight[1] > BSDF_WEIGHT_EPSILON)
    {
        BRDF::EvaluateAnisoGGX(mtlData, GetSpecularNormal(mtlData), mtlData.bsdfData.roughnessT, mtlData.bsdfData.roughnessB, mtlData.bsdfData.fresnel0, sampleDir, value, pdf);
        result.specValue += value * (1.0 - mtlData.bsdfData.sheenAmount);
        result.specPdf += pdf * mtlData.bsdfWeight[1];
    }

    if (mtlData.bsdfWeight[3] > BSDF_WEIGHT_EPSILON)
    {
        BRDF::EvaluateSheen(mtlData, GetSpecularNormal(mtlData), mtlData.bsdfData.roughnessT, mtlData.bsdfData.sheenColor * mtlData.bsdfData.sheenAmount,
            mtlData.bsdfData.velvet, sampleDir, value, pdf);
        result.specValue += value;
        result.specPdf += pdf * mtlData.bsdfWeight[3];
    }
}

bool SampleMaterial(MaterialData mtlData, float3 inputSample, out float3 sampleDir, out MaterialResult result)
{
    Init(result);

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
    if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
    {
        if (mtlData.isSubsurface)
        {
            if (!BRDF::SampleLambert(mtlData, GetDiffuseNormal(mtlData), inputSample, sampleDir, result.diffValue, result.diffPdf))
                return false;

            result.diffValue *= mtlData.bsdfData.ambientOcclusion;

            return true;
        }
    }
#endif

    if (!IsAbove(mtlData))
        return false;

    float3 value;
    float pdf;
    if (inputSample.z < mtlData.bsdfWeight[0]) // Diffuse BRDF
    {
        bool sampled = IsCottonWool(mtlData)
            ? BRDF::SampleLambert(mtlData, GetDiffuseNormal(mtlData), inputSample, sampleDir, value, pdf)
            : BRDF::SampleBurley(mtlData, GetDiffuseNormal(mtlData), inputSample, sampleDir, value, pdf);
        if (!sampled)
            return false;
    }
    else if (inputSample.z < mtlData.bsdfWeight[0] + mtlData.bsdfWeight[1]) // Spec GGX BRDF
    {
        if (!BRDF::SampleAnisoGGX(mtlData, GetSpecularNormal(mtlData), mtlData.bsdfData.roughnessT, mtlData.bsdfData.roughnessB, mtlData.bsdfData.fresnel0, inputSample, sampleDir, value, pdf))
            return false;
    }
    else if (inputSample.z < mtlData.bsdfWeight[0] + mtlData.bsdfWeight[1] + mtlData.bsdfWeight[3]) // Cloth BRDF, cosine-weighted
    {
        if (!BRDF::SampleLambert(mtlData, GetSpecularNormal(mtlData), inputSample, sampleDir, value, pdf))
            return false;
    }
    else // Diffuse BTDF
    {
        if (!BTDF::SampleLambert(mtlData, GetDiffuseNormal(mtlData), inputSample, sampleDir, result.diffValue, result.diffPdf))
            return false;

        result.diffValue *= mtlData.bsdfData.transmittance * mtlData.bsdfData.ambientOcclusion;
        result.diffPdf *= mtlData.bsdfWeight[2];

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
        if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
        {
            // We compensate for the fact that there is no transmission when computing SSS
            result.diffValue /= mtlData.subsurfaceWeightFactor;
        }
#endif
        return result.diffPdf > 0.0;
    }

    EvaluateReflection(mtlData, sampleDir, result);

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
    if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
    {
        // We compensate for the fact that there is no spec when computing SSS
        result.specValue /= mtlData.subsurfaceWeightFactor;
    }
#endif

    return result.diffPdf + result.specPdf > 0.0;
}

void EvaluateMaterial(MaterialData mtlData, float3 sampleDir, out MaterialResult result)
{
    Init(result);

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
    if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
    {
        if (mtlData.isSubsurface)
        {
            BRDF::EvaluateLambert(mtlData, GetDiffuseNormal(mtlData), sampleDir, result.diffValue, result.diffPdf);
            result.diffValue *= mtlData.bsdfData.ambientOcclusion; // Take into account AO the same way as in SampleMaterial
            return;
        }
    }
#endif

    if (IsAbove(mtlData))
    {
        EvaluateReflection(mtlData, sampleDir, result);

        if (IsBelow(GetDiffuseNormal(mtlData), sampleDir) && mtlData.bsdfWeight[2] > BSDF_WEIGHT_EPSILON)
        {
            BTDF::EvaluateLambert(mtlData, GetDiffuseNormal(mtlData), sampleDir, result.diffValue, result.diffPdf);
            result.diffValue *= mtlData.bsdfData.transmittance;
            result.diffValue *= mtlData.bsdfData.ambientOcclusion; // Take into account AO the same way as in SampleMaterial
            result.diffPdf *= mtlData.bsdfWeight[2];

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
            if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
            {
                // We compensate for the fact that there is no transmission when computing SSS
                result.diffValue /= mtlData.subsurfaceWeightFactor;
            }
#endif
        }

#if defined(ILLUSION_PATH_TRACING_FABRIC_SSS)
        if (HasFlag(mtlData.bsdfData.materialFeatures, MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING))
        {
            // We compensate for the fact that there is no spec when computing SSS
            result.specValue /= mtlData.subsurfaceWeightFactor;
        }
#endif
    }
}

float3 GetLightNormal(MaterialData mtlData)
{
    // If both diffuse and specular normals are quasi-indentical, return one of them, otherwise return a null vector
    return dot(GetDiffuseNormal(mtlData), GetSpecularNormal(mtlData)) > 0.99 ? GetDiffuseNormal(mtlData) : float3(0.0, 0.0, 0.0);
}

float AdjustPathRoughness(MaterialData mtlData, MaterialResult mtlResult, bool isSampleBelow, float pathRoughness)
{
    // Adjust the max roughness, based on the estimated diff/spec ratio
    return (mtlResult.specPdf * max(mtlData.bsdfData.roughnessT, mtlData.bsdfData.roughnessB) + mtlResult.diffPdf) / (mtlResult.diffPdf + mtlResult.specPdf);
}

float3 GetMaterialAbsorption(MaterialData mtlData, SurfaceData surfaceData, float dist, bool isSampleBelow)
{
    // No absorption here
    return 1.0;
}

void GetAOVData(BSDFData bsdfData, out AOVData aovData)
{
    aovData.albedo = bsdfData.diffuseColor;
    aovData.normal = bsdfData.normalWS;
}
