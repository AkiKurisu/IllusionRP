using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        public readonly struct ShadowCacheStats
        {
            public readonly bool valid;
            public readonly uint evaluated, cacheHits, cacheMisses, invalidByEpoch, invalidByAge;
            public readonly uint shadowmapSamples, fallbackFromCache, uncoveredNoCache;
            public ShadowCacheStats(uint[] counters)
            {
                valid = true;
                evaluated = counters[0]; cacheHits = counters[1]; cacheMisses = counters[2];
                invalidByEpoch = counters[3]; invalidByAge = counters[4];
                shadowmapSamples = counters[5]; fallbackFromCache = counters[6]; uncoveredNoCache = counters[7];
            }
        }

        internal struct ShadowCacheSnapshotEntry
        {
            public float shadow;
            public uint epoch, lastUpdateFrame, valid;
        }

        public struct ShadowCacheGlobalStats
        {
            public bool valid;
            public uint epoch, frameIndex, surfelCount, surfelReady, surfelFresh, surfelStale;
            public uint surfelInvalidEpoch, surfelUninitialized;
            public float surfelMeanShadow;
        }

        public ShadowCacheStats LatestShadowCacheStats { get; private set; }
        public ShadowCacheGlobalStats LatestShadowCacheGlobalStats { get; private set; }
        public ShadowCacheGlobalStats LatestShadowCacheWindowStats { get; private set; }
        private bool _globalStatsPending, _hasGlobalStatsFrame;
        private uint _globalStatsFrame, _globalStatsEpoch, _globalStatsAge;
        private string _globalPreviewName;
        private int _globalPreviewId;

        internal void UpdateShadowCacheStats(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
                return;
            var values = request.GetData<uint>();
            if (values.Length < 8)
                return;
            var counters = new uint[8];
            for (int i = 0; i < counters.Length; i++)
                counters[i] = values[i];
            LatestShadowCacheStats = new ShadowCacheStats(counters);
        }

        internal bool ShouldRequestShadowCacheGlobalStatsReadback(uint frame)
        {
            return enableShadowCacheStats && !_globalStatsPending && (!_hasGlobalStatsFrame ||
                frame - _globalStatsFrame >= (uint)Mathf.Max(1, shadowCacheStatsReadbackInterval));
        }

        internal void BeginWorldShadowStats(uint epoch, uint frame, uint maxAge, string name, int id)
        {
            _globalStatsPending = true;
            _hasGlobalStatsFrame = true;
            _globalStatsEpoch = epoch;
            _globalStatsFrame = frame;
            _globalStatsAge = maxAge;
            _globalPreviewName = name;
            _globalPreviewId = id;
        }

        internal void UpdateWorldShadowStats(AsyncGPUReadbackRequest request, int sector)
        {
            _globalStatsPending = false;
            if (request.hasError)
                return;
            var entries = request.GetData<ShadowCacheSnapshotEntry>();
            var global = new ShadowCacheGlobalStats { valid = true, epoch = _globalStatsEpoch, frameIndex = _globalStatsFrame };
            var window = global;
            ShadowPreviewSector = sector;
            var indices = CurrentWindowSurfelIndices(sector);
            for (int i = 0; i < entries.Length; i++)
            {
                AddShadowEntry(ref global, entries[i], _globalStatsAge);
                if (indices.Contains(i))
                    AddShadowEntry(ref window, entries[i], _globalStatsAge);
            }
            if (global.surfelReady > 0)
                global.surfelMeanShadow /= global.surfelReady;
            if (window.surfelReady > 0)
                window.surfelMeanShadow /= window.surfelReady;
            LatestShadowCacheGlobalStats = global;
            LatestShadowCacheWindowStats = window;
            ShadowPreviewLightName = _globalPreviewName;
            ShadowPreviewLightId = _globalPreviewId;
        }

        private static void AddShadowEntry(ref ShadowCacheGlobalStats stats, ShadowCacheSnapshotEntry entry, uint maxAge)
        {
            stats.surfelCount++;
            if (entry.valid == 0) { stats.surfelUninitialized++; return; }
            if (entry.epoch != stats.epoch) { stats.surfelInvalidEpoch++; return; }
            stats.surfelReady++;
            stats.surfelMeanShadow += Mathf.Clamp01(entry.shadow);
            if (stats.frameIndex - entry.lastUpdateFrame <= maxAge)
                stats.surfelFresh++;
            else
                stats.surfelStale++;
        }

        private HashSet<int> CurrentWindowSurfelIndices(int sector)
        {
            var indices = new HashSet<int>();
            var bricks = new HashSet<int>();
            if (_allProbes == null)
                return indices;
            foreach (var probe in _probesInBoundingBox)
            {
                if (asset.SectorIndex(probe.Index) != sector) continue;
                var data = asset.Sectors[sector];
                var range = data.probes[asset.LocalProbeIndex(probe.Index)];
                for (int i = range.factorStart; i < range.factorStart + range.factorCount; i++)
                {
                    int brick = data.factors[i].brickIndex;
                    if (!bricks.Add(brick))
                        continue;
                    var surfels = data.bricks[brick];
                    for (int index = surfels.start; index < surfels.start + surfels.count; index++)
                        indices.Add(index);
                }
            }
            return indices;
        }

        internal void ClearShadowCacheGlobalStats()
        {
            _globalStatsPending = _hasGlobalStatsFrame = false;
            LatestShadowCacheStats = default;
            LatestShadowCacheGlobalStats = LatestShadowCacheWindowStats = default;
            ShadowPreviewLightName = null;
            ShadowPreviewLightId = 0;
        }
    }
}
