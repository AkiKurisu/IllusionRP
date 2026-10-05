using UnityEngine;
using UnityEditor;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PRTProbeVolume))]
    internal partial class PRTProbeVolumeEditor : PropertyFetchEditor<PRTProbeVolume>
    {
        private static readonly Color ProbeHandleColor = new(0.2f, 0.8f, 0.1f, 0.125f);

        private const double StatsRepaintInterval = 0.2;

        private double _nextStatsRepaintTime;

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
        private SerializedProperty _sectorWidth;
        private SerializedProperty _enableRelightShadow;
        private SerializedProperty _shadowCacheMaxAge;
        private SerializedProperty _enableShadowCacheStats;
        private SerializedProperty _shadowCacheStatsReadbackInterval;
        private SerializedProperty _sectorsPerFrame;
        private SerializedProperty _sectorBudgetMiB;
        private SerializedProperty _uploadBudgetMiB;

        // Voxel Settings
        private SerializedProperty _voxelProbeSize;


        // Debug Settings
        private SerializedProperty _debugMode;
        private SerializedProperty _probeHandleSize;
        private SerializedProperty _shadowCacheDebugReadbackInterval;
        private SerializedProperty _shadowCacheDebugShowLabels;
        private SerializedProperty _shadowCacheDebugSurfelSize;

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
            _sectorWidth = Properties.Find(volume => volume.sectorWidth);
            _enableRelightShadow = Properties.Find(volume => volume.enableRelightShadow);
            _shadowCacheMaxAge = Properties.Find(volume => volume.shadowCacheMaxAge);
            _enableShadowCacheStats = Properties.Find(volume => volume.enableShadowCacheStats);
            _shadowCacheStatsReadbackInterval = Properties.Find(volume => volume.shadowCacheStatsReadbackInterval);
            _sectorsPerFrame = Properties.Find(volume => volume.sectorsPerFrame);
            _sectorBudgetMiB = Properties.Find(volume => volume.sectorBudgetMiB);
            _uploadBudgetMiB = Properties.Find(volume => volume.uploadBudgetMiB);

            // Voxel Settings
            _voxelProbeSize = Properties.Find(volume => volume.voxelProbeSize);

            InitializeBakeProperties();

            // Debug Settings
            _debugMode = Properties.Find(volume => volume.debugMode);
            _probeHandleSize = Properties.Find(volume => volume.probeHandleSize);
            _shadowCacheDebugReadbackInterval = Properties.Find(volume => volume.shadowCacheDebugReadbackInterval);
            _shadowCacheDebugShowLabels = Properties.Find(volume => volume.shadowCacheDebugShowLabels);
            _shadowCacheDebugSurfelSize = Properties.Find(volume => volume.shadowCacheDebugSurfelSize);

            EditorApplication.update += RepaintStatsInspector;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintStatsInspector;
        }

        private void RepaintStatsInspector()
        {
            if (!Target ||
                !Target.enableShadowCacheStats &&
                Target.debugMode != ProbeVolumeDebugMode.ShadowCache &&
                Target.isActiveAndEnabled)
            {
                return;
            }

            double time = EditorApplication.timeSinceStartup;
            if (time < _nextStatsRepaintTime)
            {
                return;
            }

            _nextStatsRepaintTime = time + StatsRepaintInterval;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            if (!PRTProbeVolume.IsFeatureEnabled)
            {
                EditorGUILayout.HelpBox("Precomputed Radiance Transfer Global Illumination is not activated in Renderer.",
                    MessageType.Info);
            }

            serializedObject.Update();
            if (!string.IsNullOrEmpty(Target.AssetInvalidReason))
                EditorGUILayout.HelpBox(Target.AssetInvalidReason, MessageType.Warning);

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
                EditorGUILayout.PropertyField(_sectorsPerFrame);
                EditorGUILayout.PropertyField(_sectorBudgetMiB);
                EditorGUILayout.PropertyField(_uploadBudgetMiB);
                DrawSolverStatus();
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(_enableRelightShadow, Styles.EnableRelightShadowLabel);
                if (_enableRelightShadow.boolValue)
                {
                    EditorGUILayout.PropertyField(_shadowCacheMaxAge, Styles.ShadowCacheMaxAgeLabel);
                    EditorGUILayout.PropertyField(_enableShadowCacheStats, Styles.EnableShadowCacheStatsLabel);
                    if (_enableShadowCacheStats.boolValue) DrawShadowCacheStats();
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

                    if (_debugMode.enumValueIndex == (int)ProbeVolumeDebugMode.ShadowCache)
                    {
                        DrawShadowCacheDebugSettings();
                    }

                    if (_debugMode.enumValueIndex == (int)ProbeVolumeDebugMode.ProbeGridWithVirtualOffset)
                    {
                        using (new EditorGUI.DisabledScope(Application.isPlaying))
                        {
                            if (GUILayout.Button("Bake Virtual Offset"))
                            {
                                PRTBakeManager.BakePlacementPreview(Target);
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

        private void DrawShadowCacheDebugSettings()
        {
            EditorGUILayout.PropertyField(_shadowCacheDebugReadbackInterval,
                Styles.ShadowCacheDebugReadbackIntervalLabel);
            EditorGUILayout.PropertyField(_shadowCacheDebugShowLabels,
                Styles.ShadowCacheDebugShowLabelsLabel);
            EditorGUILayout.PropertyField(_shadowCacheDebugSurfelSize,
                Styles.ShadowCacheDebugSurfelSizeLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(Styles.ShadowCacheDebugSnapshotValidLabel,
                    Target.HasShadowCacheDebugSnapshot);
                EditorGUILayout.LongField(Styles.ShadowCacheDebugSnapshotFrameLabel,
                    Target.LatestShadowCacheDebugFrameIndex);
                EditorGUILayout.LongField(Styles.ShadowCacheDebugSnapshotEpochLabel,
                    Target.LatestShadowCacheDebugEpoch);
                EditorGUILayout.DoubleField(Styles.ShadowCacheDebugSnapshotTimeLabel,
                    Target.LatestShadowCacheDebugTime);
            }
        }

        private void OnSceneGUI()
        {
            if (Target.Probes == null ||
                Target.debugMode != ProbeVolumeDebugMode.ProbeRadiance &&
                Target.debugMode != ProbeVolumeDebugMode.ShadowCache)
            {
                return;
            }

            for (int i = 0; i < Target.Probes.Length; i++)
            {
                Vector3 probePos = Target.Probes[i].Position;
                bool shadowCacheMode = Target.debugMode == ProbeVolumeDebugMode.ShadowCache;
                float visibleHandleSize = Target.probeHandleSize * (shadowCacheMode ? 0.05f : 0.2f);
                float pickHandleSize = Target.probeHandleSize * 0.2f;
                var handleColor = shadowCacheMode
                    ? new Color(ProbeHandleColor.r, ProbeHandleColor.g, ProbeHandleColor.b, 0.025f)
                    : ProbeHandleColor;

                using (new Handles.DrawingScope(handleColor))
                {
                    // Draw selectable handles
                    if (Handles.Button(probePos, Quaternion.identity, visibleHandleSize,
                            pickHandleSize, Handles.SphereHandleCap))
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
            public static readonly GUIContent EnableRelightShadowLabel = new("Enable Relight Shadow", "Include world-space visibility in diffuse relighting.");
            public static readonly GUIContent ShadowCacheMaxAgeLabel = new("Old Sample Age", "Diagnostic threshold in scene ticks. Samples outside the map remain reusable until their visibility epoch changes.");
            public static readonly GUIContent EnableShadowCacheStatsLabel = new("Enable Shadow Cache Stats", "Read back PRT shadow cache hit/miss counters for profiling.");
            public static readonly GUIContent ShadowCacheStatsReadbackIntervalLabel = new("Stats Readback Interval", "Frames between full global shadow cache snapshot readbacks.");
            public static readonly GUIContent ShadowCacheDispatchStatsLabel = new("Current Dispatch Stats");
            public static readonly GUIContent ShadowCacheGlobalStatsLabel = new("Global Cache Snapshot");
            public static readonly GUIContent ShadowCacheWindowStatsLabel = new("Current Window Snapshot");
            public static readonly GUIContent ShadowCacheStatsValidLabel = new("Stats Valid");
            public static readonly GUIContent ShadowCacheEvaluatedLabel = new("Evaluated");
            public static readonly GUIContent ShadowCacheHitsLabel = new("Cache Hits");
            public static readonly GUIContent ShadowCacheMissesLabel = new("Cache Misses");
            public static readonly GUIContent ShadowCacheInvalidEpochLabel = new("Invalid By Epoch");
            public static readonly GUIContent ShadowCacheInvalidAgeLabel = new("Invalid By Age");
            public static readonly GUIContent ShadowCacheSamplesLabel = new("Shadow Samples");
            public static readonly GUIContent ShadowCacheFallbackLabel = new("Fallback From Cache");
            public static readonly GUIContent ShadowCacheUncoveredLabel = new("Uncovered No Cache");
            public static readonly GUIContent ShadowCacheGlobalFrameLabel = new("Snapshot Frame");
            public static readonly GUIContent ShadowCacheGlobalEpochLabel = new("Snapshot Epoch");
            public static readonly GUIContent ShadowCacheGlobalSurfelReadyLabel = new("Surfels Ready");
            public static readonly GUIContent ShadowCacheGlobalSurfelFreshLabel = new("Surfels Fresh");
            public static readonly GUIContent ShadowCacheGlobalSurfelStaleLabel = new("Surfels Stale");
            public static readonly GUIContent ShadowCacheGlobalSurfelInvalidEpochLabel = new("Surfels Invalid Epoch");
            public static readonly GUIContent ShadowCacheGlobalSurfelUninitializedLabel = new("Surfels Uninitialized");
            public static readonly GUIContent ShadowCacheGlobalSurfelMeanLabel = new("Surfels Mean Shadow");
            public static readonly GUIContent ShadowCacheGlobalBrickReadyLabel = new("Bricks Ready");
            public static readonly GUIContent ShadowCacheGlobalBrickFreshLabel = new("Bricks Fresh");
            public static readonly GUIContent ShadowCacheGlobalBrickStaleLabel = new("Bricks Stale");
            public static readonly GUIContent ShadowCacheGlobalBrickHighVarianceLabel = new("Bricks High Variance");
            public static readonly GUIContent ShadowCacheGlobalBrickInvalidEpochLabel = new("Bricks Invalid Epoch");
            public static readonly GUIContent ShadowCacheGlobalBrickUninitializedLabel = new("Bricks Uninitialized");
            public static readonly GUIContent ShadowCacheGlobalBrickMeanLabel = new("Bricks Mean Shadow");

            // Voxel Settings
            public static readonly GUIContent VoxelProbeSizeLabel = new("Voxel Probe Size", "Voxel texture const probe size");

            // Debug Settings
            public static readonly GUIContent BakeResolutionLabel =
                new("Bake Resolution", "Resolution for cubemap baking");

            public static readonly GUIContent ProbeHandleSizeLabel = new("Probe Handle Size", "Size of Probe Handle.");

            public static readonly GUIContent ShadowCacheDebugReadbackIntervalLabel =
                new("Shadow Cache Readback Interval", "Frames between shadow cache debug GPU readbacks.");

            public static readonly GUIContent ShadowCacheDebugShowLabelsLabel =
                new("Shadow Cache Show Labels", "Show the ShadowCache summary and selected-probe labels in the Scene view.");

            public static readonly GUIContent ShadowCacheDebugSurfelSizeLabel =
                new("Shadow Cache Surfel Size", "Size of selected-probe surfel spheres in ShadowCache debug mode.");

            public static readonly GUIContent ShadowCacheDebugSnapshotValidLabel = new("Snapshot Valid");

            public static readonly GUIContent ShadowCacheDebugSnapshotFrameLabel = new("Snapshot Frame");

            public static readonly GUIContent ShadowCacheDebugSnapshotEpochLabel = new("Snapshot Epoch");

            public static readonly GUIContent ShadowCacheDebugSnapshotTimeLabel = new("Snapshot Time");

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
