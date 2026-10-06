using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTCameraPublication : IDisposable
    {
        public Camera Camera { get; }
        public RTHandle Coefficients { get; }
        public GraphicsBuffer Constants { get; }
        public Vector3Int Slots { get; }
        public int RequestedCascades { get; }
        public int CascadeCount => _levelCounts.Length;
        public Vector3Int[] Minimum { get; }
        public Vector3Int[] Count { get; }
        public uint LastUsedFrame { get; private set; }
        private readonly Vector3Int[] _levelCounts;
        private readonly Vector3Int[] _publishedMinimum;
        private uint _generation = uint.MaxValue;
        private uint[] _sectorRevisions;
        private bool _initialized;
        private readonly PRTProbeGrid _grid;

        public PRTCameraPublication(Camera camera, PRTProbeGrid grid, PRTProbePyramid pyramid, Vector3Int slots, int cascades)
        {
            Camera = camera;
            _grid = grid;
            Slots = slots;
            RequestedCascades = cascades;
            var levels = new List<Vector3Int> { grid.count };
            while (levels.Count < cascades && levels.Count <= pyramid.CoarseLevelCount &&
                   Vector3Int.Min(slots, levels[^1]) != levels[^1])
                levels.Add(pyramid.Count(levels.Count));
            _levelCounts = levels.ToArray();
            Count = new Vector3Int[_levelCounts.Length];
            Minimum = new Vector3Int[_levelCounts.Length];
            _publishedMinimum = new Vector3Int[_levelCounts.Length];
            for (int i = 0; i < Count.Length; i++) Count[i] = Vector3Int.Min(slots, _levelCounts[i]);
            Constants = PRTLayoutConstants.Allocate();
            Coefficients = RTHandles.Alloc(slots.x, slots.z, slices: slots.y * 9 * CascadeCount, dimension: TextureDimension.Tex3D,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, name: "PRT camera SH");
        }

        private static Vector3Int WindowMinimum(Vector3Int levelCount, Vector3Int count, Vector3 coordinate) => new(
            Mathf.Clamp(Mathf.RoundToInt(coordinate.x) - count.x / 2, 0, levelCount.x - count.x),
            Mathf.Clamp(Mathf.RoundToInt(coordinate.y) - count.y / 2, 0, levelCount.y - count.y),
            Mathf.Clamp(Mathf.RoundToInt(coordinate.z) - count.z / 2, 0, levelCount.z - count.z));

        private sealed class PublishData
        {
            public PRTCameraPublication owner;
            public ComputeShader shader;
            public int kernel;
            public GraphicsBuffer source, metadata, ready, constants;
            public RenderTexture coefficientsTexture;
            public Vector3Int count, start, levelCount;
            public int level;
            public Vector3Int[] minimum;
            public uint[] sectorRevisions;
            public uint generation;
        }

        private sealed class LayoutData
        {
            public GraphicsBuffer constants;
            public PRTLayoutConstants[] values;
        }

        private sealed class BindData
        {
            public GraphicsBuffer constants;
            public float spacing;
            public bool ready;
            public PRTCameraPublication owner;
            public uint generation;
            public uint[] revisions;
        }

        public void Record(RenderGraph graph, ContextContainer frameData, PRTRelightSolver solver,
            PRTProbeVolume volume, ComputeShader shader, int probeKernel, int levelKernel)
        {
            Vector3 cameraPosition = Camera.transform.position;
            Vector3 local = (cameraPosition - _grid.origin) / _grid.spacing - (Vector3)_grid.min;
            for (int level = 0; level < CascadeCount; level++)
                Minimum[level] = WindowMinimum(_levelCounts[level], Count[level], local / (1 << level));
            LastUsedFrame = PRTRelightFrame.Index;
            var constants = PRTLayoutConstants.Create(_grid, Slots, solver.Generation, cameraPosition, Minimum, Count, _levelCounts);
            constants.enabled = solver.Generation != 0 ? 1u : 0u;
            BufferHandle layout = graph.ImportBuffer(Constants);
            TextureHandle coefficientTexture = graph.ImportTexture(Coefficients);
            frameData.GetOrCreate<PRTShaderResources>().Bindings = new PRTShaderBindings
            {
                Coefficients = coefficientTexture, Layout = layout, LayoutBuffer = Constants
            };
            using (var builder = graph.AddUnsafePass<LayoutData>("PRT camera cascade layout", out var data))
            {
                data.constants = Constants;
                data.values = new[] { constants };
                builder.UseBuffer(layout, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (LayoutData pass, UnsafeGraphContext context) =>
                    CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).SetBufferData(pass.constants, pass.values));
            }

            var revisions = (uint[])solver.SectorRevisions.Clone();
            var minimum = (Vector3Int[])Minimum.Clone();
            bool generationChanged = _generation != solver.Generation;
            for (int level = 0; level < CascadeCount; level++)
            {
                Vector3Int count = Count[level];
                Vector3Int step = Minimum[level] - _publishedMinimum[level];
                bool rebuild = !_initialized || step.y != 0 || Mathf.Abs(step.x) >= count.x || Mathf.Abs(step.z) >= count.z;
                if (rebuild)
                {
                    RecordRegion(level, Minimum[level], count);
                    continue;
                }
                if (step.x != 0)
                    RecordRegion(level, new Vector3Int(step.x > 0 ? _publishedMinimum[level].x + count.x : Minimum[level].x,
                        Minimum[level].y, Minimum[level].z), new Vector3Int(Mathf.Abs(step.x), count.y, count.z));
                if (step.z != 0)
                    RecordRegion(level, new Vector3Int(Minimum[level].x, Minimum[level].y,
                        step.z > 0 ? _publishedMinimum[level].z + count.z : Minimum[level].z), new Vector3Int(count.x, count.y, Mathf.Abs(step.z)));
                if (!generationChanged) continue;
                int width = volume.asset.SectorWidth;
                for (int sector = 0; sector < revisions.Length; sector++)
                {
                    if (_sectorRevisions[sector] == revisions[sector]) continue;
                    var coordinate = volume.asset.Sectors[sector].coordinate;
                    var low = new Vector3Int(coordinate.x * width, 0, coordinate.y * width);
                    var high = Vector3Int.Min(low + new Vector3Int(width, _grid.count.y, width), _grid.count);
                    for (int coarse = 1; coarse <= level; coarse++)
                        PRTProbePyramid.AffectedNodes(_levelCounts[coarse], ref low, ref high);
                    var start = Vector3Int.Max(Minimum[level], low);
                    var size = Vector3Int.Min(Minimum[level] + count, high) - start;
                    if (size.x > 0 && size.y > 0 && size.z > 0) RecordRegion(level, start, size);
                }
            }

            void RecordRegion(int level, Vector3Int start, Vector3Int size)
            {
                using var builder = graph.AddUnsafePass<PublishData>($"PRT publish cascade {level} slots", out var data);
                data.owner = this;
                data.shader = shader;
                data.kernel = level == 0 ? probeKernel : levelKernel;
                data.source = level == 0 ? solver.Previous : solver.Pyramid.Buffer(level);
                data.metadata = solver.PreviousMetadata;
                data.ready = solver.Ready;
                data.constants = Constants;
                data.coefficientsTexture = Coefficients.rt;
                data.count = size;
                data.start = start;
                data.level = level;
                data.levelCount = _levelCounts[level];
                data.sectorRevisions = revisions;
                data.minimum = minimum;
                data.generation = solver.Generation;
                builder.UseBuffer(graph.ImportBuffer(data.source), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(solver.PreviousMetadata), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(solver.Ready), AccessFlags.Read);
                builder.UseBuffer(layout, AccessFlags.Read);
                builder.UseTexture(coefficientTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PublishData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    PRTLayoutConstants.Bind(command, pass.shader, pass.constants);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtPreviousSH", pass.source);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtPyramidSource", pass.source);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_validityMasks", pass.metadata);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtReady", pass.ready);
                    command.SetComputeIntParams(pass.shader, "_prtPublishOffset", pass.start.x, pass.start.y, pass.start.z);
                    command.SetComputeIntParams(pass.shader, "_prtPublishCount", pass.count.x, pass.count.y, pass.count.z);
                    command.SetComputeIntParams(pass.shader, "_prtPyramidSourceCount", pass.levelCount.x, pass.levelCount.y, pass.levelCount.z);
                    command.SetComputeIntParam(pass.shader, "_prtPublishCascade", pass.level);
                    command.SetComputeTextureParam(pass.shader, pass.kernel, "_coefficientVoxel3D", pass.coefficientsTexture);
                    command.DispatchCompute(pass.shader, pass.kernel, (pass.count.x + 7) / 8, (pass.count.y + 3) / 4, (pass.count.z + 7) / 8);
                    var owner = pass.owner;
                    owner._publishedMinimum[pass.level] = pass.minimum[pass.level];
                    owner._generation = pass.generation;
                    owner._sectorRevisions = pass.sectorRevisions;
                    if (pass.level == owner.CascadeCount - 1) owner._initialized = true;
                });
            }

            using (var builder = graph.AddUnsafePass<BindData>("PRT camera publication", out var data))
            {
                data.constants = Constants;
                data.spacing = _grid.spacing;
                data.ready = solver.Generation != 0;
                data.owner = this;
                data.generation = solver.Generation;
                data.revisions = revisions;
                builder.UseBuffer(layout, AccessFlags.Read);
                builder.UseTexture(coefficientTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(coefficientTexture, Shader.PropertyToID("_coefficientVoxel3D"));
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BindData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    command.SetGlobalConstantBuffer(pass.constants, PRTLayoutConstants.ShaderId, 0, PRTLayoutConstants.Stride);
                    command.SetGlobalFloat("_coefficientVoxelGridSize", pass.ready ? pass.spacing : 0);
                    pass.owner._generation = pass.generation;
                    pass.owner._sectorRevisions = pass.revisions;
                });
            }
            volume.ObservePublication(Camera, Minimum, Count, Coefficients.rt, solver);
        }

        public void Dispose()
        {
            Coefficients.Release();
            Constants.Dispose();
        }
    }
}
