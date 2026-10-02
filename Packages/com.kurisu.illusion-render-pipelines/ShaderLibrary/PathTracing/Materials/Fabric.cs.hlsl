//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef FABRIC_CS_HLSL
#define FABRIC_CS_HLSL
//
// UnityEngine.Rendering.HighDefinition.Fabric+MaterialFeatureFlags:  static fields
//
#define MATERIALFEATUREFLAGS_FABRIC_COTTON_WOOL (1)
#define MATERIALFEATUREFLAGS_FABRIC_SUBSURFACE_SCATTERING (2)
#define MATERIALFEATUREFLAGS_FABRIC_TRANSMISSION (4)

//
// UnityEngine.Rendering.HighDefinition.Fabric+BSDFData:  static fields
//
#define DEBUGVIEW_FABRIC_BSDFDATA_MATERIAL_FEATURES (1350)
#define DEBUGVIEW_FABRIC_BSDFDATA_DIFFUSE_COLOR (1351)
#define DEBUGVIEW_FABRIC_BSDFDATA_FRESNEL0 (1352)
#define DEBUGVIEW_FABRIC_BSDFDATA_AMBIENT_OCCLUSION (1353)
#define DEBUGVIEW_FABRIC_BSDFDATA_SPECULAR_OCCLUSION (1354)
#define DEBUGVIEW_FABRIC_BSDFDATA_NORMAL_WS (1355)
#define DEBUGVIEW_FABRIC_BSDFDATA_NORMAL_VIEW_SPACE (1356)
#define DEBUGVIEW_FABRIC_BSDFDATA_GEOMETRIC_NORMAL (1357)
#define DEBUGVIEW_FABRIC_BSDFDATA_GEOMETRIC_NORMAL_VIEW_SPACE (1358)
#define DEBUGVIEW_FABRIC_BSDFDATA_PERCEPTUAL_ROUGHNESS (1359)
#define DEBUGVIEW_FABRIC_BSDFDATA_DIFFUSION_PROFILE_INDEX (1360)
#define DEBUGVIEW_FABRIC_BSDFDATA_SUBSURFACE_MASK (1361)
#define DEBUGVIEW_FABRIC_BSDFDATA_THICKNESS (1362)
#define DEBUGVIEW_FABRIC_BSDFDATA_USE_THICK_OBJECT_MODE (1363)
#define DEBUGVIEW_FABRIC_BSDFDATA_TRANSMITTANCE (1364)
#define DEBUGVIEW_FABRIC_BSDFDATA_TANGENT_WS (1365)
#define DEBUGVIEW_FABRIC_BSDFDATA_BITANGENT_WS (1366)
#define DEBUGVIEW_FABRIC_BSDFDATA_ROUGHNESS_T (1367)
#define DEBUGVIEW_FABRIC_BSDFDATA_ROUGHNESS_B (1368)
#define DEBUGVIEW_FABRIC_BSDFDATA_ANISOTROPY (1369)

//
// UnityEngine.Rendering.HighDefinition.Fabric+SurfaceData:  static fields
//
#define DEBUGVIEW_FABRIC_SURFACEDATA_MATERIAL_FEATURES (1300)
#define DEBUGVIEW_FABRIC_SURFACEDATA_BASE_COLOR (1301)
#define DEBUGVIEW_FABRIC_SURFACEDATA_SPECULAR_OCCLUSION (1302)
#define DEBUGVIEW_FABRIC_SURFACEDATA_NORMAL (1303)
#define DEBUGVIEW_FABRIC_SURFACEDATA_NORMAL_VIEW_SPACE (1304)
#define DEBUGVIEW_FABRIC_SURFACEDATA_GEOMETRIC_NORMAL (1305)
#define DEBUGVIEW_FABRIC_SURFACEDATA_GEOMETRIC_NORMAL_VIEW_SPACE (1306)
#define DEBUGVIEW_FABRIC_SURFACEDATA_SMOOTHNESS (1307)
#define DEBUGVIEW_FABRIC_SURFACEDATA_AMBIENT_OCCLUSION (1308)
#define DEBUGVIEW_FABRIC_SURFACEDATA_SPECULAR_TINT (1309)
#define DEBUGVIEW_FABRIC_SURFACEDATA_DIFFUSION_PROFILE_HASH (1310)
#define DEBUGVIEW_FABRIC_SURFACEDATA_SUBSURFACE_MASK (1311)
#define DEBUGVIEW_FABRIC_SURFACEDATA_THICKNESS (1312)
#define DEBUGVIEW_FABRIC_SURFACEDATA_TRANSMISSION_MASK (1313)
#define DEBUGVIEW_FABRIC_SURFACEDATA_TANGENT (1314)
#define DEBUGVIEW_FABRIC_SURFACEDATA_ANISOTROPY (1315)

// Generated from UnityEngine.Rendering.HighDefinition.Fabric+BSDFData
// PackingRules = Exact
struct BSDFData
{
    uint materialFeatures;
    float3 diffuseColor;
    float3 fresnel0;
    float ambientOcclusion;
    float specularOcclusion;
    float3 normalWS;
    float3 geomNormalWS;
    float perceptualRoughness;
    uint diffusionProfileIndex;
    float subsurfaceMask;
    float thickness;
    bool useThickObjectMode;
    float3 transmittance;
    float3 tangentWS;
    float3 bitangentWS;
    float roughnessT;
    float roughnessB;
    float anisotropy;
    float3 sheenColor;  // @IllusionRP: color of the cloth lobe
    float sheenAmount;  // @IllusionRP: blend from the GGX lobe to the cloth lobe
    bool velvet;        // @IllusionRP: the cloth lobe uses the inverted GGX distribution
};

// Generated from UnityEngine.Rendering.HighDefinition.Fabric+SurfaceData
// PackingRules = Exact
struct SurfaceData
{
    uint materialFeatures;
    float3 baseColor;
    float specularOcclusion;
    float3 normalWS;
    float3 geomNormalWS;
    float perceptualSmoothness;
    float ambientOcclusion;
    float3 specularColor;
    uint diffusionProfileHash;
    float subsurfaceMask;
    float thickness;
    float3 transmissionMask;
    float3 tangentWS;
    float anisotropy;
};


#endif
