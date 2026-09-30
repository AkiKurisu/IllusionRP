using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingPass
    {
        private const string BuildRayGenName = "RayGen_BUILD";

        private const string FillRayGenName = "RayGen_FILL";

        private class RealtimeTracePassData : TracePassData
        {
            internal RayTracingShader FillShader;
            internal PathTracingRealtimeTargets Targets;
            internal int SamplesPerPixel;
            internal TextureHandle StablePlanesHeader;
            internal TextureHandle StableRadiance;
        }

        private class GuidesPassData
        {
            internal ComputeShader Shader;
            internal int DenoiseKernel;
            internal int ImageKernel;
            internal bool RayReconstruction;
            internal GraphicsBuffer Constants;
            internal GraphicsBuffer StablePlanes;
            internal TextureHandle StablePlanesHeader;
            internal TextureHandle StableRadiance;
            internal TextureHandle Depth;
            internal TextureHandle MotionVectors;
            internal TextureHandle SpecularHitT;
            internal TextureHandle SpecularHitTScratch;
            internal TextureHandle Color;
            internal TextureHandle DiffuseAlbedo;
            internal TextureHandle SpecularAlbedo;
            internal TextureHandle NormalsAndRoughness;
            internal TextureHandle SpecularMotionVectors;
            internal int Width;
            internal int Height;
        }

        private RayTracingShader _buildShader;

        private RayTracingShader _fillShader;

        private ComputeShader _realtimeShader;

        private int _denoiseSpecHitTKernel;

        private int _prepareKernel;

        private int _mergeKernel;

        private IPathTracingRayReconstruction _rayReconstruction;

        private readonly ProfilingSampler _realtimeTraceSampler = new("Path Tracing Realtime Trace");

        private readonly ProfilingSampler _guidesSampler = new("Path Tracing Realtime Guides");

        internal bool IsRayReconstructionAvailable => _rayReconstruction is { IsAvailable: true };

        private bool UsesRayReconstruction(PathTracing settings) => settings.rayReconstruction.value && IsRayReconstructionAvailable;

        private void InitializeRealtime(IllusionRenderPipelineResources resources)
        {
            _buildShader = resources.pathTracingBuildRS;
            _fillShader = resources.pathTracingFillRS;
            _realtimeShader = resources.pathTracingRealtimeCS;
            _denoiseSpecHitTKernel = _realtimeShader.FindKernel("DenoiseSpecHitT");
            _prepareKernel = _realtimeShader.FindKernel("PrepareRayReconstructionInputs");
            _mergeKernel = _realtimeShader.FindKernel("MergeStablePlanes");
            _rayReconstruction = PathTracingRayReconstructionLoader.Create();
        }

        private void DisposeRealtime()
        {
            _rayReconstruction?.Dispose();
            _rayReconstruction = null;
        }

        private TextureHandle RecordRealtime(RenderGraph renderGraph, UniversalCameraData cameraData, PathTracing settings,
            PathTracingCameraContext context, PathTracingWorld world)
        {
            var camera = cameraData.camera;
            int width = context.Width;
            int height = context.Height;
            int outputWidth = cameraData.cameraTargetDescriptor.width;
            int outputHeight = cameraData.cameraTargetDescriptor.height;
            var targets = context.GetRealtimeTargets(width, height, outputWidth, outputHeight);
            bool rayReconstruction = UsesRayReconstruction(settings);
            var quality = settings.rayReconstructionQuality.value;

            int settingsHash = HashCode.Combine(settings.bounceCount.value, settings.diffuseBounceCount.value, settings.lightSampling.value,
                settings.fireflyFilterThreshold.value, settings.environmentDiffuseMipOffset.value, settings.layerMask.value,
                settings.realtimeSamplesPerPixel.value, HashCode.Combine(rayReconstruction, quality, width, height, outputWidth, outputHeight));
            context.BeginRealtimeFrame(settingsHash, camera.transform);

            var worldToView = camera.worldToCameraMatrix;
            var viewToClip = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true);
            var previousWorldToView = context.HasRealtimeHistory ? context.PreviousWorldToView : worldToView;
            var previousViewToClip = context.HasRealtimeHistory ? context.PreviousViewToClip : viewToClip;
            var jitter = PathTracingFrameConstants.RealtimeJitter(context.RealtimeFrameIndex, (float)height / outputHeight);
            var cameraConstants = PathTracingFrameConstants.BuildCamera(camera, width, height, Lens, outputHeight);
            cameraConstants.Jitter = jitter;
            var constants = BuildSampleConstants(
                PathTracingFrameConstants.BuildView(worldToView, viewToClip, width, height),
                PathTracingFrameConstants.BuildView(previousWorldToView, previousViewToClip, width, height),
                PathTracingFrameConstants.BuildRealtimePathTracer(settings, context, targets, cameraConstants), width, height);

            var stablePlanesHeader = renderGraph.ImportTexture(targets.StablePlanesHeader);
            var stableRadiance = renderGraph.ImportTexture(targets.StableRadiance);
            RecordRealtimeTrace(renderGraph, context, world, targets, stablePlanesHeader, stableRadiance, constants, cameraData,
                viewToClip * worldToView, settings.realtimeSamplesPerPixel.value);
            var color = RecordGuides(renderGraph, context, targets, stablePlanesHeader, stableRadiance, rayReconstruction);

            var image = color;
            if (rayReconstruction)
            {
                var inputs = new PathTracingRayReconstructionInputs
                {
                    Color = targets.Color,
                    Output = targets.Output,
                    MotionVectors = context.MotionVectors,
                    Depth = context.Depth,
                    DiffuseAlbedo = targets.DiffuseAlbedo,
                    SpecularAlbedo = targets.SpecularAlbedo,
                    NormalsAndRoughness = targets.NormalsAndRoughness,
                    SpecularMotionVectors = targets.SpecularMotionVectors,
                    WorldToView = worldToView,
                    ViewToClip = viewToClip,
                    Jitter = jitter,
                    Width = width,
                    Height = height,
                    OutputWidth = outputWidth,
                    OutputHeight = outputHeight,
                    Quality = quality
                };
                _rayReconstruction.Record(renderGraph, camera, inputs, !context.HasRealtimeHistory);
                image = renderGraph.ImportTexture(targets.Output);
            }
            else
            {
                _rayReconstruction?.Release(camera.GetInstanceID());
            }
            context.EndRealtimeFrame(worldToView, viewToClip);
            return image;
        }

        private void RecordRealtimeTrace(RenderGraph renderGraph, PathTracingCameraContext context, PathTracingWorld world,
            PathTracingRealtimeTargets targets, TextureHandle stablePlanesHeader, TextureHandle stableRadiance,
            in PathTracingSampleConstants constants, UniversalCameraData cameraData, Matrix4x4 worldToClip, int samplesPerPixel)
        {
            using var builder = renderGraph.AddUnsafePass<RealtimeTracePassData>("Path Tracing Realtime Trace", out var passData, _realtimeTraceSampler);
            FillTracePassData(renderGraph, passData, context, world, renderGraph.ImportTexture(context.Radiance), constants, cameraData, worldToClip);
            passData.Shader = _buildShader;
            passData.FillShader = _fillShader;
            passData.Targets = targets;
            passData.SamplesPerPixel = samplesPerPixel;
            passData.StablePlanesHeader = stablePlanesHeader;
            passData.StableRadiance = stableRadiance;
            DeclareTraceTextures(builder, passData);
            builder.UseTexture(stablePlanesHeader, AccessFlags.ReadWrite);
            builder.UseTexture(stableRadiance, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (RealtimeTracePassData data, UnsafeGraphContext graphContext) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(graphContext.cmd);
                PrepareScene(cmd, data);

                data.Resources.UploadConstants(cmd, data.Constants, default);
                BindTraceResources(cmd, data.Shader, data);
                BindStablePlanes(cmd, data.Shader, data.Targets);
                using (new ProfilingScope(cmd, Samplers.StablePlanes))
                    cmd.DispatchRays(data.Shader, BuildRayGenName, (uint)data.Width, (uint)data.Height, 1, data.Camera);
                using (new ProfilingScope(cmd, Samplers.LightFeedback))
                    data.LightBaker.UpdateEnd(cmd, data.Depth, data.MotionVectors, data.Width, data.Height);

                BindTraceResources(cmd, data.FillShader, data);
                BindStablePlanes(cmd, data.FillShader, data.Targets);
                using (new ProfilingScope(cmd, Samplers.Paths))
                {
                    for (int i = 0; i < data.SamplesPerPixel; i++)
                    {
                        var miniConstants = new PathTracingSampleMiniConstants { Params = new Vector4Int { X = (uint)i } };
                        data.Resources.UploadConstants(cmd, data.Constants, miniConstants);
                        cmd.DispatchRays(data.FillShader, FillRayGenName, (uint)data.Width, (uint)data.Height, 1, data.Camera);
                    }
                }
            });
        }

        private TextureHandle RecordGuides(RenderGraph renderGraph, PathTracingCameraContext context, PathTracingRealtimeTargets targets,
            TextureHandle stablePlanesHeader, TextureHandle stableRadiance, bool rayReconstruction)
        {
            using var builder = renderGraph.AddComputePass<GuidesPassData>("Path Tracing Realtime Guides", out var passData, _guidesSampler);
            passData.Shader = _realtimeShader;
            passData.DenoiseKernel = _denoiseSpecHitTKernel;
            passData.ImageKernel = rayReconstruction ? _prepareKernel : _mergeKernel;
            passData.RayReconstruction = rayReconstruction;
            passData.Constants = _resources.Constants;
            passData.StablePlanes = targets.StablePlanes;
            passData.StablePlanesHeader = stablePlanesHeader;
            passData.StableRadiance = stableRadiance;
            passData.Depth = renderGraph.ImportTexture(context.Depth);
            passData.MotionVectors = renderGraph.ImportTexture(context.MotionVectors);
            passData.SpecularHitT = renderGraph.ImportTexture(context.SpecularHitT);
            passData.SpecularHitTScratch = renderGraph.ImportTexture(targets.SpecularHitTScratch);
            passData.Color = renderGraph.ImportTexture(targets.Color);
            passData.DiffuseAlbedo = renderGraph.ImportTexture(targets.DiffuseAlbedo);
            passData.SpecularAlbedo = renderGraph.ImportTexture(targets.SpecularAlbedo);
            passData.NormalsAndRoughness = renderGraph.ImportTexture(targets.NormalsAndRoughness);
            passData.SpecularMotionVectors = renderGraph.ImportTexture(targets.SpecularMotionVectors);
            passData.Width = context.Width;
            passData.Height = context.Height;
            builder.UseTexture(stablePlanesHeader);
            builder.UseTexture(stableRadiance);
            builder.UseTexture(passData.Depth);
            builder.UseTexture(passData.MotionVectors);
            builder.UseTexture(passData.SpecularHitT, AccessFlags.ReadWrite);
            builder.UseTexture(passData.SpecularHitTScratch, AccessFlags.ReadWrite);
            builder.UseTexture(passData.Color, AccessFlags.Write);
            builder.UseTexture(passData.DiffuseAlbedo, AccessFlags.Write);
            builder.UseTexture(passData.SpecularAlbedo, AccessFlags.Write);
            builder.UseTexture(passData.NormalsAndRoughness, AccessFlags.Write);
            builder.UseTexture(passData.SpecularMotionVectors, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (GuidesPassData data, ComputeGraphContext graphContext) =>
            {
                var cmd = graphContext.cmd;
                var shader = data.Shader;
                int groupsX = (data.Width + 7) / 8;
                int groupsY = (data.Height + 7) / 8;

                cmd.SetComputeBufferParam(shader, data.DenoiseKernel, ShaderIDs.t_PathTracingConstants, data.Constants);
                cmd.SetComputeTextureParam(shader, data.DenoiseKernel, ShaderIDs.t_Depth, data.Depth);
                cmd.SetComputeTextureParam(shader, data.DenoiseKernel, ShaderIDs.t_SpecularHitT, data.SpecularHitT);
                cmd.SetComputeTextureParam(shader, data.DenoiseKernel, ShaderIDs.u_SpecularHitT, data.SpecularHitTScratch);
                cmd.DispatchCompute(shader, data.DenoiseKernel, groupsX, groupsY, 1);
                cmd.SetComputeTextureParam(shader, data.DenoiseKernel, ShaderIDs.t_SpecularHitT, data.SpecularHitTScratch);
                cmd.SetComputeTextureParam(shader, data.DenoiseKernel, ShaderIDs.u_SpecularHitT, data.SpecularHitT);
                cmd.DispatchCompute(shader, data.DenoiseKernel, groupsX, groupsY, 1);

                int kernel = data.ImageKernel;
                cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.t_PathTracingConstants, data.Constants);
                cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_StablePlanesHeader, data.StablePlanesHeader);
                cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.t_StablePlanesBuffer, data.StablePlanes);
                cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_StableRadiance, data.StableRadiance);
                cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_OutputColor, data.Color);
                if (data.RayReconstruction)
                {
                    cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_MotionVectors, data.MotionVectors);
                    cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_SpecularHitT, data.SpecularHitT);
                    cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_RRDiffuseAlbedo, data.DiffuseAlbedo);
                    cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_RRSpecAlbedo, data.SpecularAlbedo);
                    cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_RRNormalsAndRoughness, data.NormalsAndRoughness);
                    cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_RRSpecMotionVectors, data.SpecularMotionVectors);
                }
                cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
            });
            return passData.Color;
        }
    }
}
