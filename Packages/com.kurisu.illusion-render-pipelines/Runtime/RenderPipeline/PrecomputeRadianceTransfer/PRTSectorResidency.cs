using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTSectorResidency : IDisposable
    {
        private readonly PRTProbeVolumeAsset _asset;
        private readonly Dictionary<int, PRTSectorResident> _residents = new();
        private readonly Dictionary<int, Dictionary<uint, PRTWorldShadowCacheEntry[]>> _saved = new();
        private readonly List<PRTSectorResident> _completed = new();
        private const uint EvictionPatience = 4;
        private readonly List<(PRTSectorResident resident, AsyncGPUReadbackRequest request, uint frame)> _evictions = new();
        private readonly HashSet<int> _protected = new();
        public long Bytes { get; private set; }
        public long PeakBytes { get; private set; }
        public int UploadedBytes { get; set; }
        public int Count => _residents.Count;
        public string Pressure { get; private set; }
        public IEnumerable<PRTSectorResident> Residents => _residents.Values;
        public PRTSectorResidency(PRTProbeVolumeAsset asset) => _asset = asset;

        public void BeginFrame(int near, int background, long budget)
        {
            // Edit Mode can leave readbacks pending indefinitely; a late eviction waits so the budget cannot deadlock.
            for (int i = _evictions.Count - 1; i >= 0; i--)
            {
                var (evicted, request, frame) = _evictions[i];
                request.Update();
                if (!request.done && PRTRelightFrame.Index - frame < EvictionPatience) continue;
                if (!request.done) request.WaitForCompletion();
                _evictions.RemoveAt(i);
                CompleteEviction(evicted, request);
            }
            foreach (var resident in _completed)
            {
                Bytes -= resident.Bytes;
                _residents.Remove(resident.Index);
                resident.Dispose();
            }
            _completed.Clear();
            _protected.Clear();
            _protected.Add(near); _protected.Add(background);
            UploadedBytes = 0;
            Pressure = null;
            foreach (var resident in _residents.Values)
                if (resident.ReadbackFailed) Pressure = $"Sector {resident.Index} shadow preservation failed; resident retained.";
            long retiring = 0;
            foreach (var resident in _residents.Values) if (resident.Evicting) retiring += resident.Bytes;
            while (Bytes - retiring > budget)
            {
                PRTSectorResident oldest = null;
                foreach (var candidate in _residents.Values)
                    if (!candidate.Evicting && !candidate.ReadbackFailed && !_protected.Contains(candidate.Index) &&
                        (oldest == null || candidate.LastUsed < oldest.LastUsed)) oldest = candidate;
                if (oldest == null) break;
                Evict(oldest);
                retiring += oldest.Bytes;
            }
            if (Bytes > budget) Pressure = $"{Bytes}/{budget} bytes allocated, including protected sectors and pending eviction.";
        }

        public PRTSectorResident Request(int id, PRTRelightLightingSnapshot lights, long budget)
        {
            _protected.Add(id);
            if (_residents.TryGetValue(id, out var resident))
            {
                if (resident.Evicting) return null;
                if (!resident.Lighting.SetInput(lights))
                {
                    Evict(resident);
                    return null;
                }
                resident.LastUsed = PRTRelightFrame.Index;
                return resident;
            }
            long bytes = PRTSectorResident.Estimate(_asset.Sectors[id], lights);
            if (Bytes + bytes > budget)
            {
                PRTSectorResident oldest = null;
                foreach (var candidate in _residents.Values)
                    if (!candidate.Evicting && !candidate.ReadbackFailed && !_protected.Contains(candidate.Index) &&
                        (oldest == null || candidate.LastUsed < oldest.LastUsed)) oldest = candidate;
                if (oldest != null) Evict(oldest);
                Pressure = $"Sector {id} needs {bytes} bytes; {Bytes}/{budget} bytes allocated, including pending eviction.";
                return null;
            }
            _saved.TryGetValue(id, out var cache);
            resident = new PRTSectorResident(id, _asset.Sectors[id], lights, cache) { LastUsed = PRTRelightFrame.Index };
            _residents.Add(id, resident);
            Bytes += resident.Bytes;
            PeakBytes = Math.Max(PeakBytes, Bytes);
            return resident;
        }

        private void Evict(PRTSectorResident resident)
        {
            if (resident.ReadbackFailed)
            {
                Pressure = $"Sector {resident.Index} shadow preservation failed; resident retained.";
                return;
            }
            resident.Evicting = true;
            if (!resident.Ready) { _completed.Add(resident); return; }
            // An evicting sector is never relit again, so its cache holds the last frame's writes.
            _evictions.Add((resident, AsyncGPUReadback.Request(resident.Lighting.ShadowCacheBuffer), PRTRelightFrame.Index));
        }

        private void CompleteEviction(PRTSectorResident resident, AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                resident.Evicting = false;
                resident.ReadbackFailed = true;
                Pressure = $"Sector {resident.Index} shadow preservation failed; resident retained.";
                return;
            }
            _saved[resident.Index] = resident.Lighting.SaveCache(request);
            _completed.Add(resident);
        }

        public void Dispose()
        {
            foreach (var (_, request, _) in _evictions) request.WaitForCompletion();
            foreach (var resident in _residents.Values) resident.Dispose();
            _residents.Clear(); _saved.Clear(); _completed.Clear(); _evictions.Clear();
        }
    }
}
