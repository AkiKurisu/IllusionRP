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

            _config.EnableScreenSpaceGlobalIllumination = EditorGUILayout.ToggleLeft(
                new GUIContent("Screen Space Global Illumination", "Enable/Disable SSGI"),
                _config.EnableScreenSpaceGlobalIllumination);

            _config.EnableContactShadows = EditorGUILayout.ToggleLeft(
                new GUIContent("Contact Shadows", "Enable/Disable Contact Shadows"),
                _config.EnableContactShadows);

            _config.EnablePercentageCloserSoftShadows = EditorGUILayout.ToggleLeft(
                new GUIContent("Percentage Closer Soft Shadows", "Enable/Disable PCSS"),
                _config.EnablePercentageCloserSoftShadows);

            _config.EnableScreenSpaceAmbientOcclusion = EditorGUILayout.ToggleLeft(
                new GUIContent("Screen Space Ambient Occlusion", "Enable/Disable SSAO"),
                _config.EnableScreenSpaceAmbientOcclusion);

            _config.EnableVolumetricFog = EditorGUILayout.ToggleLeft(
                new GUIContent("Volumetric Fog", "Enable/Disable Volumetric Fog"),
                _config.EnableVolumetricFog);

            _config.EnablePrecomputedRadianceTransferGlobalIllumination = EditorGUILayout.ToggleLeft(
                new GUIContent("PRT Global Illumination", "Enable/Disable PRT GI"),
                _config.EnablePrecomputedRadianceTransferGlobalIllumination);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Graphics API Settings", EditorStyles.boldLabel);

            _config.EnableAsyncCompute = EditorGUILayout.ToggleLeft(
                new GUIContent("Async Compute", "Enable/Disable Async Compute"),
                _config.EnableAsyncCompute);

            _config.EnableNativeRenderPass = EditorGUILayout.ToggleLeft(
                new GUIContent("Native Render Pass", "Enable/Disable Native Render Pass"),
                _config.EnableNativeRenderPass);

            _config.EnableComputeShader = EditorGUILayout.ToggleLeft(
                new GUIContent("Compute Shader", "Enable/Disable Compute Shader"),
                _config.EnableComputeShader);
        }

        private void DrawDebugOptions()
        {
            EditorGUILayout.LabelField("Debug Options", EditorStyles.boldLabel);
            // Debug toggles
            _config.EnableMotionVectorsDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Motion Vectors Debug", "Visualize motion vectors"),
                _config.EnableMotionVectorsDebug);

            _config.EnableScreenSpaceReflectionDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("SSR Debug", "Visualize Screen Space Reflection"),
                _config.EnableScreenSpaceReflectionDebug);

            _config.EnablePerObjectShadowDebug = EditorGUILayout.ToggleLeft(
                new GUIContent("Per Object Shadow Debug", "Visualize per object shadows"),
                _config.EnablePerObjectShadowDebug);
            
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
            _config.EnableScreenSpaceGlobalIllumination = true;
            _config.EnableContactShadows = true;
            _config.EnablePercentageCloserSoftShadows = true;
            _config.EnableScreenSpaceAmbientOcclusion = true;
            _config.EnableVolumetricFog = true;
            _config.EnablePrecomputedRadianceTransferGlobalIllumination = true;
            _config.EnableAsyncCompute = true;
            _config.EnableNativeRenderPass = true;
            _config.EnableComputeShader = true;
            Repaint();
        }

        private void ResetDebugOptions()
        {
            _config.EnableMotionVectorsDebug = false;
            _config.EnableScreenSpaceReflectionDebug = false;
            _config.ExposureDebugMode = ExposureDebugMode.None;
            _config.ScreenSpaceShadowDebugMode = ScreenSpaceShadowDebugMode.None;
            _config.EnablePerObjectShadowDebug = false;
            _config.CenterHistogramAroundMiddleGrey = false;
            _config.DisplayOnSceneOverlay = true;
            _config.DisplayFinalImageHistogramAsRGB = false;
            _config.DisplayMaskOnly = false;
            Repaint();
        }
    }
}

