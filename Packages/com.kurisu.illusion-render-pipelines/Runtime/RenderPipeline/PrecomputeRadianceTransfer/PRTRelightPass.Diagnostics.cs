using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTRelightPass
    {
        private sealed class ShadowReadbackData
        {
            public PRTProbeVolume volume;
            public GraphicsBuffer stats, cache;
            public int dataId, bytes, offset, sector;
            public bool readStats, readPreview;
#if UNITY_EDITOR
            public bool readDebug;
#endif
        }

        private void RecordShadowDiagnostics(RenderGraph graph, PRTSectorResident sector)
        {
            var _lighting = sector.Lighting;
            if (_lighting.StatsBuffer == null || !_volume)
                return;
            uint frame = PRTRelightFrame.Index;
            bool stats = _volume.ShouldRequestShadowCacheGlobalStatsReadback(frame);
#if UNITY_EDITOR
            bool debug = _volume.ShouldRequestShadowCacheDebugReadback(frame);
#else
            bool debug = false;
#endif
            if (!stats && !debug)
                return;
            int surfelCount = sector.Data.surfels.Length;
            bool preview = _lighting.PreviewCacheOffset != uint.MaxValue && surfelCount > 0;
            if (stats)
                _volume.BeginWorldShadowStats(_lighting.PreviewVisibilityEpoch, frame, _lighting.MaxShadowAge,
                    _lighting.PreviewLightName, _lighting.PreviewLightId);
#if UNITY_EDITOR
            if (debug && preview)
                _volume.BeginWorldShadowDebug(_lighting.PreviewVisibilityEpoch, frame, _lighting.MaxShadowAge, _lighting.PreviewLightName);
#endif
            if (!preview)
            {
                _volume.ClearShadowCacheGlobalStats();
                _volume.ShadowPreviewLightName = _lighting.PreviewLightName;
                _volume.ShadowPreviewLightId = _lighting.PreviewLightId;
#if UNITY_EDITOR
                _volume.ClearShadowCacheDebugSnapshot();
#endif
            }
            using var builder = graph.AddUnsafePass<ShadowReadbackData>("PRT read world visibility diagnostics", out var data);
            data.volume = _volume;
            data.dataId = _volume.RuntimeDataId;
            data.sector = sector.Index;
            data.stats = _lighting.StatsBuffer;
            data.cache = _lighting.ShadowCacheBuffer;
            data.readStats = stats;
            data.readPreview = stats && preview;
            data.bytes = surfelCount * PRTWorldShadowCacheEntry.Stride;
            data.offset = preview ? checked((int)_lighting.PreviewCacheOffset * PRTWorldShadowCacheEntry.Stride) : 0;
#if UNITY_EDITOR
            data.readDebug = debug && preview;
#endif
            builder.UseBuffer(graph.ImportBuffer(data.stats), AccessFlags.Read);
            if (preview)
                builder.UseBuffer(graph.ImportBuffer(data.cache), AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (ShadowReadbackData pass, UnsafeGraphContext context) =>
            {
                var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                if (pass.readStats)
                    command.RequestAsyncReadback(pass.stats, request =>
                    {
                        if (pass.volume && pass.volume.RuntimeDataId == pass.dataId)
                            pass.volume.UpdateShadowCacheStats(request);
                    });
                if (pass.readPreview)
                    command.RequestAsyncReadback(pass.cache, pass.bytes, pass.offset, request =>
                    {
                        if (pass.volume && pass.volume.RuntimeDataId == pass.dataId)
                            pass.volume.UpdateWorldShadowStats(request, pass.sector);
                    });
#if UNITY_EDITOR
                if (pass.readDebug)
                    command.RequestAsyncReadback(pass.cache, pass.bytes, pass.offset, request =>
                    {
                        if (pass.volume && pass.volume.RuntimeDataId == pass.dataId)
                            pass.volume.UpdateWorldShadowDebug(request, pass.sector);
                    });
#endif
            });
        }

#if UNITY_EDITOR
        private sealed class ProbeDebugData
        {
            public ComputeShader shader;
            public int kernel, index;
            public GraphicsBuffer source, destination;
        }
        private void RecordProbeDebug(RenderGraph graph)
        {
            if (_solver.Generation == 0 || _volume.debugMode is not (ProbeVolumeDebugMode.ProbeRadiance or ProbeVolumeDebugMode.ShadowCache))
                return;
            foreach (var pair in _volume.CreatedProbeDebugData())
            {
                using var builder = graph.AddUnsafePass<ProbeDebugData>("PRT publish debug probe coefficients", out var data);
                data.shader = _probeShader;
                data.kernel = _debugKernel;
                data.index = pair.index;
                data.source = _solver.Previous;
                data.destination = pair.data.CoefficientSH9;
                builder.UseBuffer(graph.ImportBuffer(data.source), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.destination), AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ProbeDebugData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtPreviousSH", pass.source);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_coefficientSH9", pass.destination);
                    command.SetComputeIntParam(pass.shader, "_prtDebugProbeIndex", pass.index);
                    command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
                });
            }
        }
#endif
    }
}
