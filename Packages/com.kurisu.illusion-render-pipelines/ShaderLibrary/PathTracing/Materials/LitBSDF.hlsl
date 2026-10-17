#ifndef ILLUSION_HDRP_LIT_BSDF_INCLUDED
#define ILLUSION_HDRP_LIT_BSDF_INCLUDED

#include "../PathTracingPayload.hlsl"
#define ILLUSION_HDRP_ENABLE_SSS
#include "HairMaterial.hlsl"
#undef ILLUSION_HDRP_ENABLE_SSS

// As in RTXPT, specular lobes smoother than this GGX alpha count as delta.
static const float kIllusionMinGGXAlpha = 0.0064;

#ifdef _PATH_TRACED_DUAL_SCATTERING
#error HDRPBSDF storage does not carry MaterialData.positionWS
#endif

struct HDRPBSDF
{
    static const uint cRandomNumberCountForSampling = 3;
    uint storage[41];
    uint family;
    float3 skinSample;
    float3 diffuseAlbedo;
    bool thin;
    bool valid;

    // @IllusionRP: with PT_NEE_ONLY, as on the RTXDI sample's secondary surfaces, light sampling alone lights every surface,
    // which scatters no ray.
    bool lightSampledLobesOnly()
    {
#if defined(PT_NEE_ONLY)
        return true;
#else
        return false;
#endif
    }

    float getRoughness()
    {
        if (family == PT_FAMILY_HAIR) return getHair(0.0).bsdfData.perceptualRoughness;
        if (family == PT_FAMILY_FABRIC) return getFabric(0.0).bsdfData.perceptualRoughness;
        return getLit(0.0).bsdfData.perceptualRoughness;
    }

    float4 eval(const ShadingData shadingData, const float3 wo)
    {
        if (family == PT_FAMILY_UNLIT) return 0.0.xxxx;
        if (family == PT_FAMILY_HAIR)
        {
            Illusion::Hair::MaterialResult value;
            Illusion::Hair::EvaluateMaterial(getHair(shadingData.V), wo, value);
            return float4(value.specValue, Average(value.specValue));
        }
        if (family == PT_FAMILY_FABRIC)
        {
            Illusion::Fabric::MaterialResult value;
            Illusion::Fabric::EvaluateMaterial(getFabric(shadingData.V), wo, value);
            return float4(value.diffValue + value.specValue, Average(value.specValue));
        }
        const Illusion::Lit::MaterialData material = getLit(shadingData.V);
        if (Illusion::Lit::IsBelow(material, wo)) return 0.0.xxxx;
        Illusion::Lit::MaterialResult result;
        Illusion::Lit::EvaluateMaterial(material, wo, result);
        return float4(result.diffValue + result.specValue, Average(result.specValue));
    }

    float evalPdf(const ShadingData shadingData, const float3 wo, bool useImportanceSampling)
    {
        if (family == PT_FAMILY_UNLIT) return 0.0;
        if (family == PT_FAMILY_HAIR)
        {
            Illusion::Hair::MaterialResult value;
            Illusion::Hair::EvaluateMaterial(getHair(shadingData.V), wo, value);
            return value.specPdf;
        }
        if (family == PT_FAMILY_FABRIC)
        {
            Illusion::Fabric::MaterialResult value;
            Illusion::Fabric::EvaluateMaterial(getFabric(shadingData.V), wo, value);
            return value.diffPdf + value.specPdf;
        }
        const Illusion::Lit::MaterialData material = getLit(shadingData.V);
        if (Illusion::Lit::IsBelow(material, wo)) return 0.0;
        Illusion::Lit::MaterialResult result;
        Illusion::Lit::EvaluateMaterial(material, wo, result);
        return result.diffPdf + result.specPdf;
    }

