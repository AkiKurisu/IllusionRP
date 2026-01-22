#ifndef ILLUSION_NORMAL_BUFFER_INCLUDED
#define ILLUSION_NORMAL_BUFFER_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#ifdef _DEFERRED_RENDERING_PATH
    TEXTURE2D_X_HALF(_GBuffer2); // encoded-normal    encoded-normal  encoded-normal  smoothness
#else
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    TEXTURE2D_X_HALF(_ForwardGBuffer); // smoothness
#endif

void GetNormal(uint2 positionSS, out float3 N)
{
#ifdef _DEFERRED_RENDERING_PATH
    half4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, positionSS);
    N = normalize(UnpackNormal(gbuffer2));
#else
    N = LoadSceneNormals(positionSS);
#endif
}
// We have fixed it in URP version since we store smoothness instead of perceptual roughness
void GetNormalAndPerceptualRoughness(uint2 positionSS, out float3 N, out float perceptualRoughness)
{
    #ifdef _DEFERRED_RENDERING_PATH
    half4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, positionSS);
    N = normalize(UnpackNormal(gbuffer2.xyz));
    float smoothness = gbuffer2.a;
    #else
    N = LoadSceneNormals(positionSS);
    float4 gbuffer = LOAD_TEXTURE2D_X(_ForwardGBuffer, positionSS);
    float smoothness = gbuffer.r;
    #endif
    perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
}
#endif