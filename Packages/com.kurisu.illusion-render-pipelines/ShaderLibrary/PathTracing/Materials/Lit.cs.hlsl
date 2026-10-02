//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef LIT_CS_HLSL
#define LIT_CS_HLSL
//
// UnityEngine.Rendering.HighDefinition.Lit+MaterialFeatureFlags:  static fields
//
#define MATERIALFEATUREFLAGS_LIT_STANDARD (1)
#define MATERIALFEATUREFLAGS_LIT_SPECULAR_COLOR (2)
#define MATERIALFEATUREFLAGS_LIT_SUBSURFACE_SCATTERING (4)
#define MATERIALFEATUREFLAGS_LIT_TRANSMISSION (8)
#define MATERIALFEATUREFLAGS_LIT_ANISOTROPY (16)
#define MATERIALFEATUREFLAGS_LIT_IRIDESCENCE (32)
#define MATERIALFEATUREFLAGS_LIT_CLEAR_COAT (64)
#define MATERIALFEATUREFLAGS_LIT_COLORED_TRANSMISSION (128)

//
// UnityEngine.Rendering.HighDefinition.Lit+BSDFData:  static fields
//
#define DEBUGVIEW_LIT_BSDFDATA_MATERIAL_FEATURES (1050)
#define DEBUGVIEW_LIT_BSDFDATA_DIFFUSE_COLOR (1051)
#define DEBUGVIEW_LIT_BSDFDATA_FRESNEL0 (1052)
#define DEBUGVIEW_LIT_BSDFDATA_FRESNEL90 (1053)
#define DEBUGVIEW_LIT_BSDFDATA_AMBIENT_OCCLUSION (1054)
#define DEBUGVIEW_LIT_BSDFDATA_SPECULAR_OCCLUSION (1055)
#define DEBUGVIEW_LIT_BSDFDATA_NORMAL_WS (1056)
#define DEBUGVIEW_LIT_BSDFDATA_NORMAL_VIEW_SPACE (1057)
#define DEBUGVIEW_LIT_BSDFDATA_PERCEPTUAL_ROUGHNESS (1058)
#define DEBUGVIEW_LIT_BSDFDATA_COAT_MASK (1059)
#define DEBUGVIEW_LIT_BSDFDATA_DIFFUSION_PROFILE_INDEX (1060)
#define DEBUGVIEW_LIT_BSDFDATA_SUBSURFACE_MASK (1061)
#define DEBUGVIEW_LIT_BSDFDATA_THICKNESS (1062)
#define DEBUGVIEW_LIT_BSDFDATA_USE_THICK_OBJECT_MODE (1063)
#define DEBUGVIEW_LIT_BSDFDATA_TRANSMITTANCE (1064)
#define DEBUGVIEW_LIT_BSDFDATA_TANGENT_WS (1065)
#define DEBUGVIEW_LIT_BSDFDATA_BITANGENT_WS (1066)
#define DEBUGVIEW_LIT_BSDFDATA_ROUGHNESS_T (1067)
#define DEBUGVIEW_LIT_BSDFDATA_ROUGHNESS_B (1068)
#define DEBUGVIEW_LIT_BSDFDATA_ANISOTROPY (1069)
#define DEBUGVIEW_LIT_BSDFDATA_IRIDESCENCE_THICKNESS (1070)
#define DEBUGVIEW_LIT_BSDFDATA_IRIDESCENCE_MASK (1071)
#define DEBUGVIEW_LIT_BSDFDATA_COAT_ROUGHNESS (1072)
#define DEBUGVIEW_LIT_BSDFDATA_GEOMETRIC_NORMAL (1073)
#define DEBUGVIEW_LIT_BSDFDATA_GEOMETRIC_NORMAL_VIEW_SPACE (1074)
#define DEBUGVIEW_LIT_BSDFDATA_IOR (1075)
#define DEBUGVIEW_LIT_BSDFDATA_ABSORPTION_COEFFICIENT (1076)
#define DEBUGVIEW_LIT_BSDFDATA_TRANSMITTANCE_MASK (1077)