    bool sample(const ShadingData shadingData, const float4 inputSample, out BSDFSample result, bool useImportanceSampling)
    {
        result = (BSDFSample)0;
        if (!valid || lightSampledLobesOnly()) return false;
        if (family == PT_FAMILY_HAIR)
        {
            const Illusion::Hair::MaterialData hair = getHair(shadingData.V);
            Illusion::Hair::MaterialResult value;
            if (!Illusion::Hair::SampleMaterial(hair, inputSample.xyz, result.wo, value)) return false;
            result.pdf = value.specPdf;
            if (!(result.pdf > 0.0)) return false;
            result.weight = value.specValue / result.pdf;
            result.lobe = (uint)(Illusion::Hair::IsAbove(hair, result.wo) ? LobeType::SpecularReflection : LobeType::SpecularTransmission);
            result.lobeP = 1.0;
            return all(isfinite(result.weight));
        }
        if (family == PT_FAMILY_FABRIC)
        {
            const Illusion::Fabric::MaterialData fabric = getFabric(shadingData.V);
            Illusion::Fabric::MaterialResult value;
            if (!Illusion::Fabric::SampleMaterial(fabric, inputSample.xyz, result.wo, value)) return false;
            result.pdf = value.diffPdf + value.specPdf;
            if (!(result.pdf > 0.0)) return false;
            result.weight = (value.diffValue + value.specValue) / result.pdf;
            bool sampledSpecular = inputSample.z >= fabric.bsdfWeight[0] && inputSample.z < fabric.bsdfWeight[0] + fabric.bsdfWeight[1];
            result.lobe = !Illusion::Fabric::IsAbove(fabric, result.wo) ? (uint)LobeType::DiffuseTransmission
                : (uint)(sampledSpecular ? LobeType::SpecularReflection : LobeType::DiffuseReflection);
            result.lobeP = 1.0;
            return all(isfinite(result.weight));
        }
        const Illusion::Lit::MaterialData material = getLit(shadingData.V);
        Illusion::Lit::MaterialResult value;
        float3 materialSample = family == PT_FAMILY_SKIN ? skinSample : inputSample.xyz;
        if (!Illusion::Lit::SampleMaterial(material, materialSample, result.wo, value, thin)) return false;
        result.pdf = value.diffPdf + value.specPdf;
        if (!(result.pdf > 0.0)) return false;
        result.weight = (value.diffValue + value.specValue) / result.pdf;
        bool transmission = !material.isSubsurface && Illusion::Lit::IsAbove(material) != Illusion::Lit::IsAbove(material, result.wo);
        if (thin && transmission)
            result.weight *= Illusion::Lit::GetMaterialAbsorption(material, (Illusion::Lit::SurfaceData)0, 0.0, Illusion::Lit::IsBelow(material, result.wo), true);
        bool below = !material.isSubsurface && Illusion::Lit::IsBelow(material);
        bool diffuse = material.isSubsurface || (!below && materialSample.z < material.bsdfWeight[0]);
        bool coat = !diffuse && !below && materialSample.z < material.bsdfWeight[0] + material.bsdfWeight[1];
        float specularRoughness = max(max(material.bsdfData.roughnessT, material.bsdfData.roughnessB), material.bsdfData.lobeMix > 0.0 ? material.bsdfData.secondaryRoughness : 0.0);
        bool delta = below || (!diffuse && !coat && specularRoughness < kIllusionMinGGXAlpha);
        result.lobe = transmission ? (uint)(delta ? LobeType::DeltaTransmission : (diffuse ? LobeType::DiffuseTransmission : LobeType::SpecularTransmission))
                                   : (uint)(delta ? LobeType::DeltaReflection : (diffuse ? LobeType::DiffuseReflection : LobeType::SpecularReflection));
        result.lobeP = !delta ? 1.0 : below ? value.specPdf / DELTA_PDF : material.bsdfWeight[transmission ? 3 : 2];
        return all(isfinite(result.weight));
    }

    uint getLobes(const ShadingData shadingData)
    {
        if (family == PT_FAMILY_UNLIT) return 0u;
        if (family == PT_FAMILY_HAIR) return (uint)LobeType::SpecularReflection | (uint)LobeType::SpecularTransmission;
        if (family == PT_FAMILY_FABRIC)
            return (uint)LobeType::DiffuseReflection | (uint)LobeType::SpecularReflection | (getFabric(shadingData.V).bsdfWeight[2] > 0.0 ? (uint)LobeType::DiffuseTransmission : 0u);
        const Illusion::Lit::MaterialData material = getLit(shadingData.V);
        if (material.isSubsurface) return (uint)LobeType::DiffuseReflection;
        return (uint)LobeType::DiffuseReflection | (uint)LobeType::SpecularReflection
            | (material.bsdfData.transmittanceMask > 0.0 ? (uint)LobeType::SpecularTransmission : 0u);
    }

