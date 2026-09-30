#ifndef ILLUSION_HDRP_HAIR_MATERIAL_INCLUDED
#define ILLUSION_HDRP_HAIR_MATERIAL_INCLUDED
#include "FabricMaterial.hlsl"
#undef UNITY_PATH_TRACING_MATERIAL_INCLUDED
#undef UNITY_PATH_TRACING_BSDF_INCLUDED
#define Luminance HDRPLuminance
namespace Illusion
{
namespace Hair
{
#include "Hair.cs.hlsl"
typedef Lit::BuiltinData BuiltinData;
typedef Lit::PathPayload PathPayload;
typedef Lit::AOVData AOVData;
#include "PathTracingMaterial.hlsl"
#include "PathTracingBSDF.hlsl"
#include "Hair.hlsl"
#include "HairReference.hlsl"
#define WorldRayDirection() (-g_HDRPViewDirection)
#include "HairPathTracing.hlsl"
#undef WorldRayDirection
}
}
#undef Luminance
#endif