//
// UnityEngine.Rendering.HighDefinition.Lit+SurfaceData:  static fields
//
#define DEBUGVIEW_LIT_SURFACEDATA_MATERIAL_FEATURES (1000)
#define DEBUGVIEW_LIT_SURFACEDATA_BASE_COLOR (1001)
#define DEBUGVIEW_LIT_SURFACEDATA_SPECULAR_OCCLUSION (1002)
#define DEBUGVIEW_LIT_SURFACEDATA_NORMAL (1003)
#define DEBUGVIEW_LIT_SURFACEDATA_NORMAL_VIEW_SPACE (1004)
#define DEBUGVIEW_LIT_SURFACEDATA_SMOOTHNESS (1005)
#define DEBUGVIEW_LIT_SURFACEDATA_AMBIENT_OCCLUSION (1006)
#define DEBUGVIEW_LIT_SURFACEDATA_METALLIC (1007)
#define DEBUGVIEW_LIT_SURFACEDATA_COAT_MASK (1008)
#define DEBUGVIEW_LIT_SURFACEDATA_SPECULAR_COLOR (1009)
#define DEBUGVIEW_LIT_SURFACEDATA_DIFFUSION_PROFILE_HASH (1010)
#define DEBUGVIEW_LIT_SURFACEDATA_SUBSURFACE_MASK (1011)
#define DEBUGVIEW_LIT_SURFACEDATA_THICKNESS (1012)
#define DEBUGVIEW_LIT_SURFACEDATA_TRANSMISSION_MASK (1013)
#define DEBUGVIEW_LIT_SURFACEDATA_TANGENT (1014)
#define DEBUGVIEW_LIT_SURFACEDATA_ANISOTROPY (1015)
#define DEBUGVIEW_LIT_SURFACEDATA_IRIDESCENCE_LAYER_THICKNESS (1016)
#define DEBUGVIEW_LIT_SURFACEDATA_IRIDESCENCE_MASK (1017)
#define DEBUGVIEW_LIT_SURFACEDATA_GEOMETRIC_NORMAL (1018)
#define DEBUGVIEW_LIT_SURFACEDATA_GEOMETRIC_NORMAL_VIEW_SPACE (1019)
#define DEBUGVIEW_LIT_SURFACEDATA_INDEX_OF_REFRACTION (1020)
#define DEBUGVIEW_LIT_SURFACEDATA_TRANSMITTANCE_COLOR (1021)
#define DEBUGVIEW_LIT_SURFACEDATA_TRANSMITTANCE_ABSORPTION_DISTANCE (1022)
#define DEBUGVIEW_LIT_SURFACEDATA_TRANSMITTANCE_MASK (1023)

// Generated from UnityEngine.Rendering.HighDefinition.Lit+BSDFData
// PackingRules = Exact
struct BSDFData
{
    uint materialFeatures;
    real3 diffuseColor;
    real3 fresnel0;
    real fresnel90;
    real ambientOcclusion;
    real specularOcclusion;
    float3 normalWS;
    real perceptualRoughness;
    real coatMask;
    uint diffusionProfileIndex;
    real subsurfaceMask;
    real thickness;
    bool useThickObjectMode;
    real3 transmittance;
    float3 tangentWS;
    float3 bitangentWS;
    real roughnessT;
    real roughnessB;
    real anisotropy;
    real iridescenceThickness;
    real iridescenceMask;
    real coatRoughness;
    real3 geomNormalWS;
    real ior;
    real3 absorptionCoefficient;
    real transmittanceMask;
    real secondaryRoughness;  // @IllusionRP
    real lobeMix;  // @IllusionRP
};

// Generated from UnityEngine.Rendering.HighDefinition.Lit+SurfaceData
// PackingRules = Exact
struct SurfaceData
{
    uint materialFeatures;
    real3 baseColor;
    real specularOcclusion;
    float3 normalWS;
    real perceptualSmoothness;
    real ambientOcclusion;
    real metallic;
    real coatMask;
    real3 specularColor;
    uint diffusionProfileHash;
    real subsurfaceMask;
    real thickness;
    real3 transmissionMask;
    float3 tangentWS;
    real anisotropy;
    real iridescenceThickness;
    real iridescenceMask;
    real3 geomNormalWS;
    real ior;
    real3 transmittanceColor;
    real atDistance;
    real transmittanceMask;
};


#endif
