#ifndef ILLUSION_PATH_TRACING_INLINE_MATERIAL_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_MATERIAL_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

#undef TEXTURE2D
#define TEXTURE2D(textureName) PathTracingInlineTexture2D textureName
#undef SAMPLER_CMP
#define SAMPLER_CMP(samplerName) PathTracingInlineSamplerComparison samplerName
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY(textureName, samplerName, coord2, index) PathTracingInlineSampleOther(textureName, samplerName, float3(coord2, index), 0)
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY_LOD
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, coord2, index, lod) PathTracingInlineSampleOther(textureName, samplerName, float3(coord2, index), lod)
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS(textureName, samplerName, coord2, index, bias) PathTracingInlineSampleOther(textureName, samplerName, float3(coord2, index), 0)
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD(textureName, samplerName, coord2, index, dpdx, dpdy) PathTracingInlineSampleOther(textureName, samplerName, float3(coord2, index), 0)
#undef PLATFORM_SAMPLE_TEXTURECUBE
#define PLATFORM_SAMPLE_TEXTURECUBE(textureName, samplerName, coord3) PathTracingInlineSampleOther(textureName, samplerName, coord3, 0)
#undef PLATFORM_SAMPLE_TEXTURECUBE_LOD
#define PLATFORM_SAMPLE_TEXTURECUBE_LOD(textureName, samplerName, coord3, lod) PathTracingInlineSampleOther(textureName, samplerName, coord3, lod)
#undef PLATFORM_SAMPLE_TEXTURECUBE_BIAS
#define PLATFORM_SAMPLE_TEXTURECUBE_BIAS(textureName, samplerName, coord3, bias) PathTracingInlineSampleOther(textureName, samplerName, coord3, bias)
#undef PLATFORM_SAMPLE_TEXTURECUBE_ARRAY
#define PLATFORM_SAMPLE_TEXTURECUBE_ARRAY(textureName, samplerName, coord3, index) PathTracingInlineSampleOther(textureName, samplerName, float4(coord3, index), 0)
#undef PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_LOD
#define PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_LOD(textureName, samplerName, coord3, index, lod) PathTracingInlineSampleOther(textureName, samplerName, float4(coord3, index), lod)
#undef PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS
#define PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS(textureName, samplerName, coord3, index, bias) PathTracingInlineSampleOther(textureName, samplerName, float4(coord3, index), bias)
#undef PLATFORM_SAMPLE_TEXTURE3D
#define PLATFORM_SAMPLE_TEXTURE3D(textureName, samplerName, coord3) PathTracingInlineSampleOther(textureName, samplerName, coord3, 0)
#undef PLATFORM_SAMPLE_TEXTURE3D_LOD
#define PLATFORM_SAMPLE_TEXTURE3D_LOD(textureName, samplerName, coord3, lod) PathTracingInlineSampleOther(textureName, samplerName, coord3, lod)
#undef SAMPLE_TEXTURE2D_SHADOW
#define SAMPLE_TEXTURE2D_SHADOW(textureName, samplerName, coord3) PathTracingInlineSampleShadow(textureName, samplerName, coord3)
#undef CALCULATE_TEXTURE2D_LOD
#define CALCULATE_TEXTURE2D_LOD(textureName, samplerName, coord2) PathTracingInlineLod(textureName, samplerName, coord2)
#undef GATHER_TEXTURE2D
#define GATHER_TEXTURE2D(textureName, samplerName, coord2) PathTracingInlineGather(textureName, samplerName, coord2)
#undef TEXTURE2D_ARRAY
#define TEXTURE2D_ARRAY(textureName) PathTracingInlineTexture2DArray textureName
#undef TEXTURECUBE
#define TEXTURECUBE(textureName) PathTracingInlineTextureCube textureName
#undef TEXTURECUBE_ARRAY
#define TEXTURECUBE_ARRAY(textureName) PathTracingInlineTextureCubeArray textureName
#undef TEXTURE3D
#define TEXTURE3D(textureName) PathTracingInlineTexture3D textureName
#undef SAMPLER
#define SAMPLER(samplerName) PathTracingInlineSampler samplerName

#undef PLATFORM_SAMPLE_TEXTURE2D
#define PLATFORM_SAMPLE_TEXTURE2D(textureName, samplerName, coord2) PathTracingInlineSampleGrad(textureName, samplerName, coord2, PathTracingGradX(coord2), PathTracingGradY(coord2))
#undef PLATFORM_SAMPLE_TEXTURE2D_BIAS
#define PLATFORM_SAMPLE_TEXTURE2D_BIAS(textureName, samplerName, coord2, bias) PathTracingInlineSampleGrad(textureName, samplerName, coord2, PathTracingGradX(coord2) * exp2(bias), PathTracingGradY(coord2) * exp2(bias))
#undef PLATFORM_SAMPLE_TEXTURE2D_GRAD
#define PLATFORM_SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, coord2, dpdx, dpdy) PathTracingInlineSampleGrad(textureName, samplerName, coord2, PathTracingGradX(coord2), PathTracingGradY(coord2))
#undef PLATFORM_SAMPLE_TEXTURE2D_LOD
#define PLATFORM_SAMPLE_TEXTURE2D_LOD(textureName, samplerName, coord2, lod) PathTracingInlineSampleLevel(textureName, samplerName, coord2, lod)
#undef LOAD_TEXTURE2D
#define LOAD_TEXTURE2D(textureName, unCoord2) PathTracingInlineLoad(textureName, int3(unCoord2, 0))
#undef LOAD_TEXTURE2D_LOD
#define LOAD_TEXTURE2D_LOD(textureName, unCoord2, lod) PathTracingInlineLoad(textureName, int3(unCoord2, lod))

#endif
