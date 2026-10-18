using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering
{
    public enum PathTracingInlineValidationView
    {
        None,
        Inline,
        HitShader,
        Difference
    }
}

#if DEVELOPMENT_BUILD || UNITY_EDITOR

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingInlineValidation : IDisposable
    {
        private const string ReferenceRayGen = "PathTracingInlineValidationRayGen";
        private const int StatCount = 16;

        private static readonly string[] s_StatNames =
        {
            "pixels", "miss", "hitMismatch", "instanceMismatch", "uncovered", "valueMismatch", "match",
            "albedo", "normal", "specularRoughness", "emission", "distance", "family", "material"
        };

        private readonly RayTracingShader _reference;
        private readonly uint[] _zero = new uint[StatCount];
        private GraphicsBuffer _referenceSamples;
        private GraphicsBuffer _inlineSamples;
        private GraphicsBuffer _stats;
        private RTHandle _output;

        public static string Summary { get; private set; } = string.Empty;

        public static uint[] Counts { get; } = new uint[StatCount];

        public PathTracingInlineValidation(RayTracingShader reference)
        {
            _reference = reference;
        }

        private sealed class PassData
        {
            internal RayTracingShader Reference;
            internal ComputeShader Validation;
            internal PathTracingWorld World;
            internal GraphicsBuffer ReferenceSamples;
            internal GraphicsBuffer InlineSamples;
            internal GraphicsBuffer Stats;
            internal TextureHandle Output;
            internal uint[] Zero;
            internal Matrix4x4 PixelToWorld;
            internal Vector4 Camera;
            internal Camera CameraObject;
            internal int Width;
            internal int Height;
            internal int View;
        }

        public TextureHandle Record(RenderGraph renderGraph, Camera camera, PathTracingWorld world, int width, int height,
            PathTracingInlineValidationView view)
        {
            PathTracingInlineRegistry registry = PathTracingInlineRegistry.Instance;
            if (!_reference || !registry || !registry.Validation || !PathTracingInlineResources.IsAvailable)
                return TextureHandle.nullHandle;
            world.UpdateInline();
            if (!world.Inline.IsReady)
                return TextureHandle.nullHandle;

            int pixels = width * height;
            Ensure(ref _referenceSamples, pixels * 4, "PathTracingInlineValidationReference");
            Ensure(ref _inlineSamples, pixels * 4, "PathTracingInlineValidationInline");
            _stats ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, StatCount, 4) { name = "PathTracingInlineValidationStats" };
            if (_output == null || _output.rt.width != width || _output.rt.height != height)
            {
                _output?.Release();
                _output = RTHandles.Alloc(width, height, colorFormat: GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite: true,
                    name: "_PathTracingInlineValidation");
            }

            Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            var pixelToNdc = Matrix4x4.identity;
            pixelToNdc.m00 = 2f / width;
            pixelToNdc.m03 = -1f;
            pixelToNdc.m11 = 2f / height;
            pixelToNdc.m13 = -1f;
            float spread = 2f * Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad) / height;

            TextureHandle output = renderGraph.ImportTexture(_output);
            using var builder = renderGraph.AddUnsafePass<PassData>("Path Tracing Inline Validation", out var passData);
            passData.Reference = _reference;
            passData.Validation = registry.Validation;
            passData.World = world;
            passData.ReferenceSamples = _referenceSamples;
            passData.InlineSamples = _inlineSamples;
            passData.Stats = _stats;
            passData.Output = output;
            passData.Zero = _zero;
            passData.PixelToWorld = viewProjection.inverse * pixelToNdc;
            passData.Camera = new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, spread);
            passData.CameraObject = camera;
            passData.Width = width;
            passData.Height = height;
            passData.View = (int)view;
            builder.UseTexture(output, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetBufferData(data.Stats, data.Zero);
                RayTracingAccelerationStructure scene = data.World.Scene.AccelerationStructure;

                RayTracingShader reference = data.Reference;
                cmd.SetRayTracingShaderPass(reference, PathTracingPass.MaterialPassName);
                cmd.SetRayTracingAccelerationStructure(reference, ShaderIDs.SceneBVH, scene);
                cmd.SetRayTracingBufferParam(reference, ShaderIDs._PathTracingInlineValidationSamples, data.ReferenceSamples);
                cmd.SetRayTracingMatrixParam(reference, ShaderIDs._PathTracingInlineValidationPixelToWorld, data.PixelToWorld);
                cmd.SetRayTracingVectorParam(reference, ShaderIDs._PathTracingInlineValidationCamera, data.Camera);
                cmd.SetRayTracingIntParams(reference, ShaderIDs._PathTracingInlineValidationSize, data.Width, data.Height, data.View, 0);
                cmd.DispatchRays(reference, ReferenceRayGen, (uint)data.Width, (uint)data.Height, 1, data.CameraObject);

                ComputeShader validation = data.Validation;
                int trace = validation.FindKernel("PathTracingInlineValidate");
                data.World.BindInline(cmd, validation, trace);
                cmd.SetRayTracingAccelerationStructure(validation, trace, ShaderIDs.SceneBVH, scene);
                cmd.SetComputeBufferParam(validation, trace, ShaderIDs._PathTracingInlineValidationSamples, data.InlineSamples);
                cmd.SetComputeMatrixParam(validation, ShaderIDs._PathTracingInlineValidationPixelToWorld, data.PixelToWorld);
                cmd.SetComputeVectorParam(validation, ShaderIDs._PathTracingInlineValidationCamera, data.Camera);
                cmd.SetComputeIntParams(validation, ShaderIDs._PathTracingInlineValidationSize, data.Width, data.Height, data.View, 0);
                cmd.DispatchCompute(validation, trace, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);

                int compare = validation.FindKernel("PathTracingInlineValidationCompare");
                cmd.SetComputeBufferParam(validation, compare, ShaderIDs._PathTracingInlineValidationReference, data.ReferenceSamples);
                cmd.SetComputeBufferParam(validation, compare, ShaderIDs._PathTracingInlineValidationInline, data.InlineSamples);
                cmd.SetComputeBufferParam(validation, compare, ShaderIDs._PathTracingInlineValidationStats, data.Stats);
                cmd.SetComputeTextureParam(validation, compare, ShaderIDs._PathTracingInlineValidationOutput, data.Output);
                cmd.DispatchCompute(validation, compare, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);

                PathTracingInlineScene inline = data.World.Inline;
                string scene2 = $"evaluated {inline.EvaluatedGeometries} exceptions {inline.ExceptionGeometries} uncoveredInstances {inline.UncoveredInstances} " +
                                $"textures {PathTracingInlineResources.TextureCount} meshes {PathTracingInlineResources.MeshCount} entries {PathTracingInlineResources.UsedSlots}";
                cmd.RequestAsyncReadback(data.Stats, request =>
                {
                    if (request.hasError)
                        return;
                    var values = request.GetData<uint>();
                    var text = new System.Text.StringBuilder();
                    for (int i = 0; i < s_StatNames.Length; i++)
                    {
                        Counts[i] = values[i];
                        text.Append(s_StatNames[i]).Append(' ').Append(values[i]).Append(' ');
                    }
                    Summary = text.Append("| ").Append(scene2).ToString();
                });
            });
            return output;
        }

        private static void Ensure(ref GraphicsBuffer buffer, int count, string name)
        {
            if (buffer != null && buffer.count >= count)
                return;
            buffer?.Release();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16) { name = name };
        }

        public void Dispose()
        {
            _referenceSamples?.Release();
            _inlineSamples?.Release();
            _stats?.Release();
            _output?.Release();
            _referenceSamples = _inlineSamples = _stats = null;
            _output = null;
        }

        private static class ShaderIDs
        {
            public static readonly int SceneBVH = Shader.PropertyToID("SceneBVH");
            public static readonly int _PathTracingInlineValidationSamples = Shader.PropertyToID("_PathTracingInlineValidationSamples");
            public static readonly int _PathTracingInlineValidationReference = Shader.PropertyToID("_PathTracingInlineValidationReference");
            public static readonly int _PathTracingInlineValidationInline = Shader.PropertyToID("_PathTracingInlineValidationInline");
            public static readonly int _PathTracingInlineValidationOutput = Shader.PropertyToID("_PathTracingInlineValidationOutput");
            public static readonly int _PathTracingInlineValidationStats = Shader.PropertyToID("_PathTracingInlineValidationStats");
            public static readonly int _PathTracingInlineValidationPixelToWorld = Shader.PropertyToID("_PathTracingInlineValidationPixelToWorld");
            public static readonly int _PathTracingInlineValidationCamera = Shader.PropertyToID("_PathTracingInlineValidationCamera");
            public static readonly int _PathTracingInlineValidationSize = Shader.PropertyToID("_PathTracingInlineValidationSize");
        }
    }
}
#endif
