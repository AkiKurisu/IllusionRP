using UnityEngine;
using UnityEditor;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PRTProbeVolume))]
    internal class PRTProbeVolumeEditor : PropertyFetchEditor<PRTProbeVolume>
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
        private SerializedProperty _multiFrameRelight;
        private SerializedProperty _enableRelightShadow;
        private SerializedProperty _shadowCacheMaxAge;
        private SerializedProperty _shadowCacheVarianceThreshold;
        private SerializedProperty _enableShadowCacheStats;
        private SerializedProperty _shadowCacheStatsReadbackInterval;
        private SerializedProperty _probesPerFrameUpdate;
        private SerializedProperty _localProbeCount;
        private SerializedProperty _relightOnceUntilDirty;

        // Voxel Settings
        private SerializedProperty _voxelProbeSize;

        // Asset
        private SerializedProperty _asset;

        // Debug Settings
        private SerializedProperty _debugMode;
        private SerializedProperty _probeHandleSize;
        private SerializedProperty _shadowCacheDebugReadbackInterval;
        private SerializedProperty _shadowCacheDebugShowLabels;
        private SerializedProperty _shadowCacheDebugSurfelSize;
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
            _enableRelightShadow = Properties.Find(volume => volume.enableRelightShadow);
            _shadowCacheMaxAge = Properties.Find(volume => volume.shadowCacheMaxAge);
            _shadowCacheVarianceThreshold = Properties.Find(volume => volume.shadowCacheVarianceThreshold);
            _enableShadowCacheStats = Properties.Find(volume => volume.enableShadowCacheStats);
            _shadowCacheStatsReadbackInterval = Properties.Find(volume => volume.shadowCacheStatsReadbackInterval);
            _probesPerFrameUpdate = Properties.Find(volume => volume.probesPerFrameUpdate);
            _localProbeCount = Properties.Find(volume => volume.localProbeCount);
            _relightOnceUntilDirty = Properties.Find(volume => volume.relightOnceUntilDirty);

            // Voxel Settings
            _voxelProbeSize = Properties.Find(volume => volume.voxelProbeSize);

            // Asset
            _asset = Properties.Find(volume => volume.asset);

            // Debug Settings
            _debugMode = Properties.Find(volume => volume.debugMode);
            _probeHandleSize = Properties.Find(volume => volume.probeHandleSize);
            _shadowCacheDebugReadbackInterval = Properties.Find(volume => volume.shadowCacheDebugReadbackInterval);
            _shadowCacheDebugShowLabels = Properties.Find(volume => volume.shadowCacheDebugShowLabels);
            _shadowCacheDebugSurfelSize = Properties.Find(volume => volume.shadowCacheDebugSurfelSize);
            _bakeResolution = Properties.Find(volume => volume.bakeResolution);

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
                !Target.relightOnceUntilDirty)
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

                EditorGUILayout.PropertyField(_relightOnceUntilDirty, Styles.RelightOnceUntilDirtyLabel);
                if (_relightOnceUntilDirty.boolValue)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Toggle(Styles.RelightWindowCycleCompleteLabel,
                            Target.RelightWindowCycleComplete);
                        EditorGUILayout.TextField(Styles.RelightWindowCoverageLabel,
                            $"{Target.RelitProbeCountInWindow} / {Target.ProbeCountInWindow}");
                        EditorGUILayout.IntField(Styles.RelightCurrentProbeIndexLabel,
                            Target.CurrentProbeUpdateIndex);
                        EditorGUILayout.Toggle(Styles.RelightInputTrackedLabel,
                            Target.HasRelightInputHash);
                        EditorGUILayout.IntField(Styles.RelightInputHashLabel,
                            Target.LastRelightInputHash);
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(_enableRelightShadow, Styles.EnableRelightShadowLabel);
                if (_enableRelightShadow.boolValue)
                {
                    EditorGUILayout.PropertyField(_shadowCacheMaxAge, Styles.ShadowCacheMaxAgeLabel);
                    EditorGUILayout.PropertyField(_shadowCacheVarianceThreshold, Styles.ShadowCacheVarianceThresholdLabel);
                    EditorGUILayout.PropertyField(_enableShadowCacheStats, Styles.EnableShadowCacheStatsLabel);

                    if (_enableShadowCacheStats.boolValue)
                    {
                        DrawShadowCacheStats();
                    }
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawShadowCacheStats()
        {
            EditorGUILayout.PropertyField(_shadowCacheStatsReadbackInterval,
                Styles.ShadowCacheStatsReadbackIntervalLabel);

            var stats = Target.LatestShadowCacheStats;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(Styles.ShadowCacheDispatchStatsLabel, EditorStyles.boldLabel);
                EditorGUILayout.Toggle(Styles.ShadowCacheStatsValidLabel, stats.valid);
                EditorGUILayout.LongField(Styles.ShadowCacheEvaluatedLabel, stats.evaluated);
                EditorGUILayout.LongField(Styles.ShadowCacheHitsLabel, stats.cacheHits);
                EditorGUILayout.LongField(Styles.ShadowCacheMissesLabel, stats.cacheMisses);
                EditorGUILayout.LongField(Styles.ShadowCacheInvalidEpochLabel, stats.invalidByEpoch);
                EditorGUILayout.LongField(Styles.ShadowCacheInvalidAgeLabel, stats.invalidByAge);
                EditorGUILayout.LongField(Styles.ShadowCacheSamplesLabel, stats.shadowmapSamples);
                EditorGUILayout.LongField(Styles.ShadowCacheFallbackLabel, stats.fallbackFromCache);
                EditorGUILayout.LongField(Styles.ShadowCacheUncoveredLabel, stats.uncoveredNoCache);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(Styles.ShadowCacheGlobalStatsLabel, EditorStyles.boldLabel);
                DrawShadowCacheSnapshotStats(Target.LatestShadowCacheGlobalStats);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(Styles.ShadowCacheWindowStatsLabel, EditorStyles.boldLabel);
                DrawShadowCacheSnapshotStats(Target.LatestShadowCacheWindowStats);
            }
        }

        private static void DrawShadowCacheSnapshotStats(PRTProbeVolume.ShadowCacheGlobalStats stats)
        {
            EditorGUILayout.Toggle(Styles.ShadowCacheStatsValidLabel, stats.valid);
            EditorGUILayout.LongField(Styles.ShadowCacheGlobalFrameLabel, stats.frameIndex);
            EditorGUILayout.LongField(Styles.ShadowCacheGlobalEpochLabel, stats.epoch);
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalSurfelReadyLabel,
                FormatCount(stats.surfelReady, stats.surfelCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalSurfelFreshLabel,
                FormatCount(stats.surfelFresh, stats.surfelCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalSurfelStaleLabel,
                FormatCount(stats.surfelStale, stats.surfelCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalSurfelInvalidEpochLabel,
                FormatCount(stats.surfelInvalidEpoch, stats.surfelCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalSurfelUninitializedLabel,
                FormatCount(stats.surfelUninitialized, stats.surfelCount));
            EditorGUILayout.FloatField(Styles.ShadowCacheGlobalSurfelMeanLabel,
                stats.surfelMeanShadow);
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalBrickReadyLabel,
                FormatCount(stats.brickReady, stats.brickCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalBrickFreshLabel,
                FormatCount(stats.brickFresh, stats.brickCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalBrickStaleLabel,
                FormatCount(stats.brickStale, stats.brickCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalBrickHighVarianceLabel,
                FormatCount(stats.brickHighVariance, stats.brickCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalBrickInvalidEpochLabel,
                FormatCount(stats.brickInvalidEpoch, stats.brickCount));
            EditorGUILayout.TextField(Styles.ShadowCacheGlobalBrickUninitializedLabel,
                FormatCount(stats.brickUninitialized, stats.brickCount));
            EditorGUILayout.FloatField(Styles.ShadowCacheGlobalBrickMeanLabel,
                stats.brickMeanShadow);
        }

        private static string FormatCount(uint count, uint total)
        {
            if (total == 0)
            {
                return count.ToString();
            }

            float percent = count * 100f / total;
            return $"{count} / {total} ({percent:F1}%)";
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
            public static readonly GUIContent MultiFrameRelightLabel = new("Multi Frame Relight", "Enable multi frame relight to improve performance");
            public static readonly GUIContent EnableRelightShadowLabel = new("Enable Relight Shadow", "Enable shadow calculation in PRT relight. Disable this to avoid shadow flickering when camera moves");
            public static readonly GUIContent ShadowCacheMaxAgeLabel = new("Shadow Cache Max Age", "Maximum number of frames before cached PRT shadow values must be refreshed.");
            public static readonly GUIContent ShadowCacheVarianceThresholdLabel = new("Shadow Cache Variance Threshold", "Maximum per-brick shadow variance that can reuse the brick shadow cache.");
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
            public static readonly GUIContent ProbesPerFrameUpdateLabel = new("Probes Per Frame Update", "Number of probes to update per frame");
            public static readonly GUIContent LocalProbeCountLabel = new("Local Probe Count", "Number of camera nearby probes to relight in additional to per frame update roulette");
            public static readonly GUIContent RelightOnceUntilDirtyLabel =
                new("Relight Once Until Dirty", "Stop relighting after a full window cycle, then restart when relight inputs change.");
            public static readonly GUIContent RelightWindowCycleCompleteLabel = new("Window Cycle Complete");
            public static readonly GUIContent RelightWindowCoverageLabel = new("Window Relit Probes");
            public static readonly GUIContent RelightCurrentProbeIndexLabel = new("Round Robin Index");
            public static readonly GUIContent RelightInputTrackedLabel = new("Relight Input Tracked");
            public static readonly GUIContent RelightInputHashLabel = new("Relight Input Hash");

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
