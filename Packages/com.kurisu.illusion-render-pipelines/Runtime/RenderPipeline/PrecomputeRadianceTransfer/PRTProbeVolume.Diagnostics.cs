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
            // Matches the counter indices in WorldLighting.hlsl.
            internal const int CounterCount = 5;
            public readonly bool valid;
            public readonly uint evaluated, cacheHits, uncovered, unknown, shadowmapSamples;
            public ShadowCacheStats(uint[] counters)
            {
                valid = true;
                evaluated = counters[0]; cacheHits = counters[1]; uncovered = counters[2];
                unknown = counters[3]; shadowmapSamples = counters[4];
            }
        }

        public struct ShadowCacheGlobalStats
        {
            public bool valid;
            public uint epoch, surfelCount, surfelReady, surfelInvalidEpoch, surfelUninitialized;
            public float surfelMeanShadow;
        }

        public ShadowCacheStats LatestShadowCacheStats { get; private set; }
        public ShadowCacheGlobalStats LatestShadowCacheGlobalStats { get; private set; }
        public ShadowCacheGlobalStats LatestShadowCacheWindowStats { get; private set; }
        private bool _globalStatsPending, _hasGlobalStatsFrame;
        private uint _globalStatsFrame, _globalStatsEpoch;
        private string _globalPreviewName;
        private int _globalPreviewId;

        internal void UpdateShadowCacheStats(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
                return;
            var values = request.GetData<uint>();
            if (values.Length < ShadowCacheStats.CounterCount)
                return;
            var counters = new uint[ShadowCacheStats.CounterCount];
            for (int i = 0; i < counters.Length; i++)
                counters[i] = values[i];
            LatestShadowCacheStats = new ShadowCacheStats(counters);
        }

        internal bool ShouldRequestShadowCacheGlobalStatsReadback(uint frame)
        {
            return enableShadowCacheStats && !_globalStatsPending && (!_hasGlobalStatsFrame ||
                frame - _globalStatsFrame >= (uint)Mathf.Max(1, shadowCacheStatsReadbackInterval));
        }

        internal void BeginWorldShadowStats(uint epoch, uint frame, string name, int id)
        {
            _globalStatsPending = true;
            _hasGlobalStatsFrame = true;
            _globalStatsEpoch = epoch;
            _globalStatsFrame = frame;
            _globalPreviewName = name;
            _globalPreviewId = id;
        }

        internal void UpdateWorldShadowStats(AsyncGPUReadbackRequest request, int sector)
        {
            _globalStatsPending = false;
            if (request.hasError)
                return;
            var entries = request.GetData<uint>();
            var global = new ShadowCacheGlobalStats { valid = true, epoch = _globalStatsEpoch };
            var window = global;
            ShadowPreviewSector = sector;
            var indices = CurrentWindowSurfelIndices(sector);
            for (int i = 0; i < entries.Length; i++)
            {
                AddShadowEntry(ref global, entries[i]);
                if (indices.Contains(i))
                    AddShadowEntry(ref window, entries[i]);
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

        private static void AddShadowEntry(ref ShadowCacheGlobalStats stats, uint entry)
        {
            stats.surfelCount++;
            uint epoch = entry >> 8;
            if (epoch == 0) { stats.surfelUninitialized++; return; }
            if (epoch != stats.epoch) { stats.surfelInvalidEpoch++; return; }
            stats.surfelReady++;
            stats.surfelMeanShadow += (entry & 0xFF) / 255f;
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
                    int brick = data.factors[i].BrickIndex;
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
