#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        internal struct ShadowCacheDebugEntry
        {
            public float shadow;
            public ShadowCacheDebugStatus status;
            public uint epoch;
        }
        internal struct ShadowCacheProbeDebugSummary
        {
            public bool valid;
            public ShadowCacheDebugStatus status;
            public float meanShadow;
            public uint cachedCount, uncoveredCount;
        }
        internal ShadowCacheDebugEntry[] LatestShadowCacheDebugEntries { get; private set; }
        internal ShadowCacheProbeDebugSummary[] LatestShadowCacheProbeSummaries { get; private set; }
        internal bool HasShadowCacheDebugSnapshot { get; private set; }
        internal uint LatestShadowCacheDebugEpoch { get; private set; }
        internal uint LatestShadowCacheDebugFrameIndex { get; private set; }
        internal double LatestShadowCacheDebugTime { get; private set; }
        internal string ShadowDebugPreviewLightName { get; private set; }
        private bool _shadowDebugPending, _hasShadowDebugFrame;
        private uint _shadowDebugFrame, _shadowDebugEpoch;
        private string _shadowDebugName;
        internal int ShadowDebugSector { get; private set; }
        internal bool IsShadowCacheDebugActive => debugMode == ProbeVolumeDebugMode.ShadowCache;
        internal bool ShouldRequestShadowCacheDebugReadback(uint frame) => IsShadowCacheDebugActive && !_shadowDebugPending &&
            (!_hasShadowDebugFrame || frame - _shadowDebugFrame >= (uint)Mathf.Max(1, shadowCacheDebugReadbackInterval));

        internal void BeginWorldShadowDebug(uint epoch, uint frame, string name)
        {
            _shadowDebugPending = true;
            _hasShadowDebugFrame = true;
            _shadowDebugFrame = frame;
            _shadowDebugEpoch = epoch;
            _shadowDebugName = name;
        }

        internal void UpdateWorldShadowDebug(AsyncGPUReadbackRequest request, int sector)
        {
            _shadowDebugPending = false;
            if (request.hasError)
                return;
            var values = request.GetData<uint>();
            var entries = new ShadowCacheDebugEntry[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                uint epoch = values[i] >> 8;
                bool ready = epoch != 0 && epoch == _shadowDebugEpoch;
                entries[i] = new ShadowCacheDebugEntry
                {
                    shadow = ready ? (values[i] & 0xFF) / 255f : 1f, epoch = epoch,
                    status = ready ? ShadowCacheDebugStatus.Cached : ShadowCacheDebugStatus.Uncovered
                };
            }
            ShadowDebugSector = sector;
            LatestShadowCacheDebugEntries = entries;
            LatestShadowCacheDebugEpoch = _shadowDebugEpoch;
            LatestShadowCacheDebugFrameIndex = _shadowDebugFrame;
            LatestShadowCacheDebugTime = Time.realtimeSinceStartupAsDouble;
            ShadowDebugPreviewLightName = _shadowDebugName;
            HasShadowCacheDebugSnapshot = true;
            RebuildShadowCacheProbeSummaries();
        }

        private void RebuildShadowCacheProbeSummaries()
        {
            if (_allProbes == null || LatestShadowCacheDebugEntries == null)
                return;
            var summaries = new ShadowCacheProbeDebugSummary[_allProbes.Length];
            for (int index = 0; index < summaries.Length; index++)
            {
                if (asset.SectorIndex(index) != ShadowDebugSector) continue;
                var data = asset.Sectors[ShadowDebugSector];
                var range = data.probes[asset.LocalProbeIndex(index)];
                var summary = new ShadowCacheProbeDebugSummary();
                uint count = 0;
                float sum = 0;
                for (int factor = range.factorStart; factor < range.factorStart + range.factorCount; factor++)
                {
                    var brick = data.bricks[data.factors[factor].BrickIndex];
                    for (int surfel = brick.start; surfel < brick.start + brick.count; surfel++)
                    {
                        var entry = LatestShadowCacheDebugEntries[surfel];
                        count++;
                        sum += Mathf.Clamp01(entry.shadow);
                        if (entry.status == ShadowCacheDebugStatus.Cached) summary.cachedCount++;
                        else summary.uncoveredCount++;
                    }
                }
                summary.valid = count > 0;
                summary.meanShadow = count > 0 ? sum / count : 0;
                summary.status = summary.uncoveredCount > 0 ? ShadowCacheDebugStatus.Uncovered : ShadowCacheDebugStatus.Cached;
                summaries[index] = summary;
            }
            LatestShadowCacheProbeSummaries = summaries;
        }

        internal void ClearShadowCacheDebugSnapshot()
        {
            _shadowDebugPending = _hasShadowDebugFrame = false;
            LatestShadowCacheDebugEntries = null;
            LatestShadowCacheProbeSummaries = null;
            HasShadowCacheDebugSnapshot = false;
            LatestShadowCacheDebugEpoch = LatestShadowCacheDebugFrameIndex = 0;
            LatestShadowCacheDebugTime = 0;
            ShadowDebugPreviewLightName = null;
        }
    }
}
#endif
