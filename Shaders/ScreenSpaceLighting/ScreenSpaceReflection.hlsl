#ifndef SCREEN_SPACE_REFLECTION_INCLUDED
#define SCREEN_SPACE_REFLECTION_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/ScreenSpaceLighting/ScreenSpaceReflectionCommon.hlsl"

#ifdef SSR_TRACE
half4 FragSSRLinearVS(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, input.texcoord, 0).r;

    UNITY_BRANCH
    if (IsInfinityFar(rawDepth))
        return half4(0, 0, 0, 0);

    uint2 positionSS = (uint2)(input.texcoord * _ScreenSize.xy);
    // TODO: Add stencil prepass in deferred rendering path
#ifndef _DEFERRED_RENDERING_PATH
    bool doesntReceiveSSR = false;
    uint stencilValue = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, positionSS.xy));
    doesntReceiveSSR = (stencilValue & STENCIL_USAGE_IS_SSR) == 0;
    if (doesntReceiveSSR)
    {
        return half4(0, 0, 0, 0);
    }
#endif

    float3 normalWS = 0;
    float smoothness;
#ifdef _DEFERRED_RENDERING_PATH
    half4 gbuffer2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_PointClamp, input.texcoord, 0);
    normalWS = normalize(UnpackNormal(gbuffer2));
    smoothness = gbuffer2.a;
#else
    half4 gbuffer2 = SAMPLE_TEXTURE2D_X_LOD(_ForwardGBuffer, sampler_PointClamp, input.texcoord, 0);
    normalWS = SampleSceneNormals(input.texcoord);
    smoothness = gbuffer2.r;
#endif
    float3 positionWS = ComputeWorldSpacePosition(input.texcoord.xy, rawDepth, UNITY_MATRIX_I_VP);
    float3 reflectRayWS = normalize(reflect((positionWS - _WorldSpaceCameraPos), normalWS));
    float3 reflectRayVS = TransformWorldToViewDir(reflectRayWS);
    float3 rayOrigin = TransformWorldToView(positionWS);
    //handle view space in right-handed coordinates
    reflectRayVS.z *= -1;
    rayOrigin.z *= -1;

    float2 reflectUV = ComputeNormalizedDeviceCoordinates(positionWS, UNITY_MATRIX_VP);
    float3 viewDirWS = normalize(float3(positionWS.xyz) - _WorldSpaceCameraPos);
    
    float viewReflectDot = saturate(dot(viewDirWS, reflectRayWS));
    float oneMinusViewReflectDot = sqrt(1 - viewReflectDot);
    _StepSize /= oneMinusViewReflectDot;


    float depthDelta = 0;
    float3 samplePositionVS = rayOrigin;
    
    if (smoothness <= 0)
    {
        return half4(0, 0, 0, 0);
    }
    
    float maxRayLength = SSR_STEPS * _StepSize;
    float maxDist = lerp(min(rayOrigin.z, maxRayLength), maxRayLength, viewReflectDot);
    float numSteps_f = maxDist / _StepSize;
    float totalSteps = max(numSteps_f, 0);

    //trace in view space
    bool hasTraceHit = LinearTraceRayVS(_CameraDepthTexture, sampler_PointClamp, totalSteps, _StepSize, float2(0, 0),
                                        rawDepth, depthDelta, reflectUV, samplePositionVS, reflectRayVS);
    return half4(reflectUV, 0, 0);
}

half4 FragSSRLinearSS(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, input.texcoord, 0).r;

    UNITY_BRANCH
    if (IsInfinityFar(rawDepth))
        return half4(0, 0, 0, 0);

    uint2 positionSS = (uint2)(input.texcoord * _ScreenSize.xy);
    // TODO: Add stencil prepass in deferred rendering path
#ifndef _DEFERRED_RENDERING_PATH
    bool doesntReceiveSSR = false;
    uint stencilValue = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, positionSS.xy));
    doesntReceiveSSR = (stencilValue & STENCIL_USAGE_IS_SSR) == 0;
    if (doesntReceiveSSR)
    {
        return half4(0, 0, 0, 0);
    }
#endif

    float3 normalWS = 0;
#ifdef _DEFERRED_RENDERING_PATH
    half4 gbuffer2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_PointClamp, input.texcoord, 0);
    normalWS = normalize(UnpackNormal(gbuffer2.xyz));
#else
    normalWS = SampleSceneNormals(input.texcoord);
#endif
    float3 positionWS = ComputeWorldSpacePosition(input.texcoord.xy, rawDepth, UNITY_MATRIX_I_VP);
    float3 reflectRayWS = normalize(reflect((positionWS - _WorldSpaceCameraPos), normalWS));
    float3 reflectRayVS = TransformWorldToViewDir(reflectRayWS);
    float3 rayOrigin = TransformWorldToView(positionWS);

    float2 ditherUV = input.texcoord.xy * _ScreenParams.xy;
    uint index = (uint(ditherUV.x) % 4) * 4 + uint(ditherUV.y) % 4;
    float jitter = 1 + (1 - dither[index]);
    // 64 - 512
    float traceDistance = 200;
    float3 hitPointVS = rayOrigin;
    float2 reflectUV = 0; // ComputeNormalizedDeviceCoordinates(positionWS, UNITY_MATRIX_VP);
    float stepCount = 0;
    float stepSize = _StepSize * 30;
    bool traceHit = Linear2D_Trace(_CameraDepthTexture, sampler_PointClamp, rayOrigin, reflectRayVS,
                                      _SSR_ProjectionMatrix, _ScreenSize.xy, jitter, SSR_STEPS,
                                      LINEAR_TRACE_2D_THICKNESS,
                                      traceDistance, reflectUV, stepSize, hitPointVS, stepCount);
    reflectUV /= _ScreenSize.xy;
    return half4(reflectUV, 0, 0);
}

half4 FragSSRHizSS(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    uint2 positionSS = (uint2)(input.texcoord * _ScreenSize.xy);
    return ScreenSpaceReflection(positionSS);
}
#endif // defined(SSR_TRACE)

#ifdef SSR_REPROJECT
half4 FragSSRReprojection(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    uint2 positionSS = (uint2)(input.texcoord * _ScreenSize.xy);
    return ScreenSpaceReflectionReprojection(positionSS);
}
#endif // defined(SSR_REPROJECT)
#endif // defined(SCREEN_SPACE_REFLECTION_INCLUDED)