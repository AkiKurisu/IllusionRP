#ifndef ILLUSION_HDRP_LIT_MATERIAL_INCLUDED
#define ILLUSION_HDRP_LIT_MATERIAL_INCLUDED

// @IllusionRP: isolate HDRP helpers from RTXPT helpers with the same names.
#ifdef FLT_MIN
#undef FLT_MIN
#endif
#define Luminance HDRPLuminance
namespace Illusion
{
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"
#include "PreIntegratedFGD.cs.hlsl"

Texture2D<float4> _PreIntegratedFGD_GGXDisneyDiffuse;
SamplerState s_linear_clamp_sampler;
static float3 g_HDRPViewDirection;

namespace Lit
{
#include "Lit.cs.hlsl"
#include "BuiltinData.cs.hlsl"

struct PathPayload
{
    float maxRoughness;
    uint2 pixelCoord;
};
struct AOVData
{
    float3 albedo;
    float3 normal;
};

#include "PathTracingMaterial.hlsl"
#define WorldRayDirection() (-g_HDRPViewDirection)
#define LIT_USE_GGX_ENERGY_COMPENSATION
#define _SURFACE_TYPE_TRANSPARENT
#define HAS_REFRACTION 1
#include "PathTracingBSDF.hlsl"
#include "LitPathTracing.hlsl"
#undef HAS_REFRACTION
#undef _SURFACE_TYPE_TRANSPARENT
#undef LIT_USE_GGX_ENERGY_COMPENSATION
#undef WorldRayDirection
}


}
#undef Luminance
#endif
