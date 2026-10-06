using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    // Coarse levels of committed probe lighting. Node i of a level is aligned with node 2i of the finer level and
    // stores tent-filtered, validity-premultiplied SH (rgb = SH * intensity * validity, a = validity).
    internal sealed class PRTProbePyramid : IDisposable
    {
        public const int MaxCoarseLevels = 3;
        private readonly Vector3Int[] _counts;
        private readonly GraphicsBuffer[] _levels;
        public int CoarseLevelCount => _counts.Length - 1;
        public long Bytes { get; }

        private sealed class PassData
        {
            public ComputeShader shader;
            public int kernel;
            public GraphicsBuffer sh, metadata, ready, source, target;
            public Vector3Int sourceCount, targetCount, offset, size;
        }

        public PRTProbePyramid(PRTProbeGrid grid)
        {
            var counts = new List<Vector3Int> { grid.count };
            while (counts.Count <= MaxCoarseLevels)
            {
                Vector3Int fine = counts[^1];
                var coarse = new Vector3Int(CoarseCount(fine.x), CoarseCount(fine.y), CoarseCount(fine.z));
                if (coarse == fine) break;
                counts.Add(coarse);
            }
            _counts = counts.ToArray();
            _levels = new GraphicsBuffer[_counts.Length];
            for (int level = 1; level < _counts.Length; level++)
            {
                Vector3Int count = _counts[level];
                int nodes = count.x * count.y * count.z;
                _levels[level] = PRTRelightSolver.Upload(new Vector4[nodes * 9], 16, $"PRT probe pyramid level {level}");
                Bytes += (long)nodes * 9 * 16;
            }
        }

        private static int CoarseCount(int fine) => fine == 1 ? 1 : fine / 2 + 1;

        // Maps a changed finer range [min, max) to the coarse nodes whose 2i-1..2i+1 footprint touches it.
        public static void AffectedNodes(Vector3Int coarseCount, ref Vector3Int min, ref Vector3Int max)
        {
            var low = Vector3Int.Max(Vector3Int.zero, new Vector3Int(min.x / 2, min.y / 2, min.z / 2));
            max = Vector3Int.Min(coarseCount, new Vector3Int(max.x / 2 + 1, max.y / 2 + 1, max.z / 2 + 1));
            min = low;
        }

        public Vector3Int Count(int level) => _counts[level];
        public GraphicsBuffer Buffer(int level) => _levels[level];

        public void Record(RenderGraph graph, ComputeShader shader, int probeKernel, int levelKernel, PRTRelightSolver solver,
            PRTProbeVolumeAsset asset, IReadOnlyList<PRTSectorResident> sectors)
        {
            int width = asset.SectorWidth;
            foreach (var sector in sectors)
            {
                Vector2Int coordinate = asset.Sectors[sector.Index].coordinate;
                var min = new Vector3Int(coordinate.x * width, 0, coordinate.y * width);
                var max = Vector3Int.Min(min + new Vector3Int(width, _counts[0].y, width), _counts[0]);
                for (int level = 1; level < _counts.Length; level++)
                {
                    AffectedNodes(_counts[level], ref min, ref max);
                    RecordLevel(graph, shader, level == 1 ? probeKernel : levelKernel, solver, level, min, max - min);
                }
            }
        }

        private void RecordLevel(RenderGraph graph, ComputeShader shader, int kernel, PRTRelightSolver solver,
            int level, Vector3Int offset, Vector3Int size)
        {
            using var builder = graph.AddUnsafePass<PassData>($"PRT probe pyramid level {level}", out var pass);
            pass.shader = shader; pass.kernel = kernel;
            pass.sh = solver.Previous; pass.metadata = solver.PreviousMetadata; pass.ready = solver.Ready;
            pass.source = level == 1 ? solver.Previous : _levels[level - 1];
            pass.target = _levels[level];
            pass.sourceCount = _counts[level - 1]; pass.targetCount = _counts[level];
            pass.offset = offset; pass.size = size;
            foreach (var input in new[] { pass.sh, pass.metadata, pass.ready, pass.source })
                builder.UseBuffer(graph.ImportBuffer(input), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(pass.target), AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtPreviousSH", data.sh);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_validityMasks", data.metadata);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtReady", data.ready);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtPyramidSource", data.source);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtPyramidTarget", data.target);
                cmd.SetComputeIntParams(data.shader, "_prtPyramidSourceCount", data.sourceCount.x, data.sourceCount.y, data.sourceCount.z);
                cmd.SetComputeIntParams(data.shader, "_prtPyramidTargetCount", data.targetCount.x, data.targetCount.y, data.targetCount.z);
                cmd.SetComputeIntParams(data.shader, "_prtPyramidOffset", data.offset.x, data.offset.y, data.offset.z);
                cmd.SetComputeIntParams(data.shader, "_prtPyramidSize", data.size.x, data.size.y, data.size.z);
                cmd.DispatchCompute(data.shader, data.kernel, (data.size.x + 3) / 4, (data.size.y + 3) / 4, (data.size.z + 3) / 4);
            });
        }

        public void Dispose()
        {
            foreach (var buffer in _levels) buffer?.Dispose();
        }
    }
}