    void estimateSpecDiffBSDF(out float3 diffuse, out float3 specular, const float3 normal, const float3 view)
    {
        if (family == PT_FAMILY_UNLIT)
        {
            diffuse = 0.0;
            specular = 0.0;
            return;
        }
        if (family == PT_FAMILY_FABRIC)
        {
            const Illusion::Fabric::MaterialData fabric = getFabric(view);
            diffuse = fabric.bsdfData.diffuseColor;
            specular = lerp(fabric.bsdfData.fresnel0, fabric.bsdfData.sheenColor, fabric.bsdfData.sheenAmount);
            return;
        }
        if (family == PT_FAMILY_HAIR)
        {
            diffuse = 0.0;
            specular = getHair(view).bsdfData.diffuseColor;
            return;
        }
        const Illusion::Lit::MaterialData material = getLit(view);
        diffuse = diffuseAlbedo * (1.0 - material.bsdfData.transmittanceMask);
        float2 uv = Illusion::Remap01ToHalfTexelCoord(float2(sqrt(saturate(dot(normal, view))), material.bsdfData.perceptualRoughness), FGDTEXTURE_RESOLUTION);
        float2 fgd = Illusion::_PreIntegratedFGD_GGXDisneyDiffuse.SampleLevel(Illusion::s_linear_clamp_sampler, uv, 0).xy;
        specular = lerp(fgd.x, fgd.y, material.bsdfData.fresnel0) * Illusion::Lit::GetSpecularCompensation(material);
    }

    void evalDeltaLobes(const ShadingData shadingData, out DeltaLobe lobes[cMaxDeltaLobes], out int count, out float nonDeltaPart)
    {
        count = 0;
        nonDeltaPart = 1.0;
        [unroll] for (uint i = 0; i < cMaxDeltaLobes; ++i) lobes[i] = (DeltaLobe)0;
        if (family == PT_FAMILY_UNLIT)
        {
            nonDeltaPart = 0.0;
            return;
        }
        if (family == PT_FAMILY_FABRIC || family == PT_FAMILY_HAIR) return;
        const Illusion::Lit::MaterialData material = getLit(shadingData.V);
        if (Illusion::Lit::IsAbove(material)) return;
        if (thin && material.bsdfData.transmittanceMask > 0.0)
        {
            lobes[0].dir = -material.V; lobes[0].thp = Illusion::Lit::GetMaterialAbsorption(material, (Illusion::Lit::SurfaceData)0, 0.0, Illusion::Lit::IsBelow(material, -material.V), true); lobes[0].probability = 1.0; lobes[0].transmission = 1;
            count = 1; nonDeltaPart = 0.0; return;
        }
        float3 direction, value; float pdf;
        if (material.bsdfWeight[2] > BSDF_WEIGHT_EPSILON && Illusion::Lit::BRDF::SampleDelta(material, Illusion::Lit::GetSpecularNormal(material), material.bsdfData.ior, direction, value, pdf))
        {
            lobes[count].dir = direction; lobes[count].thp = value / pdf;
            lobes[count].probability = material.bsdfWeight[2]; lobes[count].transmission = 0; count++;
        }
        if (material.bsdfWeight[3] > BSDF_WEIGHT_EPSILON && Illusion::Lit::BTDF::SampleDelta(material, Illusion::Lit::GetSpecularNormal(material), material.bsdfData.ior, direction, value, pdf))
        {
            lobes[count].dir = direction; lobes[count].thp = value / pdf;
            lobes[count].probability = material.bsdfWeight[3]; lobes[count].transmission = 1; count++;
        }
        nonDeltaPart = count == 0 ? 1.0 : 0.0;
    }

    // @IllusionRP: HDRP Lit does not NEE-sample refraction, so it has no competing light-sampling PDF for that event.
    float scatterMISPdf(BSDFSample result)
    {
        return family != PT_FAMILY_FABRIC && family != PT_FAMILY_HAIR && result.isLobe(LobeType::Transmission) ? 0.0 : result.pdf;
    }

