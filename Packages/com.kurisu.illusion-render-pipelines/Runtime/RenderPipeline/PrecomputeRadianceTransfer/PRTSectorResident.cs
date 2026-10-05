using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTSectorResident : IDisposable
    {
        private sealed class Upload
        {
            public GraphicsBuffer buffer;
            public Array values;
            public int offset, count;
            public PRTSectorResident owner;
            public bool last;
        }
        private readonly List<(GraphicsBuffer buffer, Array values)> _uploads = new();
        private int _uploadIndex, _uploadOffset;
        public int Index { get; }
        public PRTSectorData Data { get; }
        public GraphicsBuffer Surfels, Bricks, Factors, Probes, ProbeIds, Sky, Radiance, Next, Residuals;
        public PRTRelightWorldLighting Lighting { get; }
        public bool Ready { get; private set; }
        public bool Evicting { get; internal set; }
        public bool ReadbackFailed { get; internal set; }
        public bool Disposed { get; private set; }
        public uint LastUsed { get; set; }
        public long Bytes { get; private set; }

        public PRTSectorResident(int index, PRTSectorData data, PRTRelightLightingSnapshot lights,
            Dictionary<uint, PRTWorldShadowCacheEntry[]> saved)
        {
            Index = index; Data = data;
            Surfels = Allocate(data.surfels, Surfel.Stride, "surfels");
            Bricks = Allocate(data.bricks, SurfelIndices.Stride, "bricks");
            Factors = Allocate(data.factors, BrickFactor.Stride, "factors");
            Probes = Allocate(data.probes, PRTProbeData.Stride, "probe ranges");
            ProbeIds = Allocate(data.probeIds, 4, "global probe IDs");
            Sky = Allocate(data.skySamples, PRTSkySample.Stride, "sky");
            Radiance = Allocate(new Vector4[data.bricks.Length], 16, "brick radiance");
            Next = Allocate(new Vector4[data.probes.Length * 9], 16, "probe scratch");
            Residuals = Allocate(new Vector4[data.probes.Length], 16, "residuals");
            Lighting = new PRTRelightWorldLighting(lights, data.surfels.Length);
            Bytes += Lighting.Bytes;
            _uploads.Add((Lighting.ShadowCacheBuffer, Lighting.RestoreCache(saved)));
        }

        private GraphicsBuffer Allocate<T>(T[] values, int stride, string label) where T : struct
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, values.Length), stride)
                { name = $"PRT sector {Index} {label}" };
            Bytes += (long)buffer.count * stride;
            _uploads.Add((buffer, values.Length == 0 ? new T[1] : values));
            return buffer;
        }

        public static long Estimate(PRTSectorData data, PRTRelightLightingSnapshot lights) =>
            (long)Mathf.Max(1, data.surfels.Length) * Surfel.Stride + Mathf.Max(1, data.bricks.Length) * 24L
            + Mathf.Max(1, data.factors.Length) * (long)BrickFactor.Stride
            + Mathf.Max(1, data.probes.Length) * (PRTProbeData.Stride + 4L + 9L * 16 + 16)
            + Mathf.Max(1, data.skySamples.Length) * (long)PRTSkySample.Stride
            + PRTRelightWorldLighting.Estimate(lights, data.surfels.Length);

        public int RecordUpload(RenderGraph graph, int budget)
        {
            int used = 0;
            while (_uploadIndex < _uploads.Count)
            {
                var item = _uploads[_uploadIndex];
                int count = Mathf.Min(item.values.Length - _uploadOffset, (budget - used) / item.buffer.stride);
                if (count == 0) break;
                using var builder = graph.AddUnsafePass<Upload>($"PRT upload sector {Index}", out var pass);
                pass.buffer = item.buffer; pass.values = item.values; pass.offset = _uploadOffset; pass.count = count;
                used += count * item.buffer.stride;
                _uploadOffset += count;
                if (_uploadOffset == item.values.Length) { _uploadIndex++; _uploadOffset = 0; }
                pass.last = _uploadIndex == _uploads.Count;
                pass.owner = this;
                builder.UseBuffer(graph.ImportBuffer(item.buffer), AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Upload data, UnsafeGraphContext context) =>
                {
                    CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).SetBufferData(data.buffer, data.values,
                        data.offset, data.offset, data.count);
                    if (data.last) { data.owner.Ready = true; data.owner._uploads.Clear(); }
                });
            }
            return used;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Surfels.Dispose(); Bricks.Dispose(); Factors.Dispose(); Probes.Dispose(); ProbeIds.Dispose(); Sky.Dispose();
            Radiance.Dispose(); Next.Dispose(); Residuals.Dispose(); Lighting.Dispose();
            _uploads.Clear();
        }
    }
}
