#ifndef ILLUSION_HDRP_FABRIC_MATERIAL_INCLUDED
#define ILLUSION_HDRP_FABRIC_MATERIAL_INCLUDED
#include "LitMaterial.hlsl"

// @IllusionRP: instantiate HDRP's material functions with the Fabric data type.
#undef UNITY_PATH_TRACING_MATERIAL_INCLUDED
#undef UNITY_PATH_TRACING_BSDF_INCLUDED
#define Luminance HDRPLuminance
namespace Illusion
{
namespace Fabric
{
#include "Fabric.cs.hlsl"
typedef Lit::BuiltinData BuiltinData;
typedef Lit::PathPayload PathPayload;
typedef Lit::AOVData AOVData;
#include "PathTracingMaterial.hlsl"
#define WorldRayDirection() (-g_HDRPViewDirection)
#include "PathTracingBSDF.hlsl"
#include "FabricPathTracing.hlsl"
#undef WorldRayDirection
}
}
#undef Luminance
#endif
