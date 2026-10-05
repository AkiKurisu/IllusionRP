#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {

        private void DrawShadowCacheProbe(int probeIndex, Vector3 probePos)
        {
            if (!HasShadowCacheDebugSnapshot ||
                LatestShadowCacheProbeSummaries == null ||
                probeIndex < 0 ||
                probeIndex >= LatestShadowCacheProbeSummaries.Length ||
                !LatestShadowCacheProbeSummaries[probeIndex].valid)
            {
                Gizmos.color = Color.gray;
                Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.08f);
                return;
            }

            var summary = LatestShadowCacheProbeSummaries[probeIndex];
            Gizmos.color = GetShadowCacheStatusColor(summary.status, summary.meanShadow);
            float radius = probeHandleSize * Mathf.Lerp(0.06f, 0.14f, 1f - Mathf.Clamp01(summary.meanShadow));
            Gizmos.DrawSphere(probePos, radius);

            if (probeIndex == selectedProbeIndex)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.22f);
            }
        }



        private void DrawShadowCacheVolumeSummary()
        {
            if (!shadowCacheDebugShowLabels ||
                !HasShadowCacheDebugSnapshot ||
                LatestShadowCacheProbeSummaries == null)
            {
                return;
            }

            int validProbeCount = 0;
            uint sampledCount = 0;
            uint fallbackCount = 0;
            uint hitCount = 0;
            uint uncoveredCount = 0;
            uint unknownCount = 0;

            for (int i = 0; i < LatestShadowCacheProbeSummaries.Length; i++)
            {
                var summary = LatestShadowCacheProbeSummaries[i];
                if (!summary.valid)
                {
                    continue;
                }

                validProbeCount++;
                sampledCount += summary.sampledCount;
                fallbackCount += summary.fallbackCount;
                hitCount += summary.freshHitCount;
                uncoveredCount += summary.uncoveredCount;
                unknownCount += summary.unknownCount;
            }

            Handles.Label(GetShadowCacheSummaryLabelPosition(),
                $"Shadow preview {ShadowDebugPreviewLightName} Frame:{LatestShadowCacheDebugFrameIndex} Epoch:{LatestShadowCacheDebugEpoch}\n" +
                $"Probes:{validProbeCount}/{LatestShadowCacheProbeSummaries.Length} " +
                $"S:{sampledCount} F:{fallbackCount} H:{hitCount} U:{uncoveredCount} ?:{unknownCount}");
        }



        private Vector3 GetShadowCacheSummaryLabelPosition()
        {
            if (_currentBoundingBox.size != Vector3.zero)
            {
                return _currentBoundingBox.center +
                       Vector3.up * (_currentBoundingBox.extents.y + Mathf.Max(probeGridSize, 1f) * 0.5f);
            }

            return transform.position + Vector3.up * Mathf.Max(probeGridSize, 1f);
        }



        private void DrawSelectedProbeShadowCache(PRTProbe probe, Vector3 probePos, PRTProbeDebugData debugData)
        {
            if (!HasShadowCacheDebugSnapshot || debugData == null || asset.SectorIndex(probe.Index) != ShadowDebugSector)
            {
                Handles.Label(probePos + Vector3.up * probeHandleSize * 0.8f,
                    "No shadow cache snapshot");
                return;
            }

            DrawSelectedProbeShadowCacheSummary(probe.Index, probePos);
            DrawSelectedProbeShadowCacheSurfels(debugData);
        }



        private void DrawSelectedProbeShadowCacheSummary(int probeIndex, Vector3 probePos)
        {
            if (!shadowCacheDebugShowLabels ||
                LatestShadowCacheProbeSummaries == null ||
                probeIndex < 0 ||
                probeIndex >= LatestShadowCacheProbeSummaries.Length)
            {
                return;
            }

            var summary = LatestShadowCacheProbeSummaries[probeIndex];
            uint hitCount = summary.freshHitCount;
            Handles.Label(probePos + Vector3.up * probeHandleSize * 0.85f,
                $"ShadowCache F:{LatestShadowCacheDebugFrameIndex} E:{LatestShadowCacheDebugEpoch} " +
                $"Avg:{summary.meanShadow:0.00} S:{summary.sampledCount} F:{summary.fallbackCount} H:{hitCount} U:{summary.uncoveredCount}");
        }



        private void DrawSelectedProbeShadowCacheSurfels(PRTProbeDebugData debugData)
        {
            if (LatestShadowCacheDebugEntries == null ||
                debugData.LocalSurfels == null ||
                debugData.LocalSurfelIndices == null)
            {
                return;
            }

            float surfelSize = Mathf.Max(0.001f, shadowCacheDebugSurfelSize);
            int count = Mathf.Min(debugData.LocalSurfels.Length, debugData.LocalSurfelIndices.Length);
            for (int i = 0; i < count; i++)
            {
                int surfelIndex = debugData.LocalSurfelIndices[i];
                if (surfelIndex < 0 || surfelIndex >= LatestShadowCacheDebugEntries.Length)
                {
                    Gizmos.color = GetShadowCacheStatusColor(ShadowCacheDebugStatus.Unknown, 1f);
                    Gizmos.DrawWireSphere(debugData.LocalSurfels[i].position, surfelSize);
                    continue;
                }

                var entry = LatestShadowCacheDebugEntries[surfelIndex];
                var status = (ShadowCacheDebugStatus)entry.status;
                Gizmos.color = GetShadowCacheStatusColor(status, entry.shadow);
                Gizmos.DrawSphere(debugData.LocalSurfels[i].position, surfelSize);
            }
        }




        private static Color GetShadowCacheStatusColor(ShadowCacheDebugStatus status, float shadow)
        {
            Color baseColor = status switch
            {
                ShadowCacheDebugStatus.FreshHit => new Color(0.1f, 0.9f, 0.25f, 0.85f),
                ShadowCacheDebugStatus.Sampled => new Color(0f, 0.85f, 1f, 0.85f),
                ShadowCacheDebugStatus.FallbackFromCache => new Color(0.1f, 0.35f, 1f, 0.9f),
                ShadowCacheDebugStatus.UncoveredNoCache => new Color(1f, 0.15f, 0.1f, 0.9f),
                _ => new Color(0.45f, 0.45f, 0.45f, 0.75f)
            };

            float brightness = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(shadow));
            baseColor.r *= brightness;
            baseColor.g *= brightness;
            baseColor.b *= brightness;
            return baseColor;
        }
    }
}
#endif
