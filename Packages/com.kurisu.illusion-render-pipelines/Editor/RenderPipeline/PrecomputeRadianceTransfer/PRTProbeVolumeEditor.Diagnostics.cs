using UnityEditor;
using UnityEngine;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    internal partial class PRTProbeVolumeEditor
    {
        private SerializedProperty _debugMode;
        private SerializedProperty _selectedProbeDebugMode;
        private SerializedProperty _probeHandleSize;
        private SerializedProperty _shadowCacheDebugReadbackInterval;
        private SerializedProperty _shadowCacheDebugShowLabels;
        private SerializedProperty _shadowCacheDebugSurfelSize;
        private SerializedProperty _enableShadowCacheStats;
        private SerializedProperty _shadowCacheStatsReadbackInterval;

        private void InitializeDebugProperties()
        {
            _debugMode = Properties.Find(volume => volume.debugMode);
            _selectedProbeDebugMode = Properties.Find(volume => volume.selectedProbeDebugMode);
            _probeHandleSize = Properties.Find(volume => volume.probeHandleSize);
            _shadowCacheDebugReadbackInterval = Properties.Find(volume => volume.shadowCacheDebugReadbackInterval);
            _shadowCacheDebugShowLabels = Properties.Find(volume => volume.shadowCacheDebugShowLabels);
            _shadowCacheDebugSurfelSize = Properties.Find(volume => volume.shadowCacheDebugSurfelSize);
            _enableShadowCacheStats = Properties.Find(volume => volume.enableShadowCacheStats);
            _shadowCacheStatsReadbackInterval = Properties.Find(volume => volume.shadowCacheStatsReadbackInterval);
        }

        private void DrawDebugSettings()
        {
            if (Foldout("Debug", true))
            {
                EditorGUILayout.PropertyField(_debugMode, Styles.Visualization);
                var mode = (ProbeVolumeDebugMode)_debugMode.enumValueIndex;
                EditorGUI.indentLevel++;
                switch (mode)
                {
                    case ProbeVolumeDebugMode.ProbeRadiance:
                        EditorGUILayout.PropertyField(_selectedProbeDebugMode, Styles.ProbeDebugMode);
                        break;
                    case ProbeVolumeDebugMode.ShadowCache:
                        EditorGUILayout.PropertyField(_shadowCacheDebugReadbackInterval, Styles.ShadowReadbackInterval);
                        EditorGUILayout.PropertyField(_shadowCacheDebugShowLabels, Styles.ShadowShowLabels);
                        EditorGUILayout.PropertyField(_shadowCacheDebugSurfelSize, Styles.ShadowSurfelSize);
                        Row("Snapshot", Target.HasShadowCacheDebugSnapshot
                            ? $"Scene tick {Target.LatestShadowCacheDebugFrameIndex}"
                            : "Waiting for readback");
                        break;
                }

                if (mode != ProbeVolumeDebugMode.None)
                    EditorGUILayout.PropertyField(_probeHandleSize, Styles.ProbeHandleSize);

                if (mode == ProbeVolumeDebugMode.ProbeGridWithVirtualOffset)
                {
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        Rect rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        if (GUI.Button(rect, Styles.PlacementPreview))
                            PRTBakeManager.BakePlacementPreview(Target);
                    }
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.PropertyField(_enableShadowCacheStats, Styles.CollectStats);
                if (_enableShadowCacheStats.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_shadowCacheStatsReadbackInterval, Styles.StatsReadbackInterval);
                    EditorGUI.indentLevel--;
                }

                DrawRuntimeStatus();
            }

            EditorGUILayout.Space();
        }

        private void DrawRuntimeStatus()
        {
            if (!Target.isActiveAndEnabled || Target.ResidentSectors == 0 && Target.PublishedGeneration == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            string sectors = $"{Target.ResidentSectors} resident  ·  {Target.UpdatedSectors} updated";
            if (Target.UploadingSectors > 0) sectors += $"  ·  {Target.UploadingSectors} uploading";
            if (Target.EvictingSectors > 0) sectors += $"  ·  {Target.EvictingSectors} evicting";
            Row("Sectors", sectors);
            Row("Sector Memory", $"{FormatBytes(Target.ResidentBytes)}  ·  peak {FormatBytes(Target.PeakResidentBytes)}");
            Row("Probe Memory", $"{FormatBytes(Target.FixedGpuBytes)}  ·  {Target.PyramidLevels} pyramid levels");
            Row("Frame Upload", FormatBytes(Target.FrameUploadBytes));
            if (Target.CascadeBounds.Length > 0)
                Row("Cascades", string.Join("  ·  ", System.Linq.Enumerable.Select(Target.CascadeBounds, b => $"{b.size.x:0}×{b.size.y:0}×{b.size.z:0} m")));
            if (!string.IsNullOrEmpty(Target.ResidencyPressure))
                EditorGUILayout.HelpBox(Target.ResidencyPressure, MessageType.Warning);

            if (!Target.enableShadowCacheStats) return;

            Row("Solver Residual", float.IsFinite(Target.SolverResidual)
                ? $"{Target.SolverResidual:G3} scaled  ·  {Target.SolverAbsoluteResidual:G3} absolute"
                : "Waiting for readback");
            if (Target.SolverNonFiniteCount > 0)
                EditorGUILayout.HelpBox($"Solver produced {Target.SolverNonFiniteCount:N0} non-finite values.", MessageType.Warning);

            if (Target.enableRelightShadow) DrawShadowCacheStats();
        }

        private void DrawShadowCacheStats()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shadow Cache", EditorStyles.boldLabel);
            var stats = Target.LatestShadowCacheStats;
            if (!stats.valid)
            {
                Row("Status", "Waiting for readback");
                return;
            }

            Row("Preview Light", $"{Target.ShadowPreviewLightName} (sector {Target.ShadowPreviewSector})");
            Row("Visibility", $"{stats.shadowmapSamples:N0} shadow map  ·  {stats.cacheHits:N0} cached  ·  " +
                              $"{stats.unknown:N0} unknown  of {stats.evaluated:N0}");
            DrawSurfelSnapshot("Global Surfels", Target.LatestShadowCacheGlobalStats);
            DrawSurfelSnapshot("Window Surfels", Target.LatestShadowCacheWindowStats);
        }

        private static void DrawSurfelSnapshot(string label, PRTProbeVolume.ShadowCacheGlobalStats stats)
        {
            if (!stats.valid || stats.surfelCount == 0)
                return;

            string summary = $"{Percent(stats.surfelReady, stats.surfelCount)} ready  ·  visibility {stats.surfelMeanShadow:F2}";
            if (stats.surfelInvalidEpoch + stats.surfelUninitialized > 0)
                summary += $"  ·  {stats.surfelInvalidEpoch:N0} invalid  ·  {stats.surfelUninitialized:N0} uninitialized";
            Row(label, summary);
        }

        private static void Row(string label, string value) =>
            EditorGUILayout.LabelField(label, value, EditorStyles.wordWrappedLabel);

        private static string Percent(uint count, uint total) => $"{count * 100f / total:0.#}%";

        internal static string FormatBytes(long bytes) => bytes switch
        {
            >= 1 << 20 => $"{bytes / 1048576.0:0.##} MiB",
            >= 1 << 10 => $"{bytes / 1024.0:0.#} KiB",
            _ => $"{bytes} B"
        };
    }
}
