using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTRelightWorldLighting : IDisposable
    {
        private sealed class UploadData
        {
            internal GraphicsBuffer Lights, Faces, Stats;
            internal PRTWorldLightGpu[] LightValues;
            internal PRTShadowFaceGpu[] FaceValues;
        }
        private static readonly uint[] EmptyStats = new uint[PRTProbeVolume.ShadowCacheStats.CounterCount];
        private readonly List<PRTShadowFaceGpu> _faces = new();
        private readonly GraphicsBuffer _lights, _shadowFaces;
        private readonly int _surfelCount;
        private readonly uint[] _cacheLightIds;
        internal bool CollectStats { get; set; }
        internal bool FragmentShadowBias { get; set; }
        internal uint PreviewCacheOffset { get; private set; }
        internal uint PreviewVisibilityEpoch { get; private set; }
        internal string PreviewLightName { get; private set; }
        internal int PreviewLightId { get; private set; }
        internal GraphicsBuffer StatsBuffer { get; }
        internal GraphicsBuffer ShadowCacheBuffer { get; }
        internal PRTRelightLightingSnapshot Active { get; private set; }
        internal long Bytes { get; }

        internal static PRTRelightLightingSnapshot Select(PRTRelightLightingSnapshot world, Bounds bounds)
        {
            var lights = new List<PRTWorldLightSnapshot>();
            foreach (var light in world.Lights)
                if (light.Gpu.PositionType.w == 0 || bounds.SqrDistance(light.Gpu.PositionType) <=
                    light.Gpu.DirectionRange.w * light.Gpu.DirectionRange.w) lights.Add(light);
            return new PRTRelightLightingSnapshot(world.SceneTime, lights.ToArray());
        }

        private static int ShadowCount(PRTRelightLightingSnapshot lights)
        {
            int count = 0;
            foreach (var light in lights.Lights) if (light.CastsShadows) count++;
            return count;
        }

        internal static long Estimate(PRTRelightLightingSnapshot lights, int surfels) =>
            Mathf.Max(1, lights.Lights.Length) * (long)PRTWorldLightGpu.Stride
            + Mathf.Max(1, lights.Lights.Length * 6) * (long)PRTShadowFaceGpu.Stride
            + Mathf.Max(1, ShadowCount(lights) * surfels) * (long)PRTWorldShadowCacheEntry.Stride
            + 32;

        internal PRTRelightWorldLighting(PRTRelightLightingSnapshot snapshot, int surfels)
        {
            Active = snapshot;
            _surfelCount = surfels;
            _lights = Allocate(snapshot.Lights.Length, PRTWorldLightGpu.Stride, "lights");
            _shadowFaces = Allocate(snapshot.Lights.Length * 6, PRTShadowFaceGpu.Stride, "existing shadow faces");
            ShadowCacheBuffer = Allocate(ShadowCount(snapshot) * surfels, PRTWorldShadowCacheEntry.Stride, "shadow cache");
            StatsBuffer = Allocate(PRTProbeVolume.ShadowCacheStats.CounterCount, 4, "stats");
            var ids = new List<uint>();
            foreach (var light in snapshot.Lights) if (light.CastsShadows) ids.Add(light.Gpu.LightId);
            _cacheLightIds = ids.ToArray();
            Bytes = Estimate(snapshot, surfels);
        }

        private static GraphicsBuffer Allocate(int count, int stride, string name) =>
            new(GraphicsBuffer.Target.Structured, Mathf.Max(1, count), stride) { name = "PRT sector " + name };

        internal bool SetInput(PRTRelightLightingSnapshot snapshot)
        {
            if (snapshot.Lights.Length != Active.Lights.Length) return false;
            for (int i = 0; i < snapshot.Lights.Length; i++)
                if (snapshot.Lights[i].Gpu.LightId != Active.Lights[i].Gpu.LightId ||
                    snapshot.Lights[i].CastsShadows != Active.Lights[i].CastsShadows) return false;
            Active = snapshot;
            return true;
        }

        internal PRTWorldShadowCacheEntry[] RestoreCache(Dictionary<uint, PRTWorldShadowCacheEntry[]> saved)
        {
            var result = new PRTWorldShadowCacheEntry[ShadowCacheBuffer.count];
            for (int i = 0; i < _cacheLightIds.Length; i++)
                if (saved != null && saved.TryGetValue(_cacheLightIds[i], out var entries))
                    Array.Copy(entries, 0, result, i * _surfelCount, _surfelCount);
            return result;
        }

        internal Dictionary<uint, PRTWorldShadowCacheEntry[]> SaveCache(AsyncGPUReadbackRequest request)
        {
            var result = new Dictionary<uint, PRTWorldShadowCacheEntry[]>();
            var values = request.GetData<PRTWorldShadowCacheEntry>();
            for (int i = 0; i < _cacheLightIds.Length; i++)
            {
                var entries = new PRTWorldShadowCacheEntry[_surfelCount];
                for (int j = 0; j < entries.Length; j++) entries[j] = values[i * _surfelCount + j];
                result.Add(_cacheLightIds[i], entries);
            }
            return result;
        }

        internal PRTWorldLightingResources Record(RenderGraph graph, ContextContainer frameData)
        {
            _faces.Clear();
            PreviewCacheOffset = uint.MaxValue;
            PreviewLightName = "No shadowed sector light";
            PreviewLightId = 0;
            var lights = new PRTWorldLightGpu[Mathf.Max(1, Active.Lights.Length)];
            int shadowIndex = 0;
            for (int i = 0; i < Active.Lights.Length; i++)
            {
                var light = Active.Lights[i];
                light.Gpu.FaceOffset = (uint)_faces.Count;
                light.Gpu.CacheOffset = uint.MaxValue;
                if (light.CastsShadows)
                {
                    light.Gpu.CacheOffset = checked((uint)(shadowIndex++ * _surfelCount));
                    PRTExistingShadowMaps.Append(light, Active, frameData, graph, FragmentShadowBias, _faces);
                    light.Gpu.FaceCount = (uint)_faces.Count - light.Gpu.FaceOffset;
                    if (PreviewCacheOffset == uint.MaxValue)
                    {
                        PreviewCacheOffset = light.Gpu.CacheOffset;
                        PreviewVisibilityEpoch = light.Gpu.VisibilityEpoch;
                        PreviewLightId = unchecked((int)light.Gpu.LightId);
                        PreviewLightName = light.Source.name;
                    }
                }
                lights[i] = light.Gpu;
            }
            var resources = frameData.Get<UniversalResourceData>();
            var result = new PRTWorldLightingResources
            {
                LightBuffer = _lights, ShadowCacheBuffer = ShadowCacheBuffer,
                ShadowFaceBuffer = _shadowFaces, StatsBuffer = StatsBuffer,
                LightHandle = graph.ImportBuffer(_lights), ShadowCacheHandle = graph.ImportBuffer(ShadowCacheBuffer),
                ShadowFaceHandle = graph.ImportBuffer(_shadowFaces), StatsHandle = graph.ImportBuffer(StatsBuffer),
                MainShadowTexture = resources.mainShadowsTexture.IsValid() ? resources.mainShadowsTexture : graph.defaultResources.defaultShadowTexture,
                AdditionalShadowTexture = resources.additionalShadowsTexture.IsValid() ? resources.additionalShadowsTexture : graph.defaultResources.defaultShadowTexture,
                LightCount = (uint)Active.Lights.Length, SurfelCount = (uint)_surfelCount,
                PreviewCacheOffset = PreviewCacheOffset, CollectStats = CollectStats
            };
            using var builder = graph.AddUnsafePass<UploadData>("PRT sector world lighting inputs", out var upload);
            builder.UseBuffer(result.LightHandle, AccessFlags.Write);
            builder.UseBuffer(result.ShadowFaceHandle, AccessFlags.Write);
            builder.UseBuffer(result.StatsHandle, AccessFlags.Write);
            builder.AllowPassCulling(false);
            upload.Lights = _lights; upload.Faces = _shadowFaces; upload.Stats = StatsBuffer;
            upload.LightValues = lights;
            upload.FaceValues = _faces.Count > 0 ? _faces.ToArray() : new PRTShadowFaceGpu[1];
            builder.SetRenderFunc(static (UploadData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetBufferData(data.Lights, data.LightValues);
                cmd.SetBufferData(data.Faces, data.FaceValues);
                cmd.SetBufferData(data.Stats, EmptyStats);
            });
            return result;
        }

        internal static void Bind(CommandBuffer cmd, ComputeShader shader, int kernel,
            in PRTWorldLightingResources resources)
        {
            cmd.SetComputeBufferParam(shader, kernel, "_PRTWorldLights", resources.LightBuffer);
            cmd.SetComputeBufferParam(shader, kernel, "_PRTWorldShadowFaces", resources.ShadowFaceBuffer);
            cmd.SetComputeBufferParam(shader, kernel, "_PRTWorldShadowCache", resources.ShadowCacheBuffer);
            cmd.SetComputeBufferParam(shader, kernel, "_PRTWorldLightingStats", resources.StatsBuffer);
            cmd.SetComputeTextureParam(shader, kernel, "_PRTWorldMainShadows", resources.MainShadowTexture);
            cmd.SetComputeTextureParam(shader, kernel, "_PRTWorldAdditionalShadows", resources.AdditionalShadowTexture);
            cmd.SetComputeIntParam(shader, "_PRTWorldLightCount", (int)resources.LightCount);
            cmd.SetComputeIntParam(shader, "_PRTWorldSurfelCount", (int)resources.SurfelCount);
            cmd.SetComputeIntParam(shader, "_PRTWorldStatsEnabled", resources.CollectStats ? 1 : 0);
        }

        public void Dispose()
        {
            _lights.Dispose(); _shadowFaces.Dispose(); ShadowCacheBuffer.Dispose(); StatsBuffer.Dispose();
        }
    }
}
