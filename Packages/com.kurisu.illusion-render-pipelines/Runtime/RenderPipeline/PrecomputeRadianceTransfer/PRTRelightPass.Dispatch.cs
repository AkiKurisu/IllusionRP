using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTRelightPass
    {
        private int _metadataHash = int.MinValue;
        private sealed class MetadataData
        {
            public GraphicsBuffer buffer;
            public uint[] values;
        }
        private sealed class BrickData
        {
            public ComputeShader shader;
            public int kernel, brickCount;
            public GraphicsBuffer layout, previous, metadata, ready, surfels, bricks, radiance;
            public PRTWorldLightingResources lighting;
        }

        private void RecordBricks(RenderGraph graph, PRTSectorResident sector, in PRTWorldLightingResources lighting)
        {
            if (sector.Data.bricks.Length == 0)
                return;
            using (var builder = graph.AddUnsafePass<BrickData>($"PRT relight sector {sector.Index} bricks", out var data))
            {
                data.shader = _brickShader;
                data.kernel = _brickKernel;
                data.brickCount = sector.Data.bricks.Length;
                data.ready = _solver.Ready;
                data.layout = _solver.Layout;
                data.previous = _solver.Previous;
                data.metadata = _solver.Metadata;
                data.surfels = sector.Surfels;
                data.bricks = sector.Bricks;
                data.radiance = sector.Radiance;
                data.lighting = lighting;
                builder.UseBuffer(graph.ImportBuffer(data.layout), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.previous), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.ready), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.metadata), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.surfels), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.bricks), AccessFlags.Read);
                builder.UseBuffer(graph.ImportBuffer(data.radiance), AccessFlags.Write);
                builder.UseBuffer(lighting.LightHandle, AccessFlags.Read);
                builder.UseBuffer(lighting.ShadowFaceHandle, AccessFlags.Read);
                builder.UseBuffer(lighting.ShadowCacheHandle, AccessFlags.ReadWrite);
                builder.UseBuffer(lighting.StatsHandle, AccessFlags.ReadWrite);
                builder.UseTexture(lighting.MainShadowTexture, AccessFlags.Read);
                builder.UseTexture(lighting.AdditionalShadowTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BrickData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    PRTLayoutConstants.Bind(command, pass.shader, pass.layout);
                    PRTRelightWorldLighting.Bind(command, pass.shader, pass.kernel, pass.lighting);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtPreviousSH", pass.previous);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_validityMasks", pass.metadata);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_surfels", pass.surfels);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_brickInfo", pass.bricks);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_brickRadiance", pass.radiance);
                    command.SetComputeIntParam(pass.shader, "_brickCount", pass.brickCount);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtReady", pass.ready);
                    command.DispatchCompute(pass.shader, pass.kernel, (pass.brickCount + 63) / 64, 1, 1);
                });
            }
        }

        private sealed class ProbeData
        {
            public ComputeShader shader;
            public int kernel, count;
            public GraphicsBuffer layout, probes, ids, factors, sky, radiance, previous, next, metadata, residuals;
            public Texture environment;
            public float environmentIntensity;
        }

        private void RecordProbes(RenderGraph graph, TextureHandle environment, PRTSectorResident sector)
        {
            using var builder = graph.AddUnsafePass<ProbeData>($"PRT integrate sector {sector.Index} probes", out var data);
            data.shader = _probeShader;
            data.kernel = _probeKernel;
            data.count = sector.Data.probes.Length;
            data.layout = _solver.Layout;
            data.probes = sector.Probes;
            data.factors = sector.Factors;
            data.sky = sector.Sky;
            data.radiance = sector.Radiance;
            data.previous = _solver.Previous;
            data.next = sector.Next;
            data.metadata = _solver.Metadata;
            data.ids = sector.ProbeIds;
            data.residuals = sector.Residuals;
            data.environment = _environment.Cube.rt ? _environment.Cube.rt : _environment.Cube.externalTexture;
            data.environmentIntensity = _environment.Intensity;
            builder.UseBuffer(graph.ImportBuffer(data.layout), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.probes), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.factors), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.sky), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.radiance), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.previous), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.metadata), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.next), AccessFlags.Write);
            builder.UseBuffer(graph.ImportBuffer(data.ids), AccessFlags.Read);
            builder.UseBuffer(graph.ImportBuffer(data.residuals), AccessFlags.Write);
            builder.UseTexture(environment, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (ProbeData pass, UnsafeGraphContext context) =>
            {
                var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                PRTLayoutConstants.Bind(command, pass.shader, pass.layout);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtProbeData", pass.probes);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_factors", pass.factors);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtSkySamples", pass.sky);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_brickRadiance", pass.radiance);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtPreviousSH", pass.previous);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtNextSH", pass.next);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_validityMasks", pass.metadata);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtProbeIds", pass.ids);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_prtResiduals", pass.residuals);
                command.SetComputeTextureParam(pass.shader, pass.kernel, "_prtEnvironment", pass.environment);
                command.SetComputeFloatParam(pass.shader, "_prtEnvironmentIntensity", pass.environmentIntensity);
                command.SetComputeFloatParam(pass.shader, "_prtAbsoluteTolerance", PRTRelightSolver.AbsoluteTolerance);
                command.SetComputeFloatParam(pass.shader, "_prtRelativeTolerance", PRTRelightSolver.RelativeTolerance);
                command.DispatchCompute(pass.shader, pass.kernel, pass.count, 1, 1);
            });
        }

        private void RecordMetadata(RenderGraph graph)
        {
            if (_metadataHash == _volume.MetadataHash) return;
            _metadataHash = _volume.MetadataHash;
            using (var builder = graph.AddUnsafePass<MetadataData>("PRT frame metadata upload", out var data))
            {
                data.buffer = _solver.Metadata;
                data.values = (uint[])_volume.GetValidityMasks().Clone();
                builder.UseBuffer(graph.ImportBuffer(data.buffer), AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MetadataData pass, UnsafeGraphContext context) =>
                    CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).SetBufferData(pass.buffer, pass.values));
            }
        }

        private sealed class CommitData
        {
            public ComputeShader shader;
            public int kernel, count;
            public GraphicsBuffer ids, scratch, residuals, inputMetadata, sh, metadata, ready;
        }

        private void RecordCommit(RenderGraph graph, PRTSectorResident sector)
        {
            using var builder = graph.AddUnsafePass<CommitData>($"PRT commit sector {sector.Index}", out var pass);
            pass.shader = _probeShader; pass.kernel = _commitKernel; pass.count = sector.Data.probes.Length;
            pass.ids = sector.ProbeIds; pass.scratch = sector.Next; pass.residuals = sector.Residuals;
            pass.inputMetadata = _solver.Metadata;
            pass.sh = _solver.Previous; pass.metadata = _solver.PreviousMetadata; pass.ready = _solver.Ready;
            foreach (var input in new[] { pass.ids, pass.scratch, pass.residuals, pass.inputMetadata })
                builder.UseBuffer(graph.ImportBuffer(input), AccessFlags.Read);
            foreach (var output in new[] { pass.sh, pass.metadata, pass.ready })
                builder.UseBuffer(graph.ImportBuffer(output), AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (CommitData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtProbeIds", data.ids);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtNextSH", data.scratch);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtResiduals", data.residuals);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_validityMasks", data.inputMetadata);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtCommittedSH", data.sh);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtCommittedMetadata", data.metadata);
                cmd.SetComputeBufferParam(data.shader, data.kernel, "_prtCommittedReady", data.ready);
                cmd.SetComputeIntParam(data.shader, "_prtProbeCount", data.count);
                cmd.DispatchCompute(data.shader, data.kernel, (data.count + 63) / 64, 1, 1);
            });
            _solver.ScheduleCommit(sector.Index);
        }
    }
}