    void setOutsideIoR(float relativeIoR)
    {
        if (family == PT_FAMILY_FABRIC || family == PT_FAMILY_HAIR)
            return;
        Illusion::Lit::MaterialData material = getLit(0.0);
        material.bsdfData.ior = relativeIoR;
        setLit(material);
    }

    Illusion::Lit::MaterialData getLit(float3 V)
    {
        Illusion::Lit::MaterialData m = (Illusion::Lit::MaterialData)0;
        m.bsdfData.diffuseColor = asfloat(uint3(storage[0], storage[1], storage[2]));
        m.bsdfData.fresnel0 = asfloat(uint3(storage[3], storage[4], storage[5]));
        m.bsdfData.ambientOcclusion = asfloat(storage[6]);
        m.bsdfData.specularOcclusion = asfloat(storage[7]);
        m.bsdfData.normalWS = asfloat(uint3(storage[8], storage[9], storage[10]));
        m.bsdfData.perceptualRoughness = asfloat(storage[11]);
        m.bsdfData.coatMask = asfloat(storage[12]);
        m.bsdfData.tangentWS = asfloat(uint3(storage[13], storage[14], storage[15]));
        m.bsdfData.bitangentWS = asfloat(uint3(storage[16], storage[17], storage[18]));
        m.bsdfData.roughnessT = asfloat(storage[19]);
        m.bsdfData.roughnessB = asfloat(storage[20]);
        m.bsdfData.coatRoughness = asfloat(storage[21]);
        m.bsdfData.geomNormalWS = asfloat(uint3(storage[22], storage[23], storage[24]));
        m.bsdfData.ior = asfloat(storage[25]);
        m.bsdfData.absorptionCoefficient = asfloat(uint3(storage[26], storage[27], storage[28]));
        m.bsdfData.transmittanceMask = asfloat(storage[29]);
        m.bsdfData.secondaryRoughness = asfloat(storage[30]);
        m.bsdfData.lobeMix = asfloat(storage[31]);
        m.bsdfWeight = asfloat(uint4(storage[32], storage[33], storage[34], storage[35]));
        m.isSubsurface = storage[36] != 0u;
        m.subsurfaceWeightFactor = asfloat(storage[37]);
        m.V = V;
        m.Nv = asfloat(uint3(storage[38], storage[39], storage[40]));
        return m;
    }

    void setLit(Illusion::Lit::MaterialData m)
    {
        storage[0] = asuint(m.bsdfData.diffuseColor.x);
        storage[1] = asuint(m.bsdfData.diffuseColor.y);
        storage[2] = asuint(m.bsdfData.diffuseColor.z);
        storage[3] = asuint(m.bsdfData.fresnel0.x);
        storage[4] = asuint(m.bsdfData.fresnel0.y);
        storage[5] = asuint(m.bsdfData.fresnel0.z);
        storage[6] = asuint(m.bsdfData.ambientOcclusion);
        storage[7] = asuint(m.bsdfData.specularOcclusion);
        storage[8] = asuint(m.bsdfData.normalWS.x);
        storage[9] = asuint(m.bsdfData.normalWS.y);
        storage[10] = asuint(m.bsdfData.normalWS.z);
        storage[11] = asuint(m.bsdfData.perceptualRoughness);
        storage[12] = asuint(m.bsdfData.coatMask);
        storage[13] = asuint(m.bsdfData.tangentWS.x);
        storage[14] = asuint(m.bsdfData.tangentWS.y);
        storage[15] = asuint(m.bsdfData.tangentWS.z);
        storage[16] = asuint(m.bsdfData.bitangentWS.x);
        storage[17] = asuint(m.bsdfData.bitangentWS.y);
        storage[18] = asuint(m.bsdfData.bitangentWS.z);
        storage[19] = asuint(m.bsdfData.roughnessT);
        storage[20] = asuint(m.bsdfData.roughnessB);
        storage[21] = asuint(m.bsdfData.coatRoughness);
        storage[22] = asuint(m.bsdfData.geomNormalWS.x);
        storage[23] = asuint(m.bsdfData.geomNormalWS.y);
        storage[24] = asuint(m.bsdfData.geomNormalWS.z);
        storage[25] = asuint(m.bsdfData.ior);
        storage[26] = asuint(m.bsdfData.absorptionCoefficient.x);
        storage[27] = asuint(m.bsdfData.absorptionCoefficient.y);
        storage[28] = asuint(m.bsdfData.absorptionCoefficient.z);
        storage[29] = asuint(m.bsdfData.transmittanceMask);
        storage[30] = asuint(m.bsdfData.secondaryRoughness);
        storage[31] = asuint(m.bsdfData.lobeMix);
        storage[32] = asuint(m.bsdfWeight.x);
        storage[33] = asuint(m.bsdfWeight.y);
        storage[34] = asuint(m.bsdfWeight.z);
        storage[35] = asuint(m.bsdfWeight.w);
        storage[36] = m.isSubsurface ? 1u : 0u;
        storage[37] = asuint(m.subsurfaceWeightFactor);
        storage[38] = asuint(m.Nv.x);
        storage[39] = asuint(m.Nv.y);
        storage[40] = asuint(m.Nv.z);
    }

