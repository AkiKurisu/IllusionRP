#ifndef UNIVERSAL_SPEEDTREE7_INPUT_INCLUDED
#define UNIVERSAL_SPEEDTREE7_INPUT_INCLUDED

#define SPEEDTREE_Y_UP

#ifdef GEOM_TYPE_BRANCH_DETAIL
    #define GEOM_TYPE_BRANCH
#endif


#if defined(GEOM_TYPE_FROND) || defined(GEOM_TYPE_LEAF) || defined(GEOM_TYPE_FACING_LEAF)
#define SPEEDTREE_ALPHATEST
#endif

#ifdef SCENESELECTIONPASS
    int _ObjectId;
    int _PassValue;
#endif

#include "SpeedTree7CommonInput.hlsl"

#ifdef GEOM_TYPE_BRANCH_DETAIL
    TEXTURE2D(_DetailTex);
    SAMPLER(sampler_DetailTex);
#endif

#define _Surface 0.0 // Speed Trees are always opaque

#endif
