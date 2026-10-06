using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTSectorResidency : IDisposable
    {
        private sealed class Eviction
        {
            public PRTSectorResidency owner;
            public PRTSectorResident resident;
        }
        private readonly PRTProbeVolumeAsset _asset;
        private readonly Dictionary<int, PRTSectorResident> _residents = new();
        private readonly Dictionary<int, Dictionary<uint, PRTWorldShadowCacheEntry[]>> _saved = new();
        private readonly List<PRTSectorResident> _completed = new();
        private readonly HashSet<int> _protected = new();
        private bool _disposed;
        public long Bytes { get; private set; }
        public long PeakBytes { get; private set; }
        public int UploadedBytes { get; set; }
        public int Count => _residents.Count;
        public string Pressure { get; private set; }
        public IEnumerable<PRTSectorResident> Residents => _residents.Values;
        public PRTSectorResidency(PRTProbeVolumeAsset asset) => _asset = asset;

        public void BeginFrame(RenderGraph graph, int near, int background, long budget)
        {
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
                Evict(graph, oldest);
                retiring += oldest.Bytes;
            }
            if (Bytes > budget) Pressure = $"{Bytes}/{budget} bytes allocated, including protected sectors and pending eviction.";
        }

        public PRTSectorResident Request(RenderGraph graph, int id, PRTRelightLightingSnapshot lights, long budget)
        {
            _protected.Add(id);
            if (_residents.TryGetValue(id, out var resident))
            {
                if (resident.Evicting) return null;
                if (!resident.Lighting.SetInput(lights))
                {
                    Evict(graph, resident);
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
                if (oldest != null) Evict(graph, oldest);
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

        private void Evict(RenderGraph graph, PRTSectorResident resident)
        {
            if (resident.ReadbackFailed)
            {
                Pressure = $"Sector {resident.Index} shadow preservation failed; resident retained.";
                return;
            }
            resident.Evicting = true;
            if (!resident.Ready) { _completed.Add(resident); return; }
            using var builder = graph.AddUnsafePass<Eviction>($"PRT preserve sector {resident.Index} shadows", out var pass);
            pass.owner = this; pass.resident = resident;
            builder.UseBuffer(graph.ImportBuffer(resident.Lighting.ShadowCacheBuffer), AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (Eviction data, UnsafeGraphContext context) =>
            {
                // Pass data is pooled by the render graph; the callback must not read it after this frame.
                var owner = data.owner;
                var evicted = data.resident;
                CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).RequestAsyncReadback(evicted.Lighting.ShadowCacheBuffer,
                    request => owner.CompleteEviction(evicted, request));
            });
        }

        private void CompleteEviction(PRTSectorResident resident, AsyncGPUReadbackRequest request)
        {
            if (_disposed) { resident.Dispose(); return; }
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
            _disposed = true;
            foreach (var resident in _residents.Values)
                if (!resident.Evicting || _completed.Contains(resident)) resident.Dispose();
            _residents.Clear(); _saved.Clear(); _completed.Clear();
        }
    }
}