    Illusion::Fabric::MaterialData getFabric(float3 V)
    {
        Illusion::Fabric::MaterialData m = (Illusion::Fabric::MaterialData)0;
        m.bsdfData.materialFeatures = storage[0];
        m.bsdfData.diffuseColor = asfloat(uint3(storage[1], storage[2], storage[3]));
        m.bsdfData.fresnel0 = asfloat(uint3(storage[4], storage[5], storage[6]));
        m.bsdfData.ambientOcclusion = asfloat(storage[7]);
        m.bsdfData.normalWS = asfloat(uint3(storage[8], storage[9], storage[10]));
        m.bsdfData.geomNormalWS = asfloat(uint3(storage[11], storage[12], storage[13]));
        m.bsdfData.perceptualRoughness = asfloat(storage[14]);
        m.bsdfData.transmittance = asfloat(uint3(storage[15], storage[16], storage[17]));
        m.bsdfData.tangentWS = asfloat(uint3(storage[18], storage[19], storage[20]));
        m.bsdfData.bitangentWS = asfloat(uint3(storage[21], storage[22], storage[23]));
        m.bsdfData.roughnessT = asfloat(storage[24]);
        m.bsdfData.roughnessB = asfloat(storage[25]);
        m.bsdfData.sheenColor = asfloat(uint3(storage[26], storage[27], storage[28]));
        m.bsdfData.sheenAmount = asfloat(storage[29]);
        m.bsdfData.velvet = storage[30] != 0u;
        m.bsdfWeight = asfloat(uint4(storage[31], storage[32], storage[33], storage[34]));
        m.isSubsurface = storage[35] != 0u;
        m.subsurfaceWeightFactor = asfloat(storage[36]);
        m.V = V;
        m.Nv = asfloat(uint3(storage[37], storage[38], storage[39]));
        return m;
    }

    void setFabric(Illusion::Fabric::MaterialData m)
    {
        storage[0] = m.bsdfData.materialFeatures;
        storage[1] = asuint(m.bsdfData.diffuseColor.x);
        storage[2] = asuint(m.bsdfData.diffuseColor.y);
        storage[3] = asuint(m.bsdfData.diffuseColor.z);
        storage[4] = asuint(m.bsdfData.fresnel0.x);
        storage[5] = asuint(m.bsdfData.fresnel0.y);
        storage[6] = asuint(m.bsdfData.fresnel0.z);
        storage[7] = asuint(m.bsdfData.ambientOcclusion);
        storage[8] = asuint(m.bsdfData.normalWS.x);
        storage[9] = asuint(m.bsdfData.normalWS.y);
        storage[10] = asuint(m.bsdfData.normalWS.z);
        storage[11] = asuint(m.bsdfData.geomNormalWS.x);
        storage[12] = asuint(m.bsdfData.geomNormalWS.y);
        storage[13] = asuint(m.bsdfData.geomNormalWS.z);
        storage[14] = asuint(m.bsdfData.perceptualRoughness);
        storage[15] = asuint(m.bsdfData.transmittance.x);
        storage[16] = asuint(m.bsdfData.transmittance.y);
        storage[17] = asuint(m.bsdfData.transmittance.z);
        storage[18] = asuint(m.bsdfData.tangentWS.x);
        storage[19] = asuint(m.bsdfData.tangentWS.y);
        storage[20] = asuint(m.bsdfData.tangentWS.z);
        storage[21] = asuint(m.bsdfData.bitangentWS.x);
        storage[22] = asuint(m.bsdfData.bitangentWS.y);
        storage[23] = asuint(m.bsdfData.bitangentWS.z);
        storage[24] = asuint(m.bsdfData.roughnessT);
        storage[25] = asuint(m.bsdfData.roughnessB);
        storage[26] = asuint(m.bsdfData.sheenColor.x);
        storage[27] = asuint(m.bsdfData.sheenColor.y);
        storage[28] = asuint(m.bsdfData.sheenColor.z);
        storage[29] = asuint(m.bsdfData.sheenAmount);
        storage[30] = m.bsdfData.velvet ? 1u : 0u;
        storage[31] = asuint(m.bsdfWeight.x);
        storage[32] = asuint(m.bsdfWeight.y);
        storage[33] = asuint(m.bsdfWeight.z);
        storage[34] = asuint(m.bsdfWeight.w);
        storage[35] = m.isSubsurface ? 1u : 0u;
        storage[36] = asuint(m.subsurfaceWeightFactor);
        storage[37] = asuint(m.Nv.x);
        storage[38] = asuint(m.Nv.y);
        storage[39] = asuint(m.Nv.z);
    }

