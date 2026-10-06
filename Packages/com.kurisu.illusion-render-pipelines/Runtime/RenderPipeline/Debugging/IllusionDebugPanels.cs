using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    internal sealed partial class IllusionDebugPanels : IDebugData
    {
        public const string FeaturesPanelName = "Illusion Features";

        public const string DebugPanelName = "Illusion Debug";

        public const string PathTracingPanelName = "Illusion Path Tracing";

        private const int GroupIndex = 100;

        private static readonly HashSet<IllusionRendererFeature> Features = new();

        private static readonly IllusionRuntimeRenderingConfig Defaults = new();

        private static IllusionDebugPanels _instance;

        private readonly List<DebugUI.Panel> _panels = new();

        private readonly List<Action> _resets = new();

        private static IllusionRuntimeRenderingConfig Config => IllusionRuntimeRenderingConfig.Get();

        private void Build()
        {
            AddPanel(FeaturesPanelName, CreateFeatureWidgets());
            AddPanel(DebugPanelName, CreateDebugWidgets());
            AddPanel(PathTracingPanelName, CreatePathTracingWidgets());
            DebugManager.instance.RegisterData(this);
        }

        public static void Register(IllusionRendererFeature feature)
        {
            Features.Add(feature);
            if (_instance != null)
                return;

            var panels = new IllusionDebugPanels();
            try
            {
                panels.Build();
                _instance = panels;
            }
            catch (Exception exception)
            {
                panels.Remove();
                Debug.LogException(exception);
            }
        }

        public static void Unregister(IllusionRendererFeature feature)
        {
            Features.Remove(feature);
            if (Features.Count > 0 || _instance == null)
                return;
            _instance.Remove();
            _instance = null;
        }

        public Action GetReset() => () =>
        {
            foreach (var reset in _resets)
                reset();
        };

        private void AddPanel(string displayName, IEnumerable<DebugUI.Widget> widgets)
        {
            var panel = DebugManager.instance.GetPanel(displayName, true, GroupIndex, true);
            _panels.Add(panel);
            foreach (var widget in widgets)
                panel.children.Add(widget);
        }

        private void Remove()
        {
            DebugManager.instance.UnregisterData(this);
            foreach (var panel in _panels)
                DebugManager.instance.RemovePanel(panel);
            _panels.Clear();
        }

        private IEnumerable<DebugUI.Widget> CreateFeatureWidgets()
        {
            yield return Section("Lighting",
                Toggle("Screen Space Reflection", "Screen space reflections.",
                    c => c.EnableScreenSpaceReflection, (c, v) => c.EnableScreenSpaceReflection = v),
                Toggle("Transparent Screen Space Reflection", "Screen space reflections on supported transparent water shaders.",
                    c => c.EnableTransparentScreenSpaceReflection, (c, v) => c.EnableTransparentScreenSpaceReflection = v),
                Toggle("Screen Space Refraction", "Copy the opaque scene color for refractive shaders.",
                    c => c.EnableScreenSpaceRefraction, (c, v) => c.EnableScreenSpaceRefraction = v),
                Toggle("Screen Space Global Illumination", "Screen space global illumination.",
                    c => c.EnableScreenSpaceGlobalIllumination, (c, v) => c.EnableScreenSpaceGlobalIllumination = v),
                Toggle("PRT Global Illumination", "Precomputed radiance transfer global illumination.",
                    c => c.EnablePrecomputedRadianceTransferGlobalIllumination, (c, v) => c.EnablePrecomputedRadianceTransferGlobalIllumination = v),
                Toggle("Screen Space Ambient Occlusion", "Screen space ambient occlusion.",
                    c => c.EnableScreenSpaceAmbientOcclusion, (c, v) => c.EnableScreenSpaceAmbientOcclusion = v),
                Toggle("Wet Surface Decals", "Project wet and dry surface decals in Forward rendering.",
                    c => c.EnableWetSurfaceDecals, (c, v) => c.EnableWetSurfaceDecals = v),
                Toggle("Area Lights", "Rectangle area lights.",
                    c => c.EnableAreaLights, (c, v) => c.EnableAreaLights = v));
            yield return Section("Shadows",
                Toggle("Contact Shadows", "Screen space contact shadows.",
                    c => c.EnableContactShadows, (c, v) => c.EnableContactShadows = v),
                Toggle("Percentage Closer Soft Shadows", "Percentage closer soft shadows.",
                    c => c.EnablePercentageCloserSoftShadows, (c, v) => c.EnablePercentageCloserSoftShadows = v));
            yield return Section("Post Processing",
                Toggle("Volumetric Fog", "Volumetric fog.",
                    c => c.EnableVolumetricFog, (c, v) => c.EnableVolumetricFog = v),
                Toggle("Convolution Bloom", "Convolution bloom.",
                    c => c.EnableConvolutionBloom, (c, v) => c.EnableConvolutionBloom = v),
                Toggle("Sun Shafts", "Screen space sun shafts.",
                    c => c.EnableSunShafts, (c, v) => c.EnableSunShafts = v));
            yield return Section("Neural Rendering and Path Tracing",
                Toggle("DLSS Neural Rendering", "The optional full-resolution DLSS Neural Rendering pass.",
                    c => c.EnableDLSSNeuralRendering, (c, v) => c.EnableDLSSNeuralRendering = v),
                Toggle("Path Tracing", "Path tracing for cameras with an active Path Tracing Volume.",
                    c => c.EnablePathTracing, (c, v) => c.EnablePathTracing = v));
            yield return Section("Graphics API",
                Toggle("Compute Shader", "Prefer compute shader passes where available.",
                    c => c.EnableComputeShader, (c, v) => c.EnableComputeShader = v),
                Toggle("Stencil VRS", "Stencil based variable rate shading.",
                    c => c.EnableVrs, (c, v) => c.EnableVrs = v));
        }

        private IEnumerable<DebugUI.Widget> CreateDebugWidgets()
        {
            yield return Section("Debug Views",
                Toggle("Motion Vectors", "Visualize motion vectors.",
                    c => c.EnableMotionVectorsDebug, (c, v) => c.EnableMotionVectorsDebug = v),
                Toggle("Screen Space Reflection", "Visualize screen space reflections.",
                    c => c.EnableScreenSpaceReflectionDebug, (c, v) => c.EnableScreenSpaceReflectionDebug = v),
                Toggle("Transparent Screen Space Reflection", "Visualize screen space reflections on transparent water.",
                    c => c.EnableTransparentScreenSpaceReflectionDebug, (c, v) => c.EnableTransparentScreenSpaceReflectionDebug = v),
                Toggle("Per Object Shadow", "Visualize per-object shadows.",
                    c => c.EnablePerObjectShadowDebug, (c, v) => c.EnablePerObjectShadowDebug = v),
                Toggle("Stencil VRS", "Visualize the stencil variable rate shading mask.",
                    c => c.EnableVrsDebug, (c, v) => c.EnableVrsDebug = v),
                Toggle("PRT Cascades", "Color pixels by the PRT camera cascades that light them (red, yellow, green, blue from fine to coarse; grey falls back).",
                    c => c.EnablePRTCascadesDebug, (c, v) => c.EnablePRTCascadesDebug = v),
                Toggle("Area Light Shadow Atlas", "Overlay the area light shadow atlas.",
                    c => c.EnableAreaLightShadowAtlasDebug, (c, v) => c.EnableAreaLightShadowAtlasDebug = v),
                Indented("AreaLightShadowAtlasRange", () => !Config.EnableAreaLightShadowAtlasDebug,
                    Float("Min Value", "Atlas value mapped to black.",
                        c => c.AreaLightShadowAtlasDebugMinValue, (c, v) => c.AreaLightShadowAtlasDebugMinValue = v),
                    Float("Max Value", "Atlas value mapped to white.",
                        c => c.AreaLightShadowAtlasDebugMaxValue, (c, v) => c.AreaLightShadowAtlasDebugMaxValue = v)),
                Choice("Screen Space Shadow", "Screen space shadow debug view.",
                    c => c.ScreenSpaceShadowDebugMode, (c, v) => c.ScreenSpaceShadowDebugMode = v));
            yield return Section("Exposure",
                Choice("Debug Mode", "Exposure debug view.",
                    c => c.ExposureDebugMode, (c, v) => c.ExposureDebugMode = v),
                Indented("ExposureOptions", () => Config.ExposureDebugMode == ExposureDebugMode.None,
                    Toggle("Center Around Middle Grey", "Center the histogram around the middle-grey point.",
                        c => c.CenterHistogramAroundMiddleGrey, (c, v) => c.CenterHistogramAroundMiddleGrey = v),
                    Toggle("Display Scene Overlay", "Show an on-scene overlay for excluded pixels.",
                        c => c.DisplayOnSceneOverlay, (c, v) => c.DisplayOnSceneOverlay = v),
                    Toggle("Histogram RGB Mode", "Display the histogram per RGB channel.",
                        c => c.DisplayFinalImageHistogramAsRGB, (c, v) => c.DisplayFinalImageHistogramAsRGB = v),
                    Toggle("Display Mask Only", "Show only the mask in the picture-in-picture.",
                        c => c.DisplayMaskOnly, (c, v) => c.DisplayMaskOnly = v)));
            yield return Section("DLSS Neural Rendering",
                Value("Backend", "Whether the UnityRHI backend assembly is installed.",
                    () => DLSSNeuralRenderingBackendLoader.GetStatus().BackendInstalled ? "Installed" : "Not installed"),
                Value("Graphics API", "DLSS Neural Rendering requires Direct3D 12.",
                    () => DLSSNeuralRenderingBackendLoader.GetStatus().D3D12Active ? "Direct3D 12" : "Unavailable"),
                Value("Runtime", "Whether the NGX runtime initialized.", () =>
                {
                    var status = DLSSNeuralRenderingBackendLoader.GetStatus();
                    return status.RuntimeAvailable ? "Available" : $"Unavailable (0x{unchecked((uint)status.InitResult):X8})";
                }),
                Value("Create Result", "Result of the last feature creation.",
                    () => $"0x{unchecked((uint)DLSSNeuralRenderingBackendLoader.GetStatus().LastCreateResult):X8}"),
                Value("Evaluate Result", "Result of the last evaluation.",
                    () => $"0x{unchecked((uint)DLSSNeuralRenderingBackendLoader.GetStatus().LastEvaluateResult):X8}"),
                Choice("Input Debug", "Visualize a prepared DLSS Neural Rendering input.",
                    c => c.DLSSNeuralRenderingDebugMode, (c, v) => c.DLSSNeuralRenderingDebugMode = v),
                Float("Motion Range", "Visualization range of motion vector inputs.",
                    c => c.DLSSNeuralRenderingDebugMotionRange, (c, v) => c.DLSSNeuralRenderingDebugMotionRange = v),
                Float("Depth Range", "Visualization range of the linear eye depth input.",
                    c => c.DLSSNeuralRenderingDebugDepthRange, (c, v) => c.DLSSNeuralRenderingDebugDepthRange = v),
                new DebugUI.Button
                {
                    displayName = "Reset History",
                    action = () =>
                    {
                        foreach (var feature in Features)
                            feature.ResetDLSSNeuralRenderingHistory();
                    }
                });
        }

        private static DebugUI.Foldout Section(string displayName, params DebugUI.Widget[] children)
        {
            var foldout = new DebugUI.Foldout { displayName = displayName, opened = true };
            foldout.children.Add(children);
            return foldout;
        }

        private static DebugUI.Container Indented(string id, Func<bool> isHidden, params DebugUI.Widget[] children)
        {
            var container = new DebugUI.Container(id) { isHiddenCallback = isHidden };
            container.children.Add(children);
            return container;
        }

        private static DebugUI.Value Value(string displayName, string tooltip, Func<object> getter)
        {
            return new DebugUI.Value { displayName = displayName, tooltip = tooltip, getter = getter };
        }

        private DebugUI.BoolField Toggle(string displayName, string tooltip,
            Func<IllusionRuntimeRenderingConfig, bool> get, Action<IllusionRuntimeRenderingConfig, bool> set)
        {
            _resets.Add(() => set(Config, get(Defaults)));
            return new DebugUI.BoolField
            {
                displayName = displayName,
                tooltip = tooltip,
                getter = () => get(Config),
                setter = value => set(Config, value)
            };
        }

        private DebugUI.FloatField Float(string displayName, string tooltip,
            Func<IllusionRuntimeRenderingConfig, float> get, Action<IllusionRuntimeRenderingConfig, float> set)
        {
            _resets.Add(() => set(Config, get(Defaults)));
            return new DebugUI.FloatField
            {
                displayName = displayName,
                tooltip = tooltip,
                getter = () => get(Config),
                setter = value => set(Config, value)
            };
        }

        private DebugUI.EnumField Choice<T>(string displayName, string tooltip,
            Func<IllusionRuntimeRenderingConfig, T> get, Action<IllusionRuntimeRenderingConfig, T> set) where T : struct, System.Enum
        {
            _resets.Add(() => set(Config, get(Defaults)));
            return new DebugUI.EnumField
            {
                displayName = displayName,
                tooltip = tooltip,
                autoEnum = typeof(T),
                getter = () => Convert.ToInt32(get(Config)),
                setter = value => set(Config, (T)System.Enum.ToObject(typeof(T), value)),
                getIndex = () => Convert.ToInt32(get(Config)),
                setIndex = value => set(Config, (T)System.Enum.ToObject(typeof(T), value))
            };
        }
    }
}
