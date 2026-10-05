using UnityEditor;
using UnityEngine;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    internal partial class PRTProbeVolumeEditor
    {
        private void DrawSolverStatus()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Sectors Updated", Target.UpdatedSectors);
                EditorGUILayout.IntField("Resident Sectors", Target.ResidentSectors);
                EditorGUILayout.IntField("Uploading Sectors", Target.UploadingSectors);
                EditorGUILayout.IntField("Evicting Sectors", Target.EvictingSectors);
                EditorGUILayout.LongField("Sector GPU Bytes", Target.ResidentBytes);
                EditorGUILayout.LongField("Peak Sector GPU Bytes", Target.PeakResidentBytes);
                EditorGUILayout.LongField("Global Probe GPU Bytes", Target.FixedGpuBytes);
                EditorGUILayout.IntField("Uploaded Bytes", Target.FrameUploadBytes);
                EditorGUILayout.IntField("Shadow Preview Sector", Target.ShadowPreviewSector);
                EditorGUILayout.FloatField("Scaled Residual (diagnostic)", Target.SolverResidual);
                EditorGUILayout.FloatField("Absolute Residual", Target.SolverAbsoluteResidual);
                EditorGUILayout.IntField("Non-finite Values", Target.SolverNonFiniteCount);
                EditorGUILayout.LongField("Published Revision", Target.PublishedGeneration);
                if (!string.IsNullOrEmpty(Target.ResidencyPressure)) EditorGUILayout.HelpBox(Target.ResidencyPressure, MessageType.Warning);
            }
        }

        private void DrawShadowCacheStats()
        {
            EditorGUILayout.PropertyField(_shadowCacheStatsReadbackInterval,
                Styles.ShadowCacheStatsReadbackIntervalLabel);
            var stats = Target.LatestShadowCacheStats;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Stats Valid", stats.valid);
                EditorGUILayout.LongField("Evaluated", stats.evaluated);
                EditorGUILayout.LongField("Cache Hits", stats.cacheHits);
                EditorGUILayout.LongField("Cache Misses", stats.cacheMisses);
                EditorGUILayout.LongField("Invalid Epoch", stats.invalidByEpoch);
                EditorGUILayout.LongField("Old Cached Samples", stats.invalidByAge);
                EditorGUILayout.LongField("Existing Map Samples", stats.shadowmapSamples);
                EditorGUILayout.LongField("Cached Samples Outside Map", stats.fallbackFromCache);
                EditorGUILayout.LongField("Unknown Visibility", stats.uncoveredNoCache);
                EditorGUILayout.TextField("Preview Light", Target.ShadowPreviewLightName ?? string.Empty);
                EditorGUILayout.IntField("Preview Light ID", Target.ShadowPreviewLightId);
                EditorGUILayout.LabelField("Global Preview", EditorStyles.boldLabel);
                DrawShadowCacheSnapshotStats(Target.LatestShadowCacheGlobalStats);
                EditorGUILayout.LabelField("Window Preview", EditorStyles.boldLabel);
                DrawShadowCacheSnapshotStats(Target.LatestShadowCacheWindowStats);
            }
        }

        private static void DrawShadowCacheSnapshotStats(PRTProbeVolume.ShadowCacheGlobalStats stats)
        {
            EditorGUILayout.Toggle("Snapshot Valid", stats.valid);
            EditorGUILayout.LongField("Scene Tick", stats.frameIndex);
            EditorGUILayout.LongField("Visibility Epoch", stats.epoch);
            EditorGUILayout.TextField("Ready", FormatCount(stats.surfelReady, stats.surfelCount));
            EditorGUILayout.TextField("Fresh", FormatCount(stats.surfelFresh, stats.surfelCount));
            EditorGUILayout.TextField("Old Samples", FormatCount(stats.surfelStale, stats.surfelCount));
            EditorGUILayout.TextField("Invalid Epoch", FormatCount(stats.surfelInvalidEpoch, stats.surfelCount));
            EditorGUILayout.TextField("Uninitialized", FormatCount(stats.surfelUninitialized, stats.surfelCount));
            EditorGUILayout.FloatField("Mean Visibility", stats.surfelMeanShadow);
        }

        private static string FormatCount(uint count, uint total) => total == 0
            ? count.ToString() : $"{count} / {total} ({count * 100f / total:F1}%)";
    }
}
