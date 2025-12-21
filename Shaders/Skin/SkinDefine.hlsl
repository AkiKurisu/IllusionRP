// ============================ Shader Define for Skin =============================== //
// Enable spherical gaussian sss when screen space scattering off
#define SPHERICAL_GAUSSIAN_SSS                  (!defined(_SCREEN_SPACE_SSS))

// Partially Apply albedo twice to get softer effect
// Reference: Pre- and Post-Scatter in HDRP
#define PRE_POST_SCATTER                        1

// 15 degrees
#define TRANSMISSION_WRAP_ANGLE                 (PI/12)
#define TRANSMISSION_WRAP_LIGHT                 cos(PI/2 - TRANSMISSION_WRAP_ANGLE)

// Unified Translucency effect strength for skin
#define TRANSLUCENCY_STRENGTH                   4

struct SkinData
{
    float3 GeomNormal;                           // Geometric normal (without detail normal map).
    half3 Scatter;
    half3 Transmittance;
    half Thickness;
    half LobeWeight;                            // Dual lobes mix weight.
    half Smoothness;                            // Lobe 2 smoothness.
    half PerceptualRoughness;                   // Lobe 2 roughness.
    half PerceptualRoughnessMix;                // Weighted blended roughness from dual lobes.
    half Wet;
    half F0;
    uint DiffusionProfileIndex;
};
// ============================ Shader Define for Skin =============================== //