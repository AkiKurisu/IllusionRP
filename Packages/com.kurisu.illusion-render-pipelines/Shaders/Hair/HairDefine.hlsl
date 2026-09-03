// ============================ Shader Define for Hair =============================== //

// Marschner lobes
#ifndef HAIR_MARSCHNER_R
    #define HAIR_MARSCHNER_R                    1
#endif
#ifndef HAIR_MARSCHNER_TT
    #define HAIR_MARSCHNER_TT                   1
#endif
#ifndef HAIR_MARSCHNER_TRT
    #define HAIR_MARSCHNER_TRT                  1
#endif

#define DEFAULT_HAIR_SPECULAR_VALUE             0.0465          // Hair is IOR 1.55

#ifndef HAIR_SHIFT_VALUE
    #define HAIR_SHIFT_VALUE                    0.035
#endif

#ifndef HAIR_MULTI_SCATTERING
    #define HAIR_MULTI_SCATTERING               1
#endif

#ifndef _MARSCHNER_HAIR
    #define _MARSCHNER_HAIR                     0
#endif

#ifdef _KAJIYA_DIFFUSE_ATTENUATION
    #define DIFFUSE_ATTENUATION                 KajiyaKayDiffuseAttenuation
#else
    #define DIFFUSE_ATTENUATION                 UnchartedDiffuseAttenuation
#endif

#ifndef HAIR_INDIRECT_MARSCHNER
    #define HAIR_INDIRECT_MARSCHNER             _MARSCHNER_HAIR
#endif

// 15 degrees
#define TRANSMISSION_WRAP_ANGLE                 (PI/12)
#define TRANSMISSION_WRAP_LIGHT                 cos(PI/2 - TRANSMISSION_WRAP_ANGLE)

struct HairData
{
    float3 GeomNormal;
    float3 Tangent;
    half3 Tint;
    half Metallic;
    half Noise;
    half HighLight;
    half Roughness;
    half Shadow;
    half Backlit;
    half Area;
    half Wet;
};
// ============================ Shader Define for Hair =============================== //