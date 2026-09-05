using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.AreaLights
{
    // Reference: UnityEngine.Rendering.HighDefinition.HDShadowManager (area light slice)
    internal struct HDShadowCullingSplit
    {
        public Matrix4x4 view;
        public Matrix4x4 deviceProjectionMatrix;
        public Matrix4x4 deviceProjectionYFlip; // Use the y flipped device projection matrix as light projection matrix
        public Matrix4x4 projection;
        public Matrix4x4 invViewProjection;
        public Vector4 deviceProjection;
        public Vector4 cullingSphere;
        public Vector2 viewportSize;
        public float forwardOffset;
        public int splitIndex;
    }

    internal struct HDShadowResolutionRequest
    {
        public Rect             dynamicAtlasViewport;
        public Vector2          resolution;
    }

    // @IllusionRP: flat fields instead of the packed flags / cached atlas plumbing, area atlas is always dynamic.
    internal struct HDShadowRequest
    {
        public HDShadowCullingSplit cullingSplit;
        public HDShadowData cachedShadowData;
        public Matrix4x4 shadowToWorld;
        public Vector4 zBufferParam;
        public Vector4 evsmParams;
        // Warning: these viewport fields are updated by ProcessShadowRequests and are invalid before
        public Rect dynamicAtlasViewport;

        public Vector3 position;

        // TODO: Remove these field once scriptable culling is here (currently required by ScriptableRenderContext.DrawShadows)
        public int lightIndex;
        // end

        public float normalBias;
        public float worldTexelSize;
        public float slopeBias;

        // PCSS parameter
        public float shadowSoftness;

        public float minFilterSize;

        // PCSS parameters
        public byte blockerSampleCount;
        public byte filterSampleCount;

        public bool zClip;
        public bool isValid;

        public ShadowSplitData splitData;
        public bool isInCachedAtlas => false;

        public void InitDefault()
        {
            cullingSplit = default;
            shadowToWorld = default;
            position = default;
            zBufferParam = default;
            dynamicAtlasViewport = default;
            zClip = default;
            lightIndex = default;
            normalBias = default;
            worldTexelSize = default;
            slopeBias = default;
            shadowSoftness = default;
            blockerSampleCount = default;
            filterSampleCount = default;
            minFilterSize = default;
            evsmParams = default;
            cachedShadowData = default;
            splitData = default;
            isValid = true;
        }
    }

    // Reference: UnityEngine.Rendering.HighDefinition.HDGpuLightsBuilder (LightLoop.cs, area light slice)
    internal static class HDGpuLightsBuilder
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float4 GetZBufferParam(in VisibleLight visibleLight, float nearPlaneForZBufferParam)
        {
            // zBuffer param to reconstruct depth position (for transmission)
            float f = visibleLight.range;
            float n = nearPlaneForZBufferParam;
            return new float4((f-n)/n, 1.0f, (f-n)/(n*f), 1.0f/f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float GetNonDirectionalSoftness(in HDShadowRequest shadowRequest, IllusionAdditionalLightData additionalLightData)
        {
            // This derivation has been fitted with quartic regression checking against raytracing reference and with a resolution of 512
            float x = additionalLightData.shapeRadius * additionalLightData.softnessScale;
            float x2 = x * x;
            float softness = 0.02403461f + 3.452916f * x - 1.362672f * x2 + 0.6700115f * x2 * x + 0.2159474f * x2 * x2;
            softness /= 100.0f;

            var viewportWidth = shadowRequest.dynamicAtlasViewport.width;
            softness *= (viewportWidth / 512);  // Make it resolution independent whereas the baseline is 512

            return softness;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float GetBaseBias(bool isHighQuality, float softness)
        {
            // Bias
            // This base bias is a good value if we expose a [0..1] since values within [0..5] are empirically shown to be sensible for the slope-scale bias with the width of our PCF.
            float baseBias = 5.0f;
            // If we are PCSS, the blur radius can be quite big, hence we need to tweak up the slope bias
            if (isHighQuality && softness > 0.01f)
            {
                // maxBaseBias is an empirically set value, also the lerp stops at a shadow softness of 0.05, then is clamped.
                float maxBaseBias = 18.0f;
                baseBias = Mathf.Lerp(baseBias, maxBaseBias, Mathf.Min(1.0f, (softness * 100) / 5));
            }

            return baseBias;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector3 GetPositionFromVisibleLight(in VisibleLight visibleLight, Vector3 cameraPos, float forwardOffset, int shaderConfigCameraRelativeRendering)
        {
            var lightAxisAndPosition = visibleLight.GetAxisAndPosition();
            Vector3 position = lightAxisAndPosition.Position + lightAxisAndPosition.Forward * forwardOffset;
            if (shaderConfigCameraRelativeRendering != 0)
                position -= cameraPos;

            return position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetAreaRequestSettings(ref HDShadowRequest shadowRequest,
            in VisibleLight visibleLight, float forwardOffset,
            Vector3 cameraPos, Matrix4x4 invViewProjection, Matrix4x4 projection, Vector2 viewportSize, int lightIndex,
            HDAreaShadowFilteringQuality areaFilteringQuality,
            IllusionAdditionalLightData additionalLightData, int shaderConfigCameraRelativeRendering)
        {
            float nearPlane = additionalLightData.shadowNearPlane;
            float4 zBufferParam = GetZBufferParam(visibleLight, nearPlane);

            Vector3 position = GetPositionFromVisibleLight(visibleLight, cameraPos, forwardOffset, shaderConfigCameraRelativeRendering);
            float softness = GetNonDirectionalSoftness(shadowRequest, additionalLightData);
            float baseBias = GetBaseBias(areaFilteringQuality == HDAreaShadowFilteringQuality.High, softness);

            SetCommonShadowRequestSettings(ref shadowRequest, cameraPos, invViewProjection, projection,
                viewportSize, lightIndex, additionalLightData, shaderConfigCameraRelativeRendering,
                zBufferParam, softness, position, baseBias,
                false, true);

            // We transform it to base two for faster computation.
            // So e^x = 2^y where y = x * log2 (e)
            const float log2e = 1.44269504089f;
            shadowRequest.evsmParams.x = additionalLightData.evsmExponent * log2e;
            shadowRequest.evsmParams.y = additionalLightData.evsmLightLeakBias;
            shadowRequest.evsmParams.z = additionalLightData.evsmVarianceBias;
            shadowRequest.evsmParams.w = additionalLightData.evsmBlurPasses;
        }

        // @IllusionRP: frustum planes (tessellation clipping) are not needed by IllusionRP shaders and are skipped.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetCommonShadowRequestSettings(ref HDShadowRequest shadowRequest,
            Vector3 cameraPos, Matrix4x4 invViewProjection, Matrix4x4 projection, Vector2 viewportSize, int lightIndex,
            IllusionAdditionalLightData additionalLightData, int shaderConfigCameraRelativeRendering,
            float4 zBufferParam, float softness, Vector3 position, float baseBias, bool hasOrthoMatrix, bool zClip)
        {
            shadowRequest.zBufferParam = zBufferParam;
            shadowRequest.worldTexelSize = 2.0f / shadowRequest.cullingSplit.deviceProjectionYFlip.m00 / viewportSize.x * Mathf.Sqrt(2.0f);
            shadowRequest.normalBias = additionalLightData.normalBias;

            shadowRequest.position = position;

            shadowRequest.shadowToWorld = invViewProjection.transpose;
            shadowRequest.zClip = zClip;
            shadowRequest.lightIndex = lightIndex;

            shadowRequest.slopeBias = HDShadowUtils.GetSlopeBias(baseBias, additionalLightData.slopeBias);

            // Shadow algorithm parameters
            shadowRequest.shadowSoftness = softness;
            shadowRequest.blockerSampleCount = (byte)additionalLightData.blockerSampleCount;
            shadowRequest.filterSampleCount = (byte)additionalLightData.filterSampleCount;
            shadowRequest.minFilterSize = additionalLightData.minFilterSize * 0.001f; // This divide by 1000 is here to have a range [0...1] exposed to user
        }
    }
}
