#ifndef ILLUSION_HDRP_LIT_BSDF_INCLUDED
#define ILLUSION_HDRP_LIT_BSDF_INCLUDED

#include "LitMaterial.hlsl"

struct HDRPBSDF
{
    static const uint cRandomNumberCountForSampling = 3;
    Illusion::Lit::MaterialData material;
    bool valid;

    float getRoughness()
    {
        return material.bsdfData.perceptualRoughness;
    }

    float4 eval(const ShadingData shadingData, const float3 wo)
    {
        Illusion::Lit::MaterialResult result;
        Illusion::Lit::EvaluateMaterial(material, wo, result);
        return float4(result.diffValue + result.specValue, Average(result.specValue));
    }

    float evalPdf(const ShadingData shadingData, const float3 wo, bool useImportanceSampling)
    {
        Illusion::Lit::MaterialResult result;
        Illusion::Lit::EvaluateMaterial(material, wo, result);
        return result.diffPdf + result.specPdf;
    }

    bool sample(const ShadingData shadingData, const float4 inputSample, out BSDFSample result, bool useImportanceSampling)
    {
        result = (BSDFSample)0;
        if (!valid) return false;
        Illusion::Lit::MaterialResult value;
        if (!Illusion::Lit::SampleMaterial(material, inputSample.xyz, result.wo, value)) return false;
        result.pdf = value.diffPdf + value.specPdf;
        if (!(result.pdf > 0.0)) return false;
        result.weight = (value.diffValue + value.specValue) / result.pdf;
        bool transmission = !Illusion::Lit::IsAbove(material, result.wo);
        bool diffuse = inputSample.z < material.bsdfWeight[0];
        bool delta = value.specPdf >= DELTA_PDF * BSDF_WEIGHT_EPSILON;
        result.lobe = transmission ? (uint)(delta ? LobeType::DeltaTransmission : (diffuse ? LobeType::DiffuseTransmission : LobeType::SpecularTransmission))
                                   : (uint)(delta ? LobeType::DeltaReflection : (diffuse ? LobeType::DiffuseReflection : LobeType::SpecularReflection));
        result.lobeP = delta ? value.specPdf / DELTA_PDF : 1.0;
        return all(isfinite(result.weight));
    }

    uint getLobes(const ShadingData shadingData)
    {
        return (uint)LobeType::DiffuseReflection | (uint)LobeType::SpecularReflection
            | (material.bsdfData.transmittanceMask > 0.0 ? (uint)LobeType::SpecularTransmission : 0u);
    }

    void estimateSpecDiffBSDF(out float3 diffuse, out float3 specular, const float3 normal, const float3 view)
    {
        diffuse = material.bsdfData.diffuseColor * (1.0 - material.bsdfData.transmittanceMask);
        float2 uv = Illusion::Remap01ToHalfTexelCoord(float2(sqrt(saturate(dot(normal, view))), material.bsdfData.perceptualRoughness), FGDTEXTURE_RESOLUTION);
        float2 fgd = Illusion::_PreIntegratedFGD_GGXDisneyDiffuse.SampleLevel(Illusion::s_linear_clamp_sampler, uv, 0).xy;
        specular = lerp(fgd.x, fgd.y, material.bsdfData.fresnel0) * Illusion::Lit::GetSpecularCompensation(material);
    }

    void evalDeltaLobes(const ShadingData shadingData, out DeltaLobe lobes[cMaxDeltaLobes], out int count, out float nonDeltaPart)
    {
        count = 0;
        nonDeltaPart = 1.0;
        [unroll] for (uint i = 0; i < cMaxDeltaLobes; ++i) lobes[i] = (DeltaLobe)0;
        if (Illusion::Lit::IsAbove(material)) return;
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

    void setOutsideIoR(float relativeIoR)
    {
        material.bsdfData.ior = relativeIoR;
    }
};

#endif
