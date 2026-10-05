using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTRelightPass
    {
        private struct ReflectionProbeData
        {
            public Vector4 L0L1, L2_1;
            public float L2_2;
            public int normalizeWithProbeVolume;
            public Vector2 padding;
        }
        private GraphicsBuffer _reflectionBuffer;
        private ReflectionProbeData[] _reflectionValues;
        private void InitializeReflectionNormalization()
        {
            _reflectionValues = new ReflectionProbeData[UniversalRenderPipeline.maxVisibleReflectionProbes];
            _reflectionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _reflectionValues.Length, 48);
        }
        private sealed class ReflectionData
        {
            public GraphicsBuffer buffer;
            public Vector4 normalization;
            public ReflectionProbeData[] values;
        }
        private void RecordReflectionNormalization(RenderGraph graph, UniversalRenderingData rendering)
        {
            Array.Clear(_reflectionValues, 0, _reflectionValues.Length);
            var probes = rendering.cullResults.visibleReflectionProbes;
            for (int i = 0; i < probes.Length && i < _reflectionValues.Length; i++)
            {
                if (!PRTVolumeManager.TryGetReflectionProbeAdditionalData(probes[i].reflectionProbe, out var additional)
                    || !additional.TryGetSHForNormalization(out Vector4 l01, out Vector4 l21, out float l22))
                    continue;
                _reflectionValues[i] = new ReflectionProbeData
                {
                    L0L1 = l01, L2_1 = l21, L2_2 = l22, normalizeWithProbeVolume = 1
                };
            }
            var settings = VolumeManager.instance.stack.GetComponent<ReflectionNormalization>();
            using var builder = graph.AddUnsafePass<ReflectionData>("PRT reflection normalization publication", out var data);
            data.buffer = _reflectionBuffer;
            data.values = (ReflectionProbeData[])_reflectionValues.Clone();
            data.normalization = settings != null && settings.IsActive()
                ? new Vector4(settings.minNormalizationFactor.value, settings.maxNormalizationFactor.value, 0, settings.probeVolumeWeight.value)
                : Vector4.zero;
            builder.UseBuffer(graph.ImportBuffer(data.buffer), AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (ReflectionData pass, UnsafeGraphContext context) =>
            {
                var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                command.SetBufferData(pass.buffer, pass.values);
                command.SetGlobalBuffer("_reflectionProbeNormalizationData", pass.buffer);
                command.SetGlobalVector("_reflectionProbeNormalizationFactor", pass.normalization);
            });
        }
    }
}
