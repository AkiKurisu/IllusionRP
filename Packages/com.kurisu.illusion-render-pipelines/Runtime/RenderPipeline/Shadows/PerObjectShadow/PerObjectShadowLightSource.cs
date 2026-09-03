using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    /// <summary>
    /// Selects the directional light used to render per-object shadows for this camera.
    /// A null source follows the camera's current URP main light.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PerObjectShadowLightSource : MonoBehaviour
    {
        [SerializeField]
        private Light source;

        public Light Source
        {
            get => source;
            set => source = value;
        }
    }

    /// <summary>
    /// Excludes the Per-Object rendering layer from the URP Main Light shadow caster cull when a
    /// different directional light owns Per-Object shadows for the camera.
    /// </summary>
    internal sealed class PerObjectShadowMainLightCasterScope : IDisposable
    {
        private bool enabled;
        private RenderingLayerMask casterRenderingLayers;
        private Camera activeCamera;
        private LightShadowState activeState;
        private bool hasActiveScope;

        private readonly struct LightShadowState
        {
            public readonly Light Light;
            public readonly UniversalAdditionalLightData AdditionalData;
            public readonly bool CustomShadowLayers;
            public readonly RenderingLayerMask ShadowRenderingLayers;
            public readonly int LightRenderingLayerMask;

            public LightShadowState(Light light, UniversalAdditionalLightData additionalData)
            {
                Light = light;
                AdditionalData = additionalData;
                CustomShadowLayers = additionalData.customShadowLayers;
                ShadowRenderingLayers = additionalData.shadowRenderingLayers;
                LightRenderingLayerMask = light.renderingLayerMask;
            }
        }

        public PerObjectShadowMainLightCasterScope(bool enabled, RenderingLayerMask renderingLayers)
        {
            this.enabled = enabled;
            casterRenderingLayers = renderingLayers;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        public void UpdateSettings(bool isEnabled, RenderingLayerMask renderingLayers)
        {
            enabled = isEnabled;
            casterRenderingLayers = renderingLayers;
            if (!enabled)
            {
                RestoreActiveScope();
            }
        }

        public void Dispose()
        {
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RestoreActiveScope();
        }

        public void BeginCameraCull(Camera camera, ScriptableRenderer renderer)
        {
            // Recover any scope whose end callback was skipped before applying the next camera's
            // renderer-owned culling state.
            RestoreActiveScope();

            uint casterMask = casterRenderingLayers;
            if (!enabled || casterMask == 0 || !camera ||
                !camera.TryGetComponent(out PerObjectShadowLightSource selector) ||
                !selector.isActiveAndEnabled)
            {
                return;
            }

            if (UniversalRenderingUtility.GetRenderingModeActual(renderer) != RenderingMode.ForwardPlus)
                return;

            Light source = selector.Source;
            Light mainLight = RenderSettings.sun;
            if (!PerObjectShadowLightData.IsUsableDirectional(source) ||
                !PerObjectShadowLightData.IsUsableDirectional(mainLight) || source == mainLight ||
                !IsVisibleToCamera(camera, source) || !IsVisibleToCamera(camera, mainLight) ||
                !mainLight.TryGetComponent(out UniversalAdditionalLightData additionalData))
            {
                return;
            }

            var state = new LightShadowState(mainLight, additionalData);
            RenderingLayerMask effectiveShadowLayers = state.CustomShadowLayers
                ? state.ShadowRenderingLayers
                : additionalData.renderingLayers;
            RenderingLayerMask filteredLayers = (uint)effectiveShadowLayers & ~casterMask;

            // URP snapshots Light.renderingLayerMask during camera culling. This scope must therefore
            // begin from the owning renderer's pre-cull callback rather than from a ScriptableRenderPass.
            additionalData.shadowRenderingLayers = filteredLayers;
            additionalData.customShadowLayers = true;
            activeCamera = camera;
            activeState = state;
            hasActiveScope = true;
        }

        private void EndCameraRendering(ScriptableRenderContext _, Camera camera)
        {
            if (camera == activeCamera)
            {
                RestoreActiveScope();
            }
        }

        private void RestoreActiveScope()
        {
            if (!hasActiveScope)
            {
                return;
            }

            Restore(activeState);
            activeCamera = null;
            activeState = default;
            hasActiveScope = false;
        }

        private static void Restore(in LightShadowState state)
        {
            if (!state.AdditionalData || !state.Light)
            {
                return;
            }

            state.AdditionalData.shadowRenderingLayers = state.ShadowRenderingLayers;
            state.AdditionalData.customShadowLayers = state.CustomShadowLayers;
            state.Light.renderingLayerMask = state.LightRenderingLayerMask;
        }

        private static bool IsVisibleToCamera(Camera camera, Light light)
        {
            return light.forceVisible || (camera.cullingMask & (1 << light.gameObject.layer)) != 0;
        }
    }

    public enum PerObjectShadowLightMode
    {
        Disabled = 0,
        Main = 1,
        AdditionalDirectional = 2
    }

    public readonly struct PerObjectShadowLightData
    {
        public readonly PerObjectShadowLightMode Mode;
        public readonly int AdditionalLightIndex;
        public readonly VisibleLight VisibleLight;

        public bool IsValid => Mode != PerObjectShadowLightMode.Disabled;

        private PerObjectShadowLightData(PerObjectShadowLightMode mode, int additionalLightIndex,
            VisibleLight visibleLight)
        {
            Mode = mode;
            AdditionalLightIndex = additionalLightIndex;
            VisibleLight = visibleLight;
        }

        public static PerObjectShadowLightData Resolve(UniversalCameraData cameraData,
            UniversalLightData lightData, bool allowCameraOverride, bool supportsAdditionalDirectional)
        {
            var selector = allowCameraOverride
                ? cameraData.camera.GetComponent<PerObjectShadowLightSource>()
                : null;
            Light explicitSource = selector && selector.isActiveAndEnabled ? selector.Source : null;
            bool hasExplicitSource = explicitSource;

            if (!hasExplicitSource)
            {
                int mainIndex = lightData.mainLightIndex;
                if (mainIndex < 0)
                    return default;

                VisibleLight main = lightData.visibleLights[mainIndex];
                return IsUsableDirectional(main.light)
                    ? new PerObjectShadowLightData(PerObjectShadowLightMode.Main, -1, main)
                    : default;
            }

            if (!IsUsableDirectional(explicitSource))
                return default;

            int visibleIndex = FindVisibleLight(lightData, explicitSource);
            if (visibleIndex < 0)
                return default;

            VisibleLight visibleLight = lightData.visibleLights[visibleIndex];
            if (visibleIndex == lightData.mainLightIndex)
                return new PerObjectShadowLightData(PerObjectShadowLightMode.Main, -1, visibleLight);

            if (!supportsAdditionalDirectional)
                return default;

            int additionalIndex = GetAdditionalLightBufferIndex(lightData, visibleIndex);
            return additionalIndex >= 0 && additionalIndex < lightData.additionalLightsCount
                ? new PerObjectShadowLightData(PerObjectShadowLightMode.AdditionalDirectional,
                    additionalIndex, visibleLight)
                : default;
        }

        internal static bool IsUsableDirectional(Light light)
        {
            return light && light.type == LightType.Directional && light.isActiveAndEnabled &&
                   light.shadows != LightShadows.None;
        }

        private static int FindVisibleLight(UniversalLightData lightData, Light source)
        {
            for (int i = 0; i < lightData.visibleLights.Length; i++)
            {
                if (lightData.visibleLights[i].light == source)
                    return i;
            }

            return -1;
        }

        // Forward+ uploads visible lights in order while omitting the main light.
        private static int GetAdditionalLightBufferIndex(UniversalLightData lightData, int visibleLightIndex)
        {
            int additionalIndex = 0;
            for (int i = 0; i < lightData.visibleLights.Length; i++)
            {
                if (i == lightData.mainLightIndex)
                    continue;
                if (i == visibleLightIndex)
                    return additionalIndex;
                additionalIndex++;
            }

            return -1;
        }
    }
}
