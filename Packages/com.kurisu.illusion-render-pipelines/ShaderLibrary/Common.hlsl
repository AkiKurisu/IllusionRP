#ifndef ILLUSION_COMMON_INCLUDED
#define ILLUSION_COMMON_INCLUDED

// Ray tracing reflection rejects the min16float constants that mobile targets map half to.
#if defined(SHADER_STAGE_RAY_TRACING) && !defined(PREFER_HALF)
#define PREFER_HALF 0
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#endif