    Illusion::Hair::MaterialData getHair(float3 V)
    {
        Illusion::Hair::MaterialData m = (Illusion::Hair::MaterialData)0;
        m.bsdfData.diffuseColor = asfloat(uint3(storage[0], storage[1], storage[2]));
        m.bsdfData.fresnel0 = asfloat(uint3(storage[3], storage[4], storage[5]));
        m.bsdfData.normalWS = asfloat(uint3(storage[6], storage[7], storage[8]));
        m.bsdfData.geomNormalWS = asfloat(uint3(storage[9], storage[10], storage[11]));
        m.bsdfData.perceptualRoughness = asfloat(storage[12]);
        m.bsdfData.hairStrandDirectionWS = asfloat(uint3(storage[13], storage[14], storage[15]));
        m.bsdfData.h = asfloat(storage[16]);
        m.bsdfData.absorption = asfloat(uint3(storage[17], storage[18], storage[19]));
        m.bsdfData.cuticleAngle = asfloat(storage[20]);
        m.bsdfData.perceptualRoughnessRadial = asfloat(storage[21]);
        m.V = V;
        m.Nv = asfloat(uint3(storage[22], storage[23], storage[24]));
        return m;
    }

    void setHair(Illusion::Hair::MaterialData m)
    {
        storage[0] = asuint(m.bsdfData.diffuseColor.x);
        storage[1] = asuint(m.bsdfData.diffuseColor.y);
        storage[2] = asuint(m.bsdfData.diffuseColor.z);
        storage[3] = asuint(m.bsdfData.fresnel0.x);
        storage[4] = asuint(m.bsdfData.fresnel0.y);
        storage[5] = asuint(m.bsdfData.fresnel0.z);
        storage[6] = asuint(m.bsdfData.normalWS.x);
        storage[7] = asuint(m.bsdfData.normalWS.y);
        storage[8] = asuint(m.bsdfData.normalWS.z);
        storage[9] = asuint(m.bsdfData.geomNormalWS.x);
        storage[10] = asuint(m.bsdfData.geomNormalWS.y);
        storage[11] = asuint(m.bsdfData.geomNormalWS.z);
        storage[12] = asuint(m.bsdfData.perceptualRoughness);
        storage[13] = asuint(m.bsdfData.hairStrandDirectionWS.x);
        storage[14] = asuint(m.bsdfData.hairStrandDirectionWS.y);
        storage[15] = asuint(m.bsdfData.hairStrandDirectionWS.z);
        storage[16] = asuint(m.bsdfData.h);
        storage[17] = asuint(m.bsdfData.absorption.x);
        storage[18] = asuint(m.bsdfData.absorption.y);
        storage[19] = asuint(m.bsdfData.absorption.z);
        storage[20] = asuint(m.bsdfData.cuticleAngle);
        storage[21] = asuint(m.bsdfData.perceptualRoughnessRadial);
        storage[22] = asuint(m.Nv.x);
        storage[23] = asuint(m.Nv.y);
        storage[24] = asuint(m.Nv.z);
    }
};

#endif
