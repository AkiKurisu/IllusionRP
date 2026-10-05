using System;
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
        public RTHandle Metadata { get; }
        public GraphicsBuffer Constants { get; }
        public Vector3Int Minimum { get; private set; }
        public Vector3Int Count { get; }
        public uint LastUsedFrame { get; private set; }
        private uint _generation = uint.MaxValue;
        private uint[] _sectorRevisions;
        private Vector3Int _publishedMinimum;
        private bool _initialized;
        private readonly PRTProbeGrid _grid;

        public PRTCameraPublication(Camera camera, PRTProbeGrid grid, Vector3Int count)
        {
            Camera = camera;
            _grid = grid;
            Count = count;
            Constants = PRTLayoutConstants.Allocate();
            Coefficients = Allocate("PRT camera SH", GraphicsFormat.R32G32B32A32_SFloat, count.y * 9);
            Metadata = Allocate("PRT camera metadata", GraphicsFormat.R32_UInt, count.y);
        }

        private RTHandle Allocate(string name, GraphicsFormat format, int depth)
        {
            return RTHandles.Alloc(Count.x, Count.z, slices: depth, dimension: TextureDimension.Tex3D,
                colorFormat: format, enableRandomWrite: true,
                filterMode: format == GraphicsFormat.R32G32B32A32_SFloat ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp, name: name);
        }

        public static Vector3Int WindowMinimum(PRTProbeGrid grid, Vector3Int count, Vector3 position)
        {
            Vector3 coordinate = (position - grid.origin) / grid.spacing;
            return new Vector3Int(
                Mathf.Clamp(Mathf.RoundToInt(coordinate.x) - count.x / 2, grid.min.x, grid.min.x + grid.count.x - count.x),
                Mathf.Clamp(Mathf.RoundToInt(coordinate.y) - count.y / 2, grid.min.y, grid.min.y + grid.count.y - count.y),
                Mathf.Clamp(Mathf.RoundToInt(coordinate.z) - count.z / 2, grid.min.z, grid.min.z + grid.count.z - count.z));
        }

        private sealed class PublishData
        {
            public PRTCameraPublication owner;
            public ComputeShader shader;
            public int kernel;
            public GraphicsBuffer source, metadata, ready, constants;
            public RenderTexture coefficientsTexture, metadataTexture;
            public Vector3Int count, minimum, offset;
            public uint[] sectorRevisions;
            public uint generation;
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
            PRTProbeVolume volume, ComputeShader shader, int kernel)
        {
            Vector3Int minimum = WindowMinimum(_grid, Count, Camera.transform.position);
            bool rebuild = !_initialized || minimum != _publishedMinimum;
            bool changed = rebuild || _generation != solver.Generation;
            Minimum = minimum;
            LastUsedFrame = PRTRelightFrame.Index;
            var constants = PRTLayoutConstants.Create(_grid, Minimum, Count, solver.Generation);
            constants.enabled = solver.Generation != 0 ? 1u : 0u;
            BufferHandle layout = graph.ImportBuffer(Constants);
            TextureHandle coefficientTexture = graph.ImportTexture(Coefficients);
            TextureHandle metadataTexture = graph.ImportTexture(Metadata);
            frameData.GetOrCreate<PRTShaderResources>().Bindings = new PRTShaderBindings
            {
                Coefficients = coefficientTexture, Metadata = metadataTexture,
                Layout = layout, LayoutBuffer = Constants
            };

            if (changed)
            {
                var revisions = (uint[])solver.SectorRevisions.Clone();
                if (rebuild) RecordRegion(Vector3Int.zero, Count);
                else for (int sector = 0; sector < revisions.Length; sector++)
                {
                    if (_sectorRevisions[sector] == revisions[sector]) continue;
                    var coordinate = volume.asset.Sectors[sector].coordinate;
                    int width = volume.asset.SectorWidth;
                    var sectorMin = _grid.min + new Vector3Int(coordinate.x * width, 0, coordinate.y * width);
                    var start = Vector3Int.Max(minimum, sectorMin);
                    var end = Vector3Int.Min(minimum + Count, Vector3Int.Min(_grid.min + _grid.count,
                        sectorMin + new Vector3Int(width, _grid.count.y, width)));
                    var size = end - start;
                    if (size.x > 0 && size.y > 0 && size.z > 0) RecordRegion(start - minimum, size);
                }

                void RecordRegion(Vector3Int offset, Vector3Int size)
                {
                using var builder = graph.AddUnsafePass<PublishData>("PRT publish changed volume slots", out var data);
                data.owner = this;
                data.shader = shader;
                data.kernel = kernel;
                data.source = solver.Previous;
                data.metadata = solver.PreviousMetadata;
                data.ready = solver.Ready;
                data.constants = Constants;
                data.coefficientsTexture = Coefficients.rt;
                data.metadataTexture = Metadata.rt;
                data.count = size;
                data.offset = offset;
                data.sectorRevisions = revisions;
                data.minimum = minimum;
                data.generation = solver.Generation;
                data.values = new[] { constants };
                builder.UseBuffer(graph.ImportBuffer(solver.Previous), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(solver.PreviousMetadata), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(solver.Ready), AccessFlags.Read);
                builder.UseBuffer(layout, AccessFlags.ReadWrite);
                builder.UseTexture(coefficientTexture, AccessFlags.ReadWrite);
                builder.UseTexture(metadataTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PublishData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    command.SetBufferData(pass.constants, pass.values);
                    PRTLayoutConstants.Bind(command, pass.shader, pass.constants);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtPreviousSH", pass.source);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_validityMasks", pass.metadata);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtReady", pass.ready);
                    command.SetComputeIntParams(pass.shader, "_prtPublishOffset", pass.offset.x, pass.offset.y, pass.offset.z);
                    command.SetComputeIntParams(pass.shader, "_prtPublishCount", pass.count.x, pass.count.y, pass.count.z);
                    command.SetComputeTextureParam(pass.shader, pass.kernel, "_coefficientVoxel3D", pass.coefficientsTexture);
                    command.SetComputeTextureParam(pass.shader, pass.kernel, "_validityVoxel3D", pass.metadataTexture);
                    command.DispatchCompute(pass.shader, pass.kernel, (pass.count.x + 7) / 8, (pass.count.y + 3) / 4, (pass.count.z + 7) / 8);
                    pass.owner._publishedMinimum = pass.minimum;
                    pass.owner._generation = pass.generation;
                    pass.owner._initialized = true;
                    pass.owner._sectorRevisions = pass.sectorRevisions;
                });
                }
            }

            using (var builder = graph.AddUnsafePass<BindData>("PRT camera publication", out var data))
            {
                data.constants = Constants;
                data.spacing = _grid.spacing;
                data.ready = solver.Generation != 0;
                data.owner = this;
                data.generation = solver.Generation;
                data.revisions = (uint[])solver.SectorRevisions.Clone();
                builder.UseBuffer(layout, AccessFlags.Read);
                builder.UseTexture(coefficientTexture, AccessFlags.Read);
                builder.UseTexture(metadataTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(coefficientTexture, Shader.PropertyToID("_coefficientVoxel3D"));
                builder.SetGlobalTextureAfterPass(metadataTexture, Shader.PropertyToID("_validityVoxel3D"));
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
            volume.ObservePublication(Camera, Minimum, Count, Coefficients.rt, Metadata.rt, solver);
        }

        public void Dispose()
        {
            Coefficients.Release();
            Metadata.Release();
            Constants.Dispose();
        }
    }
}
