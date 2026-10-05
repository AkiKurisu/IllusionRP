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
            public uint status, age, epoch;
        }
        internal struct ShadowCacheProbeDebugSummary
        {
            public bool valid;
            public ShadowCacheDebugStatus status;
            public float meanShadow;
            public uint unknownCount, freshHitCount, sampledCount, fallbackCount, uncoveredCount;
        }
        internal ShadowCacheDebugEntry[] LatestShadowCacheDebugEntries { get; private set; }
        internal ShadowCacheProbeDebugSummary[] LatestShadowCacheProbeSummaries { get; private set; }
        internal bool HasShadowCacheDebugSnapshot { get; private set; }
        internal uint LatestShadowCacheDebugEpoch { get; private set; }
        internal uint LatestShadowCacheDebugFrameIndex { get; private set; }
        internal double LatestShadowCacheDebugTime { get; private set; }
        internal string ShadowDebugPreviewLightName { get; private set; }
        private bool _shadowDebugPending, _hasShadowDebugFrame;
        private uint _shadowDebugFrame, _shadowDebugEpoch, _shadowDebugAge;
        private string _shadowDebugName;
        internal int ShadowDebugSector { get; private set; }
        internal bool IsShadowCacheDebugActive => debugMode == ProbeVolumeDebugMode.ShadowCache;
        internal System.Collections.Generic.IEnumerable<(int index, PRTProbeDebugData data)> CreatedProbeDebugData()
        {
            if (_probeDebugData == null)
                yield break;
            for (int i = 0; i < _probeDebugData.Length; i++)
                if (_probeDebugData[i] != null)
                    yield return (i, _probeDebugData[i]);
        }
        internal bool ShouldRequestShadowCacheDebugReadback(uint frame) => IsShadowCacheDebugActive && !_shadowDebugPending &&
            (!_hasShadowDebugFrame || frame - _shadowDebugFrame >= (uint)Mathf.Max(1, shadowCacheDebugReadbackInterval));

        internal void BeginWorldShadowDebug(uint epoch, uint frame, uint maxAge, string name)
        {
            _shadowDebugPending = true;
            _hasShadowDebugFrame = true;
            _shadowDebugFrame = frame;
            _shadowDebugEpoch = epoch;
            _shadowDebugAge = maxAge;
            _shadowDebugName = name;
        }

        internal void UpdateWorldShadowDebug(AsyncGPUReadbackRequest request, int sector)
        {
            _shadowDebugPending = false;
            if (request.hasError)
                return;
            var values = request.GetData<ShadowCacheSnapshotEntry>();
            var entries = new ShadowCacheDebugEntry[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                uint age = _shadowDebugFrame - value.lastUpdateFrame;
                bool ready = value.valid != 0 && value.epoch == _shadowDebugEpoch;
                entries[i] = new ShadowCacheDebugEntry
                {
                    shadow = ready ? value.shadow : 1f, epoch = value.epoch, age = age,
                    status = ready ? age <= _shadowDebugAge ? 1u : 3u : 4u
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
                    var brick = data.bricks[data.factors[factor].brickIndex];
                    for (int surfel = brick.start; surfel < brick.start + brick.count; surfel++)
                    {
                        var entry = LatestShadowCacheDebugEntries[surfel];
                        count++;
                        sum += Mathf.Clamp01(entry.shadow);
                        if (entry.status == 1) summary.freshHitCount++;
                        else if (entry.status == 3) summary.fallbackCount++;
                        else summary.uncoveredCount++;
                    }
                }
                summary.valid = count > 0;
                summary.meanShadow = count > 0 ? sum / count : 0;
                summary.status = summary.uncoveredCount > 0 ? ShadowCacheDebugStatus.UncoveredNoCache
                    : summary.fallbackCount > 0 ? ShadowCacheDebugStatus.FallbackFromCache : ShadowCacheDebugStatus.FreshHit;
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
