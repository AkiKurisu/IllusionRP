using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingPass
    {
        private const string ReferenceRayGenName = "RayGen_REF";

        private class TracePassData
        {
            internal RayTracingShader Shader;
            internal PathTracingWorld World;
            internal PathTracingDiffusionProfiles Profiles;
            internal float SubsurfaceRayBias;
            internal int Frame;
            internal PathTracingShaderTime TraceTime;
            internal PathTracingShaderTime CurrentTime;
            internal PathTracingResources Resources;
            internal IllusionRenderPipelineResources PipelineResources;
            internal PathTracingEnvironment Environment;
            internal PathTracingLightCollector Lights;
            internal PathTracingLightTables LightTables;
            internal PathTracingLightBaker LightBaker;
            internal PathTracingSampleConstants Constants;
            internal Camera Camera;
            internal Matrix4x4 ViewMatrix;
            internal Matrix4x4 ProjectionMatrix;
            internal Matrix4x4 WorldToClip;
            internal RTHandle ExposureTexture;
            internal TextureHandle Radiance;
            internal TextureHandle Depth;
            internal TextureHandle MotionVectors;
            internal TextureHandle Throughput;
            internal TextureHandle SpecularHitT;
            internal int Width;
            internal int Height;
        }

        private class AccumulatePassData
        {
            internal ComputeShader Shader;
            internal int Kernel;
            internal TextureHandle Radiance;
            internal TextureHandle Accumulation;
            internal int Width;
            internal int Height;
            internal float Weight;
        }

        private RayTracingShader _referenceShader;

        private ComputeShader _accumulationShader;

        private int _accumulateKernel;

        private readonly ProfilingSampler _traceSampler = new("Path Tracing Trace");

        private readonly ProfilingSampler _accumulateSampler = new("Path Tracing Accumulate");

        private void InitializeReference(IllusionRenderPipelineResources resources)
        {
            _referenceShader = resources.pathTracingRS;
            _accumulationShader = resources.pathTracingAccumulationCS;
            _accumulateKernel = _accumulationShader.FindKernel("Accumulate");
        }

        private TextureHandle RecordReference(RenderGraph renderGraph, UniversalCameraData cameraData, PathTracing settings,
            PathTracingCameraContext context, PathTracingWorld world)
        {
            int maximumSamples = settings.maximumSamples.value;
            var camera = cameraData.camera;
            context.ReleaseRealtime();
            _rayReconstruction?.Release(camera.GetInstanceID());
            int width = context.Width;
            int height = context.Height;

            var worldToView = camera.worldToCameraMatrix;
            var viewToClip = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true);
            var cameraConstants = PathTracingFrameConstants.BuildCamera(camera, width, height, Lens);

            int stateHash = HashCode.Combine(worldToView, viewToClip, world.Scene.SceneHash, _lights.Hash,
                HashCode.Combine(PathTracingEnvironment.ComputeHash(_lights), Lens.ApertureRadius, Lens.FocusDistance, PathTracingDiffusionProfiles.Capture(_rendererData).ComputeHash()),
                HashCode.Combine(settings.bounceCount.value, settings.diffuseBounceCount.value, settings.lightSampling.value,
                    settings.fireflyFilterThreshold.value, settings.environmentDiffuseMipOffset.value, settings.layerMask.value));
            context.UpdateAccumulation(stateHash, world.Scene.TransformsChanged);

            var radiance = renderGraph.ImportTexture(context.Radiance);
            var accumulation = renderGraph.ImportTexture(context.Accumulation);
            if (context.SampleCount < (uint)maximumSamples)
            {
                var view = PathTracingFrameConstants.BuildView(worldToView, viewToClip, width, height);
                var previousView = context.HasReferencePreviousView
                    ? PathTracingFrameConstants.BuildView(context.ReferencePreviousWorldToView, context.ReferencePreviousViewToClip, width, height)
                    : view;
                var constants = BuildSampleConstants(view, previousView,
                    PathTracingFrameConstants.BuildPathTracer(settings, context, cameraConstants, settings.lightSampling.value), width, height);
                RecordReferenceTrace(renderGraph, context, world, radiance, constants, cameraData, viewToClip * worldToView);
                RecordAccumulate(renderGraph, radiance, accumulation, width, height, 1.0f / (context.SampleCount + 1));
                context.AdvanceSample(cameraConstants, worldToView, viewToClip);
            }
            return accumulation;
        }

        private PathTracingSampleConstants BuildSampleConstants(in PathTracingViewConstants view, in PathTracingViewConstants previousView,
            in PathTracerConstants pathTracer, int width, int height)
        {
            return new PathTracingSampleConstants
            {
                View = view,
                PreviousView = previousView,
                EnvMapSceneParams = PathTracingFrameConstants.BuildEnvironment(true, 1.0f, Quaternion.identity),
                EnvMapImportanceSamplingParams = new PathTracingEnvMapImportanceSamplingParams
                {
                    ImportanceInvDim = Vector2.one / PathTracingEnvironment.ImportanceMapSize,
                    ImportanceBaseMip = (uint)(_environment.ImportanceMapMipCount - 1)
                },
                PtConsts = pathTracer,
                Debug = new PathTracingDebugConstants { PickX = -1, PickY = -1, ImageWidth = width, ImageHeight = height }
            };
        }

        private void FillTracePassData(RenderGraph renderGraph, TracePassData passData, PathTracingCameraContext context,
            PathTracingWorld world, TextureHandle radiance, in PathTracingSampleConstants constants, UniversalCameraData cameraData, Matrix4x4 worldToClip)
        {
            passData.SubsurfaceRayBias = _rendererData.ScaleWorldDistance(0.001f);
            passData.Profiles = PathTracingDiffusionProfiles.Capture(_rendererData);
            passData.World = world;
            passData.Frame = PathTracingFrame.Index;
            passData.CurrentTime = PathTracingShaderTime.Current;
            passData.Resources = _resources;
            passData.PipelineResources = _rendererData.RuntimeResources;
            passData.Environment = _environment;
            passData.Lights = _lights;
            passData.LightTables = _lightTables;
            passData.LightBaker = context.GetLightBaker(_lightBakerKernels);
            passData.Constants = constants;
            passData.Camera = cameraData.camera;
            passData.ViewMatrix = cameraData.GetViewMatrix();
            passData.ProjectionMatrix = cameraData.GetProjectionMatrix();
            passData.WorldToClip = worldToClip;
            passData.ExposureTexture = _rendererData.GetExposureTexture();
            passData.Width = context.Width;
            passData.Height = context.Height;
            passData.Radiance = radiance;
            passData.Depth = renderGraph.ImportTexture(context.Depth);
            passData.MotionVectors = renderGraph.ImportTexture(context.MotionVectors);
            passData.Throughput = renderGraph.ImportTexture(context.Throughput);
            passData.SpecularHitT = renderGraph.ImportTexture(context.SpecularHitT);
        }

        private static void DeclareTraceTextures(IUnsafeRenderGraphBuilder builder, TracePassData passData)
        {
            builder.UseTexture(passData.Radiance, AccessFlags.Write);
            builder.UseTexture(passData.Depth, AccessFlags.ReadWrite);
            builder.UseTexture(passData.MotionVectors, AccessFlags.ReadWrite);
            builder.UseTexture(passData.Throughput, AccessFlags.Write);
            builder.UseTexture(passData.SpecularHitT, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
        }

        private static void PrepareScene(CommandBuffer cmd, TracePassData data)
        {
            var lights = data.Lights;
            using (new ProfilingScope(cmd, Samplers.Environment))
            {
                data.Environment.Update(cmd, lights, data.ExposureTexture);
                cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjectionMatrix);
            }
            using (new ProfilingScope(cmd, Samplers.Lights))
            {
                data.World.Emissive.Prepare(cmd);
                data.LightBaker.UpdateBegin(cmd, lights, data.Environment, data.World.Emissive, data.Camera.transform.position, data.WorldToClip, data.Width, data.Height);
                data.LightTables.Upload(cmd, lights);
            }
            using (new ProfilingScope(cmd, Samplers.Scene))
                data.World.Build(cmd, data.Frame);
            data.World.BindHitShaderGlobals(cmd);
        }

        private void RecordReferenceTrace(RenderGraph renderGraph, PathTracingCameraContext context, PathTracingWorld world, TextureHandle radiance,
            in PathTracingSampleConstants constants, UniversalCameraData cameraData, Matrix4x4 worldToClip)
        {
            using var builder = renderGraph.AddUnsafePass<TracePassData>("Path Tracing Trace", out var passData, _traceSampler);
            FillTracePassData(renderGraph, passData, context, world, radiance, constants, cameraData, worldToClip);
            passData.Shader = _referenceShader;
            passData.TraceTime = context.AccumulationTime;
            DeclareTraceTextures(builder, passData);

            builder.SetRenderFunc(static (TracePassData data, UnsafeGraphContext graphContext) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(graphContext.cmd);
                PrepareScene(cmd, data);
                using (new ProfilingScope(cmd, Samplers.LightFeedback))
                    data.LightBaker.UpdateEnd(cmd, data.Depth, data.MotionVectors, data.Width, data.Height);
                data.Resources.UploadConstants(cmd, data.Constants, default);
                BindTraceResources(cmd, data.Shader, data);
                data.TraceTime.Push(cmd);
                using (new ProfilingScope(cmd, Samplers.Paths))
                    cmd.DispatchRays(data.Shader, ReferenceRayGenName, (uint)data.Width, (uint)data.Height, 1, data.Camera);
                data.CurrentTime.Push(cmd);
            });
        }

        private void RecordAccumulate(RenderGraph renderGraph, TextureHandle radiance, TextureHandle accumulation,
            int width, int height, float weight)
        {
            using var builder = renderGraph.AddComputePass<AccumulatePassData>("Path Tracing Accumulate", out var passData, _accumulateSampler);
            passData.Shader = _accumulationShader;
            passData.Kernel = _accumulateKernel;
            passData.Radiance = radiance;
            passData.Accumulation = accumulation;
            passData.Width = width;
            passData.Height = height;
            passData.Weight = weight;
            builder.UseTexture(radiance);
            builder.UseTexture(accumulation, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (AccumulatePassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._PathTracingRadiance, data.Radiance);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._PathTracingAccumulation, data.Accumulation);
                cmd.SetComputeIntParams(data.Shader, ShaderIDs._PathTracingSize, data.Width, data.Height);
                cmd.SetComputeFloatParam(data.Shader, ShaderIDs._PathTracingSampleWeight, data.Weight);
                cmd.DispatchCompute(data.Shader, data.Kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
            });
        }
    }
}
