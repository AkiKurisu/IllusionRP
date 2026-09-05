using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    /// <summary>
    /// Editor window for controlling Illusion Rendering debug and feature settings at runtime.
    /// </summary>
    public class IllusionRenderingDebugger : EditorWindow
    {
        private Vector2 _scrollPosition;
        
        private IllusionRuntimeRenderingConfig _config;
        
        [MenuItem("Window/Analysis/Illusion Rendering Debugger")]
        public static void ShowWindow()
        {
            var window = GetWindow<IllusionRenderingDebugger>("Illusion Rendering Debugger");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshConfig();
        }

        private void RefreshConfig()
        {
            _config = IllusionRuntimeRenderingConfig.Get();
        }

        private void OnGUI()
        {
            if (_config == null)
            {
                RefreshConfig();
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawRenderingFeatures();
            EditorGUILayout.Space(10);

            DrawDebugOptions();
            EditorGUILayout.Space(10);

            DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        private void DrawRenderingFeatures()
        {
            EditorGUILayout.LabelField("Rendering Features", EditorStyles.boldLabel);
            _config.EnableScreenSpaceReflection = EditorGUILayout.ToggleLeft(
                new GUIContent("Screen Space Reflection", "Enable/Disable SSR"),
                _config.EnableScreenSpaceReflection);

            _config.EnableTransparentScreenSpaceReflection = EditorGUILayout.ToggleLeft(
                new GUIContent("Transparent Screen Space Reflection", "Enable screen space reflections for supported transparent water shaders"),
                _config.EnableTransparentScreenSpaceReflection);

            _config.EnableScreenSpaceRefraction = EditorGUILayout.ToggleLeft(
                new GUIContent("Screen Space Refraction", "Copy opaque scene color for supported refractive shaders"),
                _config.EnableScreenSpaceRefraction);

            _config.EnableScreenSpaceGlobalIllumination = EditorGUILayout.ToggleLeft(
                new GUIContent("Screen Space Global Illumination", "Enable/Disable SSGI"),
                _config.EnableScreenSpaceGlobalIllumination);

            _config.EnableContactShadows = EditorGUILayout.ToggleLeft(
                new GUIContent("Contact Shadows", "Enable/Disable contact shadows"),
                _config.EnableContactShadows);

            _config.EnablePercentageCloserSoftShadows = EditorGUILayout.ToggleLeft(
                new GUIContent("Percentage Closer Soft Shadows", "Enable/Disable PCSS"),
                _config.EnablePercentageCloserSoftShadows);

            _config.EnableAreaLights = EditorGUILayout.ToggleLeft(
                new GUIContent("Area Lights", "Enable/Disable rectangle area lights"),
                _config.EnableAreaLights);

            _config.EnableScreenSpaceAmbientOcclusion = EditorGUILayout.ToggleLeft(
                new GUIContent("Screen Space Ambient Occlusion", "Enable/Disable SSAO"),
                _config.EnableScreenSpaceAmbientOcclusion);

            _config.EnableVolumetricFog = EditorGUILayout.ToggleLeft(
                new GUIContent("Volumetric Fog", "Enable/Disable volumetric fog"),
                _config.EnableVolumetricFog);

            _config.EnablePrecomputedRadianceTransferGlobalIllumination = EditorGUILayout.ToggleLeft(
                new GUIContent("PRT Global Illumination", "Enable/Disable PRT global illumination"),
                _config.EnablePrecomputedRadianceTransferGlobalIllumination);

            _config.EnableDLSSNeuralRendering = EditorGUILayout.ToggleLeft(
                new GUIContent("DLSS Neural Rendering", "Enable/Disable the optional full-resolution NR pass"),
                _config.EnableDLSSNeuralRendering);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Graphics API Settings", EditorStyles.boldLabel);

            // TODO: Fix Async Compute Crash
            // _config.EnableAsyncCompute = EditorGUILayout.ToggleLeft(
            //     new GUIContent("Async Compute", "Enable/Disable async compute"),
            //     _config.EnableAsyncCompute);

            _config.EnableComputeShader = EditorGUILayout.ToggleLeft(
                new GUIContent("Compute Shader", "Enable/Disable compute shader"),
                _config.EnableComputeShader);
            
            _config.EnableVrs = EditorGUILayout.ToggleLeft(
                new GUIContent("Stencil VRS", "Enable/Disable stencil vrs control"),
                _config.EnableVrs);
        }

        private void DrawDebugOptions()
        {
            EditorGUILayout.LabelField("Debug Options", EditorStyles.boldLabel);
            // Debug toggles
            _config.EnableMotionVectorsDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Motion Vectors Debug", "Visualize motion vector color"),
                _config.EnableMotionVectorsDebug);

            _config.EnableScreenSpaceReflectionDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("SSR Debug", "Visualize screen space reflection"),
                _config.EnableScreenSpaceReflectionDebug);

            _config.EnableTransparentScreenSpaceReflectionDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Transparent SSR Debug", "Visualize screen space reflections for transparent water"),
                _config.EnableTransparentScreenSpaceReflectionDebug);

            _config.EnablePerObjectShadowDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Per Object Shadow Debug", "Visualize per-object shadows"),
                _config.EnablePerObjectShadowDebug);

            _config.EnableAreaLightShadowAtlasDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Area Light Shadow Atlas Debug", "Overlay the area light shadow atlas"),
                _config.EnableAreaLightShadowAtlasDebug);

            if (_config.EnableAreaLightShadowAtlasDebug)
            {
                EditorGUI.indentLevel++;
                _config.AreaLightShadowAtlasDebugMinValue = EditorGUILayout.FloatField(
                    new GUIContent("Min Value", "Atlas value mapped to black"),
                    _config.AreaLightShadowAtlasDebugMinValue);
                _config.AreaLightShadowAtlasDebugMaxValue = EditorGUILayout.FloatField(
                    new GUIContent("Max Value", "Atlas value mapped to white"),
                    _config.AreaLightShadowAtlasDebugMaxValue);
                EditorGUI.indentLevel--;
            }

            _config.EnableVrsDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Stencil VRS Debug", "Visualize stencil vrs color mask"),
                _config.EnableVrsDebug);

            EditorGUILayout.Space(5);
            DrawDLSSNeuralRenderingDebug();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Advanced Debug", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Exposure Debug Mode", "Select exposure debug visualization mode"));
            _config.ExposureDebugMode = (ExposureDebugMode)EditorGUILayout.EnumPopup(GUIContent.none, _config.ExposureDebugMode);
            EditorGUILayout.EndHorizontal();

            if (_config.ExposureDebugMode != ExposureDebugMode.None)
            {
                EditorGUI.indentLevel++;

                _config.CenterHistogramAroundMiddleGrey = EditorGUILayout.ToggleLeft(
                    new GUIContent("Center Around Middle Grey", "Center histogram around middle-grey point"),
                    _config.CenterHistogramAroundMiddleGrey);

                _config.DisplayOnSceneOverlay = EditorGUILayout.ToggleLeft(
                    new GUIContent("Display Scene Overlay", "Show on-scene overlay for excluded pixels"),
                    _config.DisplayOnSceneOverlay);

                _config.DisplayFinalImageHistogramAsRGB = EditorGUILayout.ToggleLeft(
                    new GUIContent("Histogram RGB Mode", "Display histogram in RGB mode"),
                    _config.DisplayFinalImageHistogramAsRGB);

                _config.DisplayMaskOnly = EditorGUILayout.ToggleLeft(
                    new GUIContent("Display Mask Only", "Show only the mask in picture-in-picture"),
                    _config.DisplayMaskOnly);

                EditorGUI.indentLevel--;
            }

            // Screen space shadow debug
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Screen Space Shadow Debug", "Select screen space shadow debug mode"));
            _config.ScreenSpaceShadowDebugMode = (ScreenSpaceShadowDebugMode)EditorGUILayout.EnumPopup(GUIContent.none, _config.ScreenSpaceShadowDebugMode);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDLSSNeuralRenderingDebug()
        {
            EditorGUILayout.LabelField("DLSS Neural Rendering", EditorStyles.boldLabel);

            DLSSNeuralRenderingRuntimeStatus status = DLSSNeuralRenderingBackendLoader.GetStatus();
            EditorGUILayout.LabelField("Backend", status.BackendInstalled ? "Installed" : "Not installed");
            EditorGUILayout.LabelField("Graphics API", status.D3D12Active ? "Direct3D 12" : "Unavailable");
            EditorGUILayout.LabelField("Runtime", status.RuntimeAvailable
                ? "Available"
                : $"Unavailable (0x{unchecked((uint)status.InitResult):X8})");
            EditorGUILayout.LabelField("Create Result", $"0x{unchecked((uint)status.LastCreateResult):X8}");
            EditorGUILayout.LabelField("Evaluate Result", $"0x{unchecked((uint)status.LastEvaluateResult):X8}");

            _config.DLSSNeuralRenderingDebugMode = (DLSSNeuralRenderingDebugMode)EditorGUILayout.EnumPopup(
                new GUIContent("Input Debug", "Visualize a prepared DLSS Neural Rendering input"),
                _config.DLSSNeuralRenderingDebugMode);
            _config.DLSSNeuralRenderingDebugMotionRange = EditorGUILayout.FloatField(
                new GUIContent("Motion Range", "Visualization range for motion vector inputs"),
                _config.DLSSNeuralRenderingDebugMotionRange);
            _config.DLSSNeuralRenderingDebugDepthRange = EditorGUILayout.FloatField(
                new GUIContent("Depth Range", "Visualization range for linear eye depth input"),
                _config.DLSSNeuralRenderingDebugDepthRange);

            if (GUILayout.Button("Reset History"))
            {
                foreach (IllusionRendererFeature feature in Resources.FindObjectsOfTypeAll<IllusionRendererFeature>())
                    feature.ResetDLSSNeuralRenderingHistory();
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(5);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset All Features", GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Reset All Features",
                        "Reset all rendering features to default values?",
                        "Reset", "Cancel"))
                    {
                        ResetAllFeatures();
                    }
                }

                if (GUILayout.Button("Reset Debug Options", GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Reset Debug Options",
                        "Reset all debug options to default values?",
                        "Reset", "Cancel"))
                    {
                        ResetDebugOptions();
                    }
                }
            }

            EditorGUILayout.Space(5);
        }

        private void ResetAllFeatures()
        {
            _config.EnableScreenSpaceReflection = true;
            _config.EnableTransparentScreenSpaceReflection = true;
            _config.EnableScreenSpaceRefraction = true;
            _config.EnableScreenSpaceGlobalIllumination = true;
            _config.EnableContactShadows = true;
            _config.EnablePercentageCloserSoftShadows = true;
            _config.EnableScreenSpaceAmbientOcclusion = true;
            _config.EnableVolumetricFog = true;
            _config.EnablePrecomputedRadianceTransferGlobalIllumination = true;
            _config.EnableDLSSNeuralRendering = true;
            _config.EnableAsyncCompute = false;
            _config.EnableComputeShader = true;
            Repaint();
        }

        private void ResetDebugOptions()
        {
            _config.EnableMotionVectorsDebug = false;
            _config.EnableScreenSpaceReflectionDebug = false;
            _config.EnableTransparentScreenSpaceReflectionDebug = false;
            _config.ExposureDebugMode = ExposureDebugMode.None;
            _config.ScreenSpaceShadowDebugMode = ScreenSpaceShadowDebugMode.None;
            _config.EnablePerObjectShadowDebug = false;
            _config.EnableVrsDebug = false;
            _config.DLSSNeuralRenderingDebugMode = DLSSNeuralRenderingDebugMode.Off;
            _config.DLSSNeuralRenderingDebugMotionRange = 32f;
            _config.DLSSNeuralRenderingDebugDepthRange = 100f;
            _config.CenterHistogramAroundMiddleGrey = false;
            _config.DisplayOnSceneOverlay = true;
            _config.DisplayFinalImageHistogramAsRGB = false;
            _config.DisplayMaskOnly = false;
            Repaint();
        }
    }
}

