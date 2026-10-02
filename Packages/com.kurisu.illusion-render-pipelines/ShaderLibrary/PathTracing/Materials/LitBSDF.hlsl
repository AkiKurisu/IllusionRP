#ifndef ILLUSION_HDRP_LIT_BSDF_INCLUDED
#define ILLUSION_HDRP_LIT_BSDF_INCLUDED

#include "../PathTracingPayload.hlsl"
#define ILLUSION_HDRP_ENABLE_SSS
#include "HairMaterial.hlsl"
#undef ILLUSION_HDRP_ENABLE_SSS

struct HDRPBSDF
{
    static const uint cRandomNumberCountForSampling = 3;
    Illusion::Lit::MaterialData material;
    Illusion::Fabric::MaterialData fabric;
    Illusion::Hair::MaterialData hair;
    uint family;
    float3 skinSample;
    float3 diffuseAlbedo;
    bool thin;
    bool valid;

    float getRoughness()
    {
        if (family == PT_FAMILY_HAIR) return hair.bsdfData.perceptualRoughness;
        return family == PT_FAMILY_FABRIC ? fabric.bsdfData.perceptualRoughness : material.bsdfData.perceptualRoughness;
    }

    float4 eval(const ShadingData shadingData, const float3 wo)
    {
        if (family == PT_FAMILY_UNLIT) return 0.0.xxxx;
        if (family == PT_FAMILY_HAIR)
        {
            Illusion::Hair::MaterialResult value;
            Illusion::Hair::EvaluateMaterial(hair, wo, value);
            return float4(value.specValue, Average(value.specValue));
        }
        if (family == PT_FAMILY_FABRIC)
        {
            Illusion::Fabric::MaterialResult value;
            Illusion::Fabric::EvaluateMaterial(fabric, wo, value);
            return float4(value.diffValue + value.specValue, Average(value.specValue));
        }
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
            Illusion::Hair::EvaluateMaterial(hair, wo, value);
            return value.specPdf;
        }
        if (family == PT_FAMILY_FABRIC)
        {
            Illusion::Fabric::MaterialResult value;
            Illusion::Fabric::EvaluateMaterial(fabric, wo, value);
            return value.diffPdf + value.specPdf;
        }
        if (Illusion::Lit::IsBelow(material, wo)) return 0.0;
        Illusion::Lit::MaterialResult result;
        Illusion::Lit::EvaluateMaterial(material, wo, result);
        return result.diffPdf + result.specPdf;
    }

    bool sample(const ShadingData shadingData, const float4 inputSample, out BSDFSample result, bool useImportanceSampling)
    {
        result = (BSDFSample)0;
        if (!valid) return false;
        if (family == PT_FAMILY_HAIR)
        {
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
            Illusion::Fabric::MaterialResult value;
            if (!Illusion::Fabric::SampleMaterial(fabric, inputSample.xyz, result.wo, value)) return false;
            result.pdf = value.diffPdf + value.specPdf;
            if (!(result.pdf > 0.0)) return false;
            result.weight = (value.diffValue + value.specValue) / result.pdf;
            result.lobe = !Illusion::Fabric::IsAbove(fabric, result.wo) ? (uint)LobeType::DiffuseTransmission
                : (uint)(inputSample.z < fabric.bsdfWeight[0] ? LobeType::DiffuseReflection : LobeType::SpecularReflection);
            result.lobeP = 1.0;
            return all(isfinite(result.weight));
        }
        Illusion::Lit::MaterialResult value;
        float3 materialSample = family == PT_FAMILY_SKIN ? skinSample : inputSample.xyz;
        if (!Illusion::Lit::SampleMaterial(material, materialSample, result.wo, value, thin)) return false;
        result.pdf = value.diffPdf + value.specPdf;
        if (!(result.pdf > 0.0)) return false;
        result.weight = (value.diffValue + value.specValue) / result.pdf;
        bool transmission = !material.isSubsurface && Illusion::Lit::IsAbove(material) != Illusion::Lit::IsAbove(material, result.wo);
        if (thin && transmission)
            result.weight *= Illusion::Lit::GetMaterialAbsorption(material, (Illusion::Lit::SurfaceData)0, 0.0, Illusion::Lit::IsBelow(material, result.wo), true);
        bool diffuse = material.isSubsurface || materialSample.z < material.bsdfWeight[0];
        bool delta = value.specPdf >= DELTA_PDF * BSDF_WEIGHT_EPSILON;
        result.lobe = transmission ? (uint)(delta ? LobeType::DeltaTransmission : (diffuse ? LobeType::DiffuseTransmission : LobeType::SpecularTransmission))
                                   : (uint)(delta ? LobeType::DeltaReflection : (diffuse ? LobeType::DiffuseReflection : LobeType::SpecularReflection));
        result.lobeP = delta ? value.specPdf / DELTA_PDF : 1.0;
        return all(isfinite(result.weight));
    }

    uint getLobes(const ShadingData shadingData)
    {
        if (family == PT_FAMILY_UNLIT) return 0u;
        if (family == PT_FAMILY_HAIR) return (uint)LobeType::SpecularReflection | (uint)LobeType::SpecularTransmission;
        if (family == PT_FAMILY_FABRIC)
            return (uint)LobeType::DiffuseReflection | (uint)LobeType::SpecularReflection | (fabric.bsdfWeight[2] > 0.0 ? (uint)LobeType::DiffuseTransmission : 0u);
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
            diffuse = fabric.bsdfData.diffuseColor;
            specular = fabric.bsdfData.fresnel0;
            return;
        }
        if (family == PT_FAMILY_HAIR)
        {
            diffuse = 0.0;
            specular = hair.bsdfData.diffuseColor;
            return;
        }
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
        if (family == PT_FAMILY_FABRIC || family == PT_FAMILY_HAIR || Illusion::Lit::IsAbove(material)) return;
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
        material.bsdfData.ior = relativeIoR;
    }
};

#endif
