#ifndef ILLUSION_CORE_INCLUDED
#define ILLUSION_CORE_INCLUDED
// ============================ Global Shader Define =============================== //
// Enable indirect IBL
#define PRE_INTEGRATED_FGD                      1

// Default keep using lambert diffuse model implemented in URP
#ifndef USE_DIFFUSE_LAMBERT_BRDF
    #define USE_DIFFUSE_LAMBERT_BRDF            1
#endif

// Enable microfacet BRDF, else use approximate version implemented in URP
// Reference: HDRP
#define GGX_BRDF                                1

// Enable AOMultiBounce for indirect lighting occlusion
#define EVALUATE_AO_MULTI_BOUNCE                1

// Subsurface Diffuse / ForwardGBuffer / Forward
#define STENCIL_USAGE_IS_SKIN                   (1 << 0)   // 0001
#define STENCIL_USAGE_IS_HAIR                   (1 << 1)   // 0010
#define STENCIL_USAGE_IS_SSR                    (1 << 2)   // 0100
#define STENCIL_USAGE_SUBSURFACE_SCATTERING     (1 << 3)   // 1000

// Depth Only / Depth Normal
#define STENCIL_USAGE_NO_AO                     (1 << 0)   // 0001

// Indirect Diffuse Mode
#define INDIRECTDIFFUSEMODE_OFF                 (0)
#define INDIRECTDIFFUSEMODE_SCREEN_SPACE        (1)
#define INDIRECTDIFFUSEMODE_RAY_TRACED          (2)
#define INDIRECTDIFFUSEMODE_MIXED               (3)
// ============================ Global Shader Define =============================== //

// ================================= Macro Define ================================= //
// Helper macros to handle XR single-pass with Texture2DArray
// With single-pass instancing, unity_StereoEyeIndex is used to select the eye in the current context.
// Otherwise, the index is statically set to 0
#if defined(USE_TEXTURE2D_X_AS_ARRAY)
    // Only single-pass stereo instancing used array indexing
    #if defined(UNITY_STEREO_INSTANCING_ENABLED)
        #define SLICE_ARRAY_INDEX   unity_StereoEyeIndex
    #else
        #define SLICE_ARRAY_INDEX  0
    #endif
#else
    #define COORD_TEXTURE2D_X(pixelCoord)                                    pixelCoord
    #define LOAD_TEXTURE2D_X_MSAA                                            LOAD_TEXTURE2D_MSAA
    #define RW_TEXTURE2D_X                                                   RW_TEXTURE2D
    #define TEXTURE2D_X_MSAA(type, textureName)                              Texture2DMS<type, 1> textureName
    #define TEXTURE2D_X_UINT(textureName)                                    Texture2D<uint> textureName
    #define TEXTURE2D_X_UINT2(textureName)                                   Texture2D<uint2> textureName
    #define INDEX_TEXTURE2D_ARRAY_X(slot)                                    (slot)
#endif
// ================================= Macro Define ================================= //
#endif