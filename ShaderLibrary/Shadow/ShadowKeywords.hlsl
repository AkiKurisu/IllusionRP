#ifndef ILLUSION_SHADOW_KEYWORDS_INCLUDED
#define ILLUSION_SHADOW_KEYWORDS_INCLUDED

// Should define shadow specific keywords in standalone hlsl instead of including in Core.hlsl
// ============================ Shadow Define =============================== //

// Apply shadow bias in receiver fragment shader instead of using URP caster reverse bias in vertex shader
// Notice if enabled, URP built-in shader will not be compatible anymore
#define APPLY_SHADOW_BIAS_FRAGMENT                  (defined(_SHADOW_BIAS_FRAGMENT))

#define TRANSPARENT_RECEIVE_SCREEN_SPACE_SHADOWS    (defined(_SURFACE_TYPE_TRANSPARENT) && defined(_TRANSPARENT_WRITE_DEPTH))

// Transparent with Depth Write can still receive screen space shadows.
#define SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS   (!defined(_SURFACE_TYPE_TRANSPARENT) || TRANSPARENT_RECEIVE_SCREEN_SPACE_SHADOWS)

// Override URP multi compile keywords
#if !defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (TRANSPARENT_RECEIVE_SCREEN_SPACE_SHADOWS)
    #ifdef _MAIN_LIGHT_SHADOWS
        #undef _MAIN_LIGHT_SHADOWS
    #endif
    #ifdef _MAIN_LIGHT_SHADOWS_CASCADE
        #undef _MAIN_LIGHT_SHADOWS_CASCADE
    #endif
    #define _MAIN_LIGHT_SHADOWS_SCREEN 1
#endif

// Ref: Shadows.hlsl
#ifndef REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
    #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        #if defined(_MAIN_LIGHT_SHADOWS) || (defined(_MAIN_LIGHT_SHADOWS_SCREEN) && (SURFACE_TYPE_RECEIVE_SCREEN_SPACE_SHADOWS))
            #define REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
        #endif
    #endif
#endif
// ============================ Shadow Define =============================== //

#endif