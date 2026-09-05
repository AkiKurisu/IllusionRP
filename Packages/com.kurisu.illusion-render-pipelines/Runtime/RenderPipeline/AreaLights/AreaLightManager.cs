using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.AreaLights
{
    // Reference: UnityEngine.Rendering.HighDefinition.HDUtils (distance fade helpers)
    internal static class HDUtils
    {
        internal static void GetScaleAndBiasForLinearDistanceFade(float fadeDistance, out float scale, out float bias)
        {
            // Fade with distance calculation is just a linear fade from 90% of fade distance to fade distance. 90% arbitrarily chosen but should work well enough.
            float distanceFadeNear = 0.9f * fadeDistance;
            scale = 1.0f / (fadeDistance - distanceFadeNear);
            bias = -distanceFadeNear / (fadeDistance - distanceFadeNear);
        }

        /// <summary>
        /// Compute the linear fade distance
        /// </summary>
        /// <param name="distanceToCamera">Distance from the object to fade from the camera</param>
        /// <param name="fadeDistance">Distance at witch the object is totally faded</param>
        /// <returns>Computed fade factor</returns>
        internal static float ComputeLinearDistanceFade(float distanceToCamera, float fadeDistance)
        {
            float scale;
            float bias;
            GetScaleAndBiasForLinearDistanceFade(fadeDistance, out scale, out bias);

            return 1.0f - Mathf.Clamp01(distanceToCamera * scale + bias);
        }
    }

    /// <summary>
    /// Builds the per camera rectangle light and shadow request data consumed by the area light shaders.
    /// </summary>
    // Reference: HDGpuLightsBuilder (Jobs.cs ConvertLightToGPUFormat) + HDShadowManager request / GPU data preparation
    internal sealed class AreaLightManager : IDisposable
    {
        public const int k_MaxAreaLights = 16;
        public const int k_MaxShadowRequests = 32;

        readonly AreaLightData[] m_LightDatas = new AreaLightData[k_MaxAreaLights];
        readonly HDShadowRequest[] m_ShadowRequests = new HDShadowRequest[k_MaxShadowRequests];
        readonly IllusionAdditionalLightData[] m_ShadowRequestLightDatas = new IllusionAdditionalLightData[k_MaxShadowRequests];
        readonly HDShadowData[] m_ShadowDatas = new HDShadowData[k_MaxShadowRequests];
        readonly Texture[] m_LightCookies = new Texture[k_MaxAreaLights];

        ComputeBuffer m_LightDataBuffer;
        ComputeBuffer m_ShadowDataBuffer;

        public readonly AreaLightShadowAtlas atlas = new();

        public ShaderVariablesAreaLights shaderVariables;

        public int lightCount { get; private set; }
        public int shadowRequestCount { get; private set; }
        public bool hasCookies { get; private set; }
        public HDShadowRequest[] shadowRequests => m_ShadowRequests;
        public AreaLightData[] lightDatas => m_LightDatas;
        public HDShadowData[] shadowDatas => m_ShadowDatas;
        public ComputeBuffer lightDataBuffer => m_LightDataBuffer;
        public ComputeBuffer shadowDataBuffer => m_ShadowDataBuffer;

        public AreaLightManager()
        {
            m_LightDataBuffer = new ComputeBuffer(k_MaxAreaLights, UnsafeUtility.SizeOf<AreaLightData>());
            m_ShadowDataBuffer = new ComputeBuffer(k_MaxShadowRequests, UnsafeUtility.SizeOf<HDShadowData>());
        }

        public void Dispose()
        {
            CoreUtils.SafeRelease(m_LightDataBuffer);
            CoreUtils.SafeRelease(m_ShadowDataBuffer);
            m_LightDataBuffer = null;
            m_ShadowDataBuffer = null;
        }

        public void Clear()
        {
            lightCount = 0;
            shadowRequestCount = 0;
            atlas.ClearRequests();
            hasCookies = false;
            Array.Clear(m_LightCookies, 0, m_LightCookies.Length);
            shaderVariables._AreaLightCount = 0;
        }

        private static Vector3 GetLightColor(in VisibleLight light) => new Vector3(light.finalColor.r, light.finalColor.g, light.finalColor.b);

        private static uint GetLightLayer(Light light)
        {
            if (light.TryGetComponent(out UniversalAdditionalLightData additionalLightData))
                return additionalLightData.renderingLayers;
            return unchecked((uint)light.renderingLayerMask);
        }

        // URP style: the additional data component is created on demand.
        private static IllusionAdditionalLightData GetOrAddAdditionalLightData(Light light)
        {
            if (!light.TryGetComponent(out IllusionAdditionalLightData additionalLightData))
                additionalLightData = light.gameObject.AddComponent<IllusionAdditionalLightData>();
            return additionalLightData;
        }

        // Buffer uploads happen on the command buffer of the globals pass, never inside a native render pass.
        public void UploadBuffers(CommandBuffer cmd)
        {
            if (lightCount > 0)
                cmd.SetBufferData(m_LightDataBuffer, m_LightDatas, 0, 0, lightCount);
            if (shadowRequestCount > 0)
                cmd.SetBufferData(m_ShadowDataBuffer, m_ShadowDatas, 0, 0, shadowRequestCount);
        }

        public void PrepareLights(UniversalCameraData cameraData, UniversalLightData lightData, AreaLighting settings,
            HDAreaShadowFilteringQuality areaShadowFilteringQuality, bool supportsShadows, AreaLightCookieManager cookieManager)
        {
            Clear();

            int atlasResolution = (int)settings.shadowAtlasResolution.value;
            var blurAlgorithm = areaShadowFilteringQuality == HDAreaShadowFilteringQuality.High
                ? AreaLightShadowAtlas.BlurAlgorithm.None
                : AreaLightShadowAtlas.BlurAlgorithm.EVSM;
            atlas.InitAtlas(atlasResolution, atlasResolution, settings.shadowAtlasDepthBits.value, blurAlgorithm, "Area Light Shadow Map Atlas");

            int maxShadowResolution = Mathf.Min((int)settings.maxShadowResolution.value, atlasResolution);
            int maxShadowRequests = Mathf.Min(settings.maxShadowRequests.value, k_MaxShadowRequests);
            Vector3 cameraPos = cameraData.worldSpaceCameraPos;
            bool usesReversedZBuffer = SystemInfo.usesReversedZBuffer;
            var visibleLights = lightData.visibleLights;

            if (cookieManager != null)
            {
                for (int lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
                {
                    VisibleLight visibleLight = visibleLights[lightIndex];
                    if (visibleLight.lightType != LightType.Rectangle || !visibleLight.light)
                        continue;

                    cookieManager.ReserveSpace(GetOrAddAdditionalLightData(visibleLight.light).areaLightCookie);
                }

                cookieManager.LayoutIfNeeded();
            }

            for (int lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                VisibleLight visibleLight = visibleLights[lightIndex];
                if (visibleLight.lightType != LightType.Rectangle)
                    continue;

                Light light = visibleLight.light;
                if (!light)
                    continue;

                if (lightCount >= k_MaxAreaLights)
                    break;

                IllusionAdditionalLightData lightRenderData = GetOrAddAdditionalLightData(light);
                float distanceToCamera = (visibleLight.GetPosition() - cameraPos).magnitude;
                float lightDistanceFade = HDUtils.ComputeLinearDistanceFade(distanceToCamera, lightRenderData.fadeDistance);

                bool contributesToLighting = (lightRenderData.lightDimmer > 0) && (lightRenderData.affectDiffuse || lightRenderData.affectSpecular);
                contributesToLighting = contributesToLighting && (lightDistanceFade > 0);
                if (!contributesToLighting)
                    continue;

                AreaLightData gpuLightData = default;
                ConvertLightToGPUFormat(light, visibleLight, lightRenderData, lightDistanceFade, distanceToCamera, ref gpuLightData);

                Texture cookie = lightRenderData.areaLightCookie;
                if (cookieManager != null && cookie != null
                    && cookie.width >= AreaLightCookieManager.k_MinCookieSize && cookie.height >= AreaLightCookieManager.k_MinCookieSize)
                {
                    gpuLightData.cookieMode = CookieMode.Clamp;
                    m_LightCookies[lightCount] = cookie;
                    hasCookies = true;
                }

                if (supportsShadows && light.shadows != LightShadows.None && shadowRequestCount < maxShadowRequests)
                {
                    int resolution = Mathf.Min(lightRenderData.shadowResolution, maxShadowResolution);
                    int requestIndex = atlas.ReserveResolution(new Vector2(resolution, resolution));
                    ref HDShadowRequest shadowRequest = ref m_ShadowRequests[requestIndex];
                    shadowRequest.InitDefault();
                    shadowRequest.lightIndex = lightIndex;
                    m_ShadowRequestLightDatas[requestIndex] = lightRenderData;
                    gpuLightData.shadowIndex = requestIndex;
                    shadowRequestCount++;
                }

                m_LightDatas[lightCount++] = gpuLightData;
            }

            // Assign a position to all the shadows in the atlas, and scale shadows if needed
            atlas.Layout();

            for (int requestIndex = 0; requestIndex < shadowRequestCount; requestIndex++)
            {
                ref HDShadowRequest shadowRequest = ref m_ShadowRequests[requestIndex];
                IllusionAdditionalLightData lightRenderData = m_ShadowRequestLightDatas[requestIndex];
                VisibleLight visibleLight = visibleLights[shadowRequest.lightIndex];

                shadowRequest.dynamicAtlasViewport = atlas.GetViewport(requestIndex);
                Vector2 viewportSize = shadowRequest.dynamicAtlasViewport.size;
                float forwardOffset = IllusionAdditionalLightData.GetAreaLightOffsetForShadows(visibleLight.areaSize, lightRenderData.areaLightShadowCone);

                Matrix4x4 view;
                Matrix4x4 deviceProjectionYFlip;
                Matrix4x4 projection;
                Matrix4x4 invViewProjection;
                Vector4 deviceProjection;
                ShadowSplitData splitData;

                HDShadowUtils.ExtractRectangleAreaLightData(visibleLight, forwardOffset, lightRenderData.areaLightShadowCone,
                    lightRenderData.shadowNearPlane, visibleLight.areaSize, viewportSize, lightRenderData.normalBias, usesReversedZBuffer,
                    out view, out invViewProjection, out projection,
                    out deviceProjection, out deviceProjectionYFlip,
                    out splitData);

                ref HDShadowCullingSplit hdSplit = ref shadowRequest.cullingSplit;
                hdSplit.splitIndex = 0;
                hdSplit.view = view;
                hdSplit.deviceProjectionMatrix = default;
                hdSplit.deviceProjectionYFlip = deviceProjectionYFlip;
                hdSplit.projection = projection;
                hdSplit.invViewProjection = invViewProjection;
                hdSplit.deviceProjection = deviceProjection;
                hdSplit.cullingSphere = splitData.cullingSphere;
                hdSplit.viewportSize = viewportSize;
                hdSplit.forwardOffset = forwardOffset;
                shadowRequest.splitData = splitData;

                HDGpuLightsBuilder.SetAreaRequestSettings(ref shadowRequest, visibleLight, forwardOffset, cameraPos,
                    invViewProjection, projection, viewportSize, shadowRequest.lightIndex,
                    areaShadowFilteringQuality, lightRenderData, 0);

                m_ShadowDatas[requestIndex] = atlas.CreateShadowData(ref shadowRequest);
            }

            shaderVariables._AreaLightCount = lightCount;
            shaderVariables._AreaShadowAtlasSize = new Vector4(atlas.width, atlas.height, 1.0f / atlas.width, 1.0f / atlas.height);
            shaderVariables._CachedAreaShadowAtlasSize = shaderVariables._AreaShadowAtlasSize;
            shaderVariables._CookieAtlasSize = cookieManager != null ? cookieManager.GetCookieAtlasSize() : Vector4.zero;
            shaderVariables._CookieAtlasData = cookieManager != null ? cookieManager.GetCookieAtlasDatas() : Vector4.zero;
        }

        // Runs before UploadBuffers on the same command buffer, the atlas blits need a command buffer outside native render passes.
        public void FetchCookies(CommandBuffer cmd, AreaLightCookieManager cookieManager)
        {
            for (int i = 0; i < lightCount; i++)
            {
                if (m_LightCookies[i] != null)
                    m_LightDatas[i].cookieScaleOffset = cookieManager.FetchAreaCookie(cmd, m_LightCookies[i]);
            }
        }

        // Reference: HDGpuLightsBuilder.CreateGpuLightDataJob.ConvertLightToGPUFormat, rectangle light path
        private static void ConvertLightToGPUFormat(Light light, in VisibleLight visibleLight, IllusionAdditionalLightData lightRenderData,
            float lightDistanceFade, float distanceToCamera, ref AreaLightData lightData)
        {
            lightData.lightLayers = GetLightLayer(light);
            lightData.lightType = GPULightType.Rectangle;

            var visibleLightAxisAndPosition = visibleLight.GetAxisAndPosition();
            lightData.positionRWS = visibleLightAxisAndPosition.Position;
            lightData.range = visibleLight.range;

            if (lightRenderData.applyRangeAttenuation)
            {
                lightData.rangeAttenuationScale = 1.0f / (visibleLight.range * visibleLight.range);
                lightData.rangeAttenuationBias = 1.0f;
            }
            else
            {
                // Solve f(x) = b - (a * x)^2 where x = (d/r)^2.
                // f(0) = huge -> b = huge.
                // f(1) = 0    -> huge - a^2 = 0 -> a = sqrt(huge).
                const float hugeValue = 16777216.0f;
                const float sqrtHuge = 4096.0f;
                lightData.rangeAttenuationScale = sqrtHuge / (visibleLight.range * visibleLight.range);
                lightData.rangeAttenuationBias = hugeValue;
            }

            float shapeWidthVal = visibleLight.areaSize.x;
            float shapeHeightVal = visibleLight.areaSize.y;

            lightData.color = GetLightColor(visibleLight);
            lightData.forward = visibleLightAxisAndPosition.Forward;
            lightData.up = visibleLightAxisAndPosition.Up;
            lightData.right = visibleLightAxisAndPosition.Right;

            float shapeRadiusVal = lightRenderData.shapeRadius;

            lightData.size = new Vector4(shapeWidthVal, shapeHeightVal, Mathf.Cos(lightRenderData.barnDoorAngle * Mathf.PI / 180.0f), lightRenderData.barnDoorLength);

            var lightDimmerVal = lightRenderData.lightDimmer;
            lightData.diffuseDimmer = lightDistanceFade * (lightRenderData.affectDiffuse ? lightDimmerVal : 0);
            lightData.specularDimmer = lightDistanceFade * (lightRenderData.affectSpecular ? lightDimmerVal : 0);

            lightData.cookieMode = CookieMode.None;
            lightData.shadowIndex = -1;
            lightData.screenSpaceShadowIndex = -1;

            var lightsShadowFadeDistance = lightRenderData.shadowFadeDistance;
            var shadowDimmerVal = lightRenderData.shadowDimmer;
            float shadowDistanceFade = HDUtils.ComputeLinearDistanceFade(distanceToCamera, lightsShadowFadeDistance);
            lightData.shadowDimmer = shadowDistanceFade * shadowDimmerVal;

            // We want to have a colored penumbra if the flag is on and the color is not gray
            var shadowTintVal = lightRenderData.shadowTint;
            bool penumbraTintVal = lightRenderData.penumbraTint && ((shadowTintVal.r != shadowTintVal.g) || (shadowTintVal.g != shadowTintVal.b));
            lightData.penumbraTint = penumbraTintVal ? 1.0f : 0.0f;
            if (penumbraTintVal)
                lightData.shadowTint = new Vector3(Mathf.Pow(shadowTintVal.r, 2.2f), Mathf.Pow(shadowTintVal.g, 2.2f), Mathf.Pow(shadowTintVal.b, 2.2f));
            else
                lightData.shadowTint = new Vector3(shadowTintVal.r, shadowTintVal.g, shadowTintVal.b);

            //Value of max smoothness is derived from Radius. Formula results from eyeballing. Radius of 0 results in 1 and radius of 2.5 results in 0.
            float maxSmoothness = Mathf.Clamp01(1.1725f / (1.01f + Mathf.Pow(1.0f * (shapeRadiusVal + 0.1f), 2f)) - 0.15f);
            // Value of max smoothness is from artists point of view, need to convert from perceptual smoothness to roughness
            lightData.minRoughness = (1.0f - maxSmoothness) * (1.0f - maxSmoothness);

            // use -1 to say that we don't use shadow mask
            lightData.shadowMaskSelector = Vector4.zero;
            lightData.shadowMaskSelector.x = -1.0f;
            lightData.nonLightMappedOnly = 0;
        }
    }
}
