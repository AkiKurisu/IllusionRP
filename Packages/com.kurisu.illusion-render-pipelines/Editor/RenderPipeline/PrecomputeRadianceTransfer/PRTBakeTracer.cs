using System;
using System.Runtime.InteropServices;
using Illusion.Rendering.PathTracing;
using Illusion.Rendering.PRTGI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    /// <summary>
    /// Traces bake rays against a snapshot of the scene, shading hits with the materials' path tracing passes.
    /// </summary>
    internal sealed class PRTBakeTracer : IDisposable
    {
        private const uint SceneMask = 1, SolidMask = 2;
        private const string CaptureRayGen = "CaptureRayGen", PlacementRayGen = "PlacementRayGen";
        private readonly RayTracingShader _shader;
        private readonly RayTracingAccelerationStructure _scene;
        private readonly GraphicsBuffer _instances, _instanceData, _materialRanges, _culledSubMeshes, _subMeshMaterials, _materials,
            _previousPositions, _empty4, _empty2, _emptySamples;
        private readonly float _sceneTime;
        private bool _built;

        internal PRTBakeTracer(RayTracingShader shader, PRTBakeInstance[] instances, float sceneTime)
        {
            if (!SystemInfo.supportsRayTracing)
                throw new NotSupportedException("PRT baking requires DirectX 12 with hardware ray tracing.");
            if (!shader) throw new InvalidOperationException("PRT bake ray tracing shader is unavailable.");
            _shader = shader;
            _sceneTime = sceneTime;
            _scene = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings(
                RayTracingAccelerationStructure.ManagementMode.Manual, RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1));
            int count = Mathf.Max(1, instances.Length);
            var table = new uint[count * 4];
            var data = new PathTracingInstanceData[count];
            for (int i = 0; i < instances.Length; i++)
            {
                PRTBakeInstance item = instances[i];
                var config = new RayTracingMeshInstanceConfig(item.Mesh, (uint)item.SubmeshIndex, item.Material)
                {
                    materialProperties = item.Properties,
                    subMeshFlags = item.AnyHit ? RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls
                        : RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                    // Facing is decided in object space, so mirrored transforms keep the authored winding.
                    enableTriangleCulling = item.Cull != CullMode.Off,
                    frontTriangleCounterClockwise = item.Cull == CullMode.Front,
                    mask = item.Solid ? SceneMask | SolidMask : SceneMask
                };
                _scene.AddInstance(config, item.LocalToWorld, null, (uint)i);
                table[i * 4] = item.RenderingLayers;
                table[i * 4 + 1] = item.ObjectLayerMask;
                table[i * 4 + 2] = item.MaterialKey;
                data[i] = new PathTracingInstanceData
                {
                    PreviousPositionBase = PathTracingInstanceData.NoPositionHistory, RenderingLayers = item.RenderingLayers
                };
            }
            _instances = Create(GraphicsBuffer.Target.Structured, count, 16, table, "PRT Bake Instances");
            _instanceData = Create(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<PathTracingInstanceData>(), data, "PRT Bake Path Tracing Instances");
            _materialRanges = Create(GraphicsBuffer.Target.Structured, count, 8, new uint[count * 2], "PRT Bake Material Ranges");
            _culledSubMeshes = Create(GraphicsBuffer.Target.Structured, 1, 8, new uint[2], "PRT Bake Culled Submeshes");
            _subMeshMaterials = Create(GraphicsBuffer.Target.Structured, 1, 8, new uint[2], "PRT Bake Submesh Materials");
            _materials = Create(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<PathTracingMaterialData>(),
                new PathTracingMaterialData[1], "PRT Bake Materials");
            _previousPositions = Create(GraphicsBuffer.Target.Raw, 3, 4, new uint[3], "PRT Bake Previous Positions");
            _empty4 = Create(GraphicsBuffer.Target.Structured, 1, 16, new uint[4], "PRT Bake Empty");
            _empty2 = Create(GraphicsBuffer.Target.Structured, 1, 8, new uint[2], "PRT Bake Empty Biases");
            _emptySamples = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, PRTCaptureSample.Stride) { name = "PRT Bake Empty Samples" };
        }

        private static GraphicsBuffer Create<T>(GraphicsBuffer.Target target, int count, int stride, T[] data, string name) where T : struct
        {
            var buffer = new GraphicsBuffer(target, count, stride) { name = name };
            buffer.SetData(data);
            return buffer;
        }

        /// <summary>
        /// Records one ray per probe and direction into <paramref name="samples"/>, probe-major.
        /// </summary>
        internal void Capture(CommandBuffer cmd, GraphicsBuffer probes, int probeCount, GraphicsBuffer directions, int sampleCount,
            GraphicsBuffer samples)
        {
            Bind(cmd, probes, directions, _empty2, samples, _empty4);
            cmd.SetRayTracingIntParam(_shader, ShaderIDs.SampleCount, sampleCount);
            cmd.SetRayTracingFloatParam(_shader, ShaderIDs.ConeSpread, Mathf.Sqrt(4f * Mathf.PI / sampleCount));
            Dispatch(cmd, CaptureRayGen, (uint)sampleCount, (uint)probeCount);
        }

        /// <summary>
        /// Writes each probe's placement offset (xyz) and whether it ends outside solid geometry (w).
        /// </summary>
        internal void Place(CommandBuffer cmd, GraphicsBuffer probes, GraphicsBuffer biases, int probeCount, float searchDistance,
            GraphicsBuffer placements)
        {
            Bind(cmd, probes, _empty4, biases, _emptySamples, placements);
            cmd.SetRayTracingFloatParam(_shader, ShaderIDs.SearchDistance, searchDistance);
            Dispatch(cmd, PlacementRayGen, (uint)probeCount, 1);
        }

        private void Bind(CommandBuffer cmd, GraphicsBuffer probes, GraphicsBuffer directions, GraphicsBuffer biases,
            GraphicsBuffer samples, GraphicsBuffer placements)
        {
            if (!_built)
            {
                cmd.BuildRayTracingAccelerationStructure(_scene);
                _built = true;
            }
            cmd.SetGlobalBuffer(ShaderIDs.PathTracingInstanceData, _instanceData);
            cmd.SetGlobalBuffer(ShaderIDs.PathTracingCulledSubMeshes, _culledSubMeshes);
            cmd.SetGlobalBuffer(ShaderIDs.PathTracingPreviousPositions, _previousPositions);
            cmd.SetGlobalBuffer(ShaderIDs.PathTracingMaterialRanges, _materialRanges);
            cmd.SetGlobalBuffer(ShaderIDs.PathTracingSubMeshMaterials, _subMeshMaterials);
            cmd.SetGlobalBuffer(ShaderIDs.PathTracingMaterials, _materials);
            cmd.SetGlobalInt(ShaderIDs.PathTracingMaterialCount, 0);
            cmd.SetRayTracingShaderPass(_shader, PathTracingPass.MaterialPassName);
            cmd.SetRayTracingAccelerationStructure(_shader, ShaderIDs.Scene, _scene);
            cmd.SetRayTracingBufferParam(_shader, ShaderIDs.Instances, _instances);
            cmd.SetRayTracingBufferParam(_shader, ShaderIDs.Probes, probes);
            cmd.SetRayTracingBufferParam(_shader, ShaderIDs.Directions, directions);
            cmd.SetRayTracingBufferParam(_shader, ShaderIDs.Biases, biases);
            cmd.SetRayTracingBufferParam(_shader, ShaderIDs.Samples, samples);
            cmd.SetRayTracingBufferParam(_shader, ShaderIDs.Placements, placements);
        }

        // Material passes see the scene time the bake signs.
        private void Dispatch(CommandBuffer cmd, string rayGen, uint width, uint height)
        {
            var original = new Vector4[ShaderIDs.Time.Length];
            for (int i = 0; i < original.Length; i++) original[i] = Shader.GetGlobalVector(ShaderIDs.Time[i]);
            float t = _sceneTime;
            var parameters = new Vector4(t, Mathf.Sin(t), Mathf.Cos(t), 0);
            cmd.SetGlobalVector(ShaderIDs.Time[0], new Vector4(t / 20f, t, t * 2f, t * 3f));
            cmd.SetGlobalVector(ShaderIDs.Time[1], new Vector4(Mathf.Sin(t / 8f), Mathf.Sin(t / 4f), Mathf.Sin(t / 2f), Mathf.Sin(t)));
            cmd.SetGlobalVector(ShaderIDs.Time[2], new Vector4(Mathf.Cos(t / 8f), Mathf.Cos(t / 4f), Mathf.Cos(t / 2f), Mathf.Cos(t)));
            cmd.SetGlobalVector(ShaderIDs.Time[3], parameters);
            cmd.SetGlobalVector(ShaderIDs.Time[4], parameters);
            cmd.DispatchRays(_shader, rayGen, width, height, 1);
            for (int i = 0; i < original.Length; i++) cmd.SetGlobalVector(ShaderIDs.Time[i], original[i]);
        }

        public void Dispose()
        {
            _scene.Dispose();
            foreach (GraphicsBuffer buffer in new[] { _instances, _instanceData, _materialRanges, _culledSubMeshes, _subMeshMaterials,
                         _materials, _previousPositions, _empty4, _empty2, _emptySamples })
                buffer.Release();
        }

        private static class ShaderIDs
        {
            public static readonly int Scene = Shader.PropertyToID("_PRTBakeScene");
            public static readonly int Instances = Shader.PropertyToID("_PRTBakeInstances");
            public static readonly int Probes = Shader.PropertyToID("_PRTBakeProbes");
            public static readonly int Directions = Shader.PropertyToID("_PRTBakeDirections");
            public static readonly int Biases = Shader.PropertyToID("_PRTBakeBiases");
            public static readonly int Samples = Shader.PropertyToID("_PRTBakeSamples");
            public static readonly int Placements = Shader.PropertyToID("_PRTBakePlacements");
            public static readonly int SampleCount = Shader.PropertyToID("_PRTBakeSampleCount");
            public static readonly int ConeSpread = Shader.PropertyToID("_PRTBakeConeSpread");
            public static readonly int SearchDistance = Shader.PropertyToID("_PRTBakeSearchDistance");
            public static readonly int PathTracingInstanceData = Shader.PropertyToID("_PathTracingInstanceData");
            public static readonly int PathTracingCulledSubMeshes = Shader.PropertyToID("_PathTracingCulledSubMeshes");
            public static readonly int PathTracingPreviousPositions = Shader.PropertyToID("_PathTracingPreviousPositions");
            public static readonly int PathTracingMaterialRanges = Shader.PropertyToID("_PathTracingMaterialRanges");
            public static readonly int PathTracingSubMeshMaterials = Shader.PropertyToID("_PathTracingSubMeshMaterials");
            public static readonly int PathTracingMaterials = Shader.PropertyToID("_PathTracingMaterials");
            public static readonly int PathTracingMaterialCount = Shader.PropertyToID("_PathTracingMaterialCount");
            public static readonly int[] Time =
            {
                Shader.PropertyToID("_Time"), Shader.PropertyToID("_SinTime"), Shader.PropertyToID("_CosTime"),
                Shader.PropertyToID("_TimeParameters"), Shader.PropertyToID("_LastTimeParameters")
            };
        }
    }
}
