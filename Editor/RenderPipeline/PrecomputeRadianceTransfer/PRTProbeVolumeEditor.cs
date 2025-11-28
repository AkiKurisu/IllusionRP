using UnityEngine;
using UnityEditor;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PRTProbeVolume))]
    internal class PRTProbeVolumeEditor : PropertyFetchEditor<PRTProbeVolume>
    {
        private static readonly Color ProbeHandleColor = new(0.2f, 0.8f, 0.1f, 0.125f);

        // Grid Settings
        private SerializedProperty _probeSizeX;
        private SerializedProperty _probeSizeY;
        private SerializedProperty _probeSizeZ;
        private SerializedProperty _probeGridSize;

        // Probe Placement
        private SerializedProperty _enableBakePreprocess;
        private SerializedProperty _virtualOffset;
        private SerializedProperty _geometryBias;
        private SerializedProperty _rayOriginBias;

        // Relight Settings
        private SerializedProperty _multiFrameRelight;
        private SerializedProperty _probesPerFrameUpdate;
        private SerializedProperty _localProbeCount;

        // Voxel Settings
        private SerializedProperty _voxelProbeSize;

        // Asset
        private SerializedProperty _asset;

        // Debug Settings
        private SerializedProperty _debugMode;
        private SerializedProperty _probeHandleSize;
        private SerializedProperty _bakeResolution;

        protected override void OnEnable()
        {
            base.OnEnable();

            // Grid Settings
            _probeSizeX = Properties.Find(volume => volume.probeSizeX);
            _probeSizeY = Properties.Find(volume => volume.probeSizeY);
            _probeSizeZ = Properties.Find(volume => volume.probeSizeZ);
            _probeGridSize = Properties.Find(volume => volume.probeGridSize);

            // Probe Placement
            _virtualOffset = Properties.Find(volume => volume.virtualOffset);
            _geometryBias = Properties.Find(volume => volume.geometryBias);
            _rayOriginBias = Properties.Find(volume => volume.rayOriginBias);
            _enableBakePreprocess = Properties.Find(volume => volume.enableBakePreprocess);

            // Relight Settings
            _multiFrameRelight = Properties.Find(volume => volume.multiFrameRelight);
            _probesPerFrameUpdate = Properties.Find(volume => volume.probesPerFrameUpdate);
            _localProbeCount = Properties.Find(volume => volume.localProbeCount);

            // Voxel Settings
            _voxelProbeSize = Properties.Find(volume => volume.voxelProbeSize);

            // Asset
            _asset = Properties.Find(volume => volume.asset);

            // Debug Settings
            _debugMode = Properties.Find(volume => volume.debugMode);
            _probeHandleSize = Properties.Find(volume => volume.probeHandleSize);
            _bakeResolution = Properties.Find(volume => volume.bakeResolution);
        }

        public override void OnInspectorGUI()
        {
            if (!PRTProbeVolume.IsFeatureEnabled)
            {
                EditorGUILayout.HelpBox("Precomputed Radiance Transfer Global Illumination is not activated in Renderer.",
                    MessageType.Info);
                return;
            }

            serializedObject.Update();

            using (new EditorGUI.DisabledScope(PRTVolumeManager.IsBaking))
            {
                // Basic ProbeVolume settings
                DrawGridSettings();
                DrawProbePlacementSettings();
                DrawRelightSettings();
                DrawVoxelSettings();

                // Probe Selection & Debug section
                DrawDebugSettingsSection();

                // Bake settings section
                DrawBakeSettingsSection();
            }

            // Action buttons
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawActionButtons();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGridSettings()
        {
            if (Foldout("Grid Settings", true))
            {
                EditorGUILayout.PropertyField(_probeSizeX, Styles.ProbeSizeXLabel);
                EditorGUILayout.PropertyField(_probeSizeY, Styles.ProbeSizeYLabel);
                EditorGUILayout.PropertyField(_probeSizeZ, Styles.ProbeSizeZLabel);
                EditorGUILayout.PropertyField(_probeGridSize, Styles.ProbeGridSizeLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawProbePlacementSettings()
        {
            if (Foldout("Probe Placement", true))
            {
                EditorGUILayout.PropertyField(_enableBakePreprocess, Styles.EnableBakePreprocessLabel);
                
                if (_enableBakePreprocess.boolValue)
                {
                    EditorGUILayout.PropertyField(_virtualOffset, Styles.VirtualOffsetLabel);
                    EditorGUILayout.PropertyField(_geometryBias, Styles.GeometryBiasLabel);
                    EditorGUILayout.PropertyField(_rayOriginBias, Styles.RayOriginBiasLabel);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawRelightSettings()
        {
            if (Foldout("Relight Settings", true))
            {
                EditorGUILayout.PropertyField(_multiFrameRelight, Styles.MultiFrameRelightLabel);

                if (_multiFrameRelight.boolValue)
                {
                    EditorGUILayout.PropertyField(_probesPerFrameUpdate, Styles.ProbesPerFrameUpdateLabel);
                    EditorGUILayout.PropertyField(_localProbeCount, Styles.LocalProbeCountLabel);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawVoxelSettings()
        {
            if (Foldout("Voxel Settings", true))
            {
                EditorGUILayout.PropertyField(_voxelProbeSize, Styles.VoxelProbeSizeLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawDebugSettingsSection()
        {
            if (Foldout("Debug Settings", true))
            {
                // Probe selection
                if (Target.Probes != null && Target.Probes.Length > 0)
                {
                    // Volume debug mode
                    EditorGUILayout.PropertyField(_debugMode, Styles.VolumeDebugModeLabel);

                    if (_debugMode.enumValueIndex == (int)ProbeVolumeDebugMode.ProbeRadiance)
                    {
                        // Debug mode for selected probe
                        var newDebugMode = (ProbeDebugMode)EditorGUILayout.EnumPopup("Probe Debug Mode",
                            Target.selectedProbeDebugMode);
                        Target.selectedProbeDebugMode = newDebugMode;
                    }

                    if (_debugMode.enumValueIndex == (int)ProbeVolumeDebugMode.ProbeGridWithVirtualOffset)
                    {
                        using (new EditorGUI.DisabledScope(Application.isPlaying))
                        {
                            if (GUILayout.Button("Bake Virtual Offset"))
                            {
                                Target.BakeProbeVirtualOffset();
                            }
                        }
                    }

                    if (_debugMode.enumValueIndex != (int)ProbeVolumeDebugMode.None)
                    {
                        EditorGUILayout.PropertyField(_probeHandleSize, Styles.ProbeHandleSizeLabel);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No probes found. Click 'Generate Probes' to create probe grid.", MessageType.Info);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawBakeSettingsSection()
        {
            if (Foldout("Bake Settings", true))
            {
                EditorGUILayout.PropertyField(_asset, Styles.ProbeVolumeAssetLabel);

                // Bake resolution
                EditorGUILayout.PropertyField(_bakeResolution, Styles.BakeResolutionLabel);
            }

            if (Target.asset && !Target.asset.HasValidData)
            {
                EditorGUILayout.HelpBox(
                    "This prt probe volume does not have valid data for relighting.",
                    MessageType.Warning);
            }
        }

        private static void DrawActionButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical();

            if (PRTVolumeManager.IsBaking)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel Baking"))
                {
                    StopBaking();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (ButtonWithDropdownList(Styles.GenerateLightingLabel, 
                        Styles.DetailActionLabels, 
                        OnActionDropDown))
                {
                    PRTBakeManager.GenerateLighting();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }
        
        private static void OnActionDropDown(object data)
        {
            int mode = (int)data;
            switch (mode)
            {
                case 0:
                    PRTBakeManager.BakeAllReflectionProbes();
                    break;
                case 1:
                    ClearData();
                    break;
            }
        }

        private static void ClearData()
        {
            if (EditorUtility.DisplayDialog("Clear Baked Data",
                    "Are you sure you want to clear baked data? This action cannot be undone.",
                    "Clear", "Cancel"))
            {
                PRTBakeManager.ClearBakedData();
            }
        }

        private static void StopBaking()
        {
            PRTBakeManager.StopBaking();
            PRTBakeManager.ClearBakedData();
        }

        private void OnSceneGUI()
        {
            if (Target.Probes == null || Target.debugMode != ProbeVolumeDebugMode.ProbeRadiance)
            {
                return;
            }

            for (int i = 0; i < Target.Probes.Length; i++)
            {
                Vector3 probePos = Target.Probes[i].Position;

                using (new Handles.DrawingScope(ProbeHandleColor))
                {
                    // Draw selectable handles
                    if (Handles.Button(probePos, Quaternion.identity, Target.probeHandleSize * 0.2f,
                            Target.probeHandleSize * 0.2f, Handles.SphereHandleCap))
                    {
                        Target.selectedProbeIndex = i;
                        Repaint();
                    }
                }
            }
        }

        private static class Styles
        {
            // Grid Settings
            public static readonly GUIContent ProbeSizeXLabel = new("Probe Size X", "Number of probes along X axis");
            public static readonly GUIContent ProbeSizeYLabel = new("Probe Size Y", "Number of probes along Y axis");
            public static readonly GUIContent ProbeSizeZLabel = new("Probe Size Z", "Number of probes along Z axis");
            public static readonly GUIContent ProbeGridSizeLabel = new("Probe Grid Size", "Distance between probes");

            // Probe Placement
            public static readonly GUIContent VirtualOffsetLabel = new("Virtual Offset", "Set volume offset when sampling surfels at bake time");
            public static readonly GUIContent GeometryBiasLabel = new("Geometry Bias", "How far to push a probe's capture point out of geometry");
            public static readonly GUIContent RayOriginBiasLabel = new("Ray Origin Bias", "Distance between a probe's center and the point URP uses for sampling ray origin");
            public static readonly GUIContent EnableBakePreprocessLabel = new("Enable Bake Preprocess", "Enable bake preprocess for per-probe place adjustment");

            // Relight Settings
            public static readonly GUIContent MultiFrameRelightLabel = new("Multi Frame Relight", "Enable multi frame relight to improve performance");
            public static readonly GUIContent ProbesPerFrameUpdateLabel = new("Probes Per Frame Update", "Number of probes to update per frame");
            public static readonly GUIContent LocalProbeCountLabel = new("Local Probe Count", "Number of camera nearby probes to relight in additional to per frame update roulette");

            // Voxel Settings
            public static readonly GUIContent VoxelProbeSizeLabel = new("Voxel Probe Size", "Voxel texture const probe size");

            // Debug Settings
            public static readonly GUIContent BakeResolutionLabel =
                new("Bake Resolution", "Resolution for cubemap baking");

            public static readonly GUIContent ProbeHandleSizeLabel = new("Probe Handle Size", "Size of Probe Handle.");

            public static readonly GUIContent VolumeDebugModeLabel =
                new("Volume Debug Mode", "Debug mode of Probe Volume.");

            public static readonly GUIContent ProbeVolumeAssetLabel =
                new("Probe Volume Asset", "Configure baked probe volume asset.");
            
            // Actions
            public static readonly GUIContent GenerateLightingLabel = EditorGUIUtility.TrTextContent("Generate Lighting", "Generates the probe volume and additional reflection probe data.");

            public static readonly string[] DetailActionLabels =
            {
                "Bake Reflection Probes Normalization Data",
                "Clear Baked Data"
            };
        }
    }
}