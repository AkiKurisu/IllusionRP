using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingPass : ScriptableRenderPass, IDisposable
    {
        internal const string MaterialPassName = "PathTracing";

        private const int ContextReleaseFrames = 60;

        private class OutputPassData
        {
            internal Material Material;
            internal TextureHandle Source;
            internal TextureHandle Depth;
        }

        private class DepthTexturePassData
        {
            internal Material Material;
            internal TextureHandle Depth;
            internal int ShaderPass;
        }

        private readonly Material _outputMaterial;

        private readonly IllusionRendererData _rendererData;

        private readonly PathTracingResources _resources;

        private readonly PathTracingEnvironment _environment;

        private readonly PathTracingLightCollector _lights = new();

        private readonly PathTracingRectangleLights _rectangleLights;

        private readonly PathTracingLightBakerKernels _lightBakerKernels;

        private readonly ComputeShader _emissiveShader;

        private readonly ComputeShader _motionShader;

        private readonly Dictionary<int, PathTracingWorld> _worlds = new();

        private readonly Dictionary<int, PathTracingCameraContext> _contexts = new();

        private readonly List<int> _expired = new();

        private readonly ProfilingSampler _outputSampler = new("Path Tracing Output");

        private readonly ProfilingSampler _depthTextureSampler = new("Path Tracing Depth Texture");

        private static class Samplers
        {
            public static readonly ProfilingSampler Environment = new("Path Tracing Environment");
            public static readonly ProfilingSampler Lights = new("Path Tracing Lights");
            public static readonly ProfilingSampler Scene = new("Path Tracing Acceleration Structure");
            public static readonly ProfilingSampler LightFeedback = new("Path Tracing Light Feedback");
            public static readonly ProfilingSampler StablePlanes = new("Path Tracing Stable Planes");
            public static readonly ProfilingSampler Paths = new("Path Tracing Paths");
        }

        public PathTracingPass(IllusionRenderPipelineResources resources, IllusionRendererData rendererData)
        {
            _resources = new PathTracingResources(resources.pathTracingFGDShader);
            profilingSampler = new ProfilingSampler("Path Tracing");
            renderPassEvent = IllusionRenderPassEvent.PathTracingPass;
            _outputMaterial = CoreUtils.CreateEngineMaterial(resources.pathTracingOutputShader);
            _rendererData = rendererData;
            _rectangleLights = new PathTracingRectangleLights(resources.pathTracingRectangleLightShader);
            _environment = new PathTracingEnvironment(resources.pathTracingEnvironmentCS, resources.pathTracingEnvironmentLightingShader);
            _lightBakerKernels = new PathTracingLightBakerKernels(resources.pathTracingLightsBakerCS);
            _emissiveShader = resources.pathTracingEmissiveCS;
            _motionShader = resources.pathTracingMotionCS;
            InitializeReference(resources);
            InitializeRealtime(resources);
        }

        public static bool IsSupported => SystemInfo.supportsRayTracing && SystemInfo.supportsRayTracingShaders;

        internal PathTracingLens Lens { get; set; }

        internal static bool UsesRealtime(Camera camera, PathTracing settings)
        {
            return settings.mode.value == PathTracingMode.Realtime && camera.cameraType != CameraType.SceneView;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var settings = VolumeManager.instance.stack.GetComponent<PathTracing>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var camera = cameraData.camera;
            int width = cameraData.cameraTargetDescriptor.width;
            int height = cameraData.cameraTargetDescriptor.height;
            bool realtime = UsesRealtime(camera, settings);

            if (realtime && UsesRayReconstruction(settings))
            {
                float scale = PathTracingRayReconstructionScale.Of(settings.rayReconstructionQuality.value);
                width = PathTracingRayReconstructionScale.Apply(width, scale);
                height = PathTracingRayReconstructionScale.Apply(height, scale);
            }

            var context = GetContext(camera);
            context.EnsureTargets(width, height);
            var world = GetWorld(settings.layerMask.value);
            _lights.Collect(settings.directionalAngularDiameter.value);
            _rectangleLights.Update(_lights.RectangleLights);
            world.Update(camera, settings.layerMask.value, realtime);

            TextureHandle image = realtime
                ? RecordRealtime(renderGraph, cameraData, settings, context, world)
                : RecordReference(renderGraph, cameraData, settings, context, world);
            var depth = renderGraph.ImportTexture(context.Depth);
            RecordOutput(renderGraph, image, depth, resourceData.activeColorTexture, resourceData.activeDepthTexture);
            if (resourceData.cameraDepthTexture.IsValid())
                RecordDepthTexture(renderGraph, depth, resourceData.cameraDepthTexture);

            var neuralRenderingInputs = frameData.GetOrCreate<DLSSNeuralRenderingInputs>();
            neuralRenderingInputs.Depth = depth;
            neuralRenderingInputs.Motion = renderGraph.ImportTexture(context.MotionVectors);
            neuralRenderingInputs.MotionScale = new Vector2(-1.0f / context.Width, -1.0f / context.Height);
        }

        private void RecordOutput(RenderGraph renderGraph, TextureHandle image, TextureHandle depth,
            TextureHandle cameraColor, TextureHandle cameraDepth)
        {
            using var builder = renderGraph.AddRasterRenderPass<OutputPassData>("Path Tracing Output", out var passData, _outputSampler);
            passData.Material = _outputMaterial;
            passData.Source = image;
            passData.Depth = depth;
            builder.UseTexture(image);
            builder.UseTexture(depth);
            builder.SetRenderAttachment(cameraColor, 0, AccessFlags.WriteAll);
            builder.SetRenderAttachmentDepth(cameraDepth, AccessFlags.WriteAll);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (OutputPassData data, RasterGraphContext context) =>
            {
                data.Material.SetTexture(ShaderIDs._PathTracingDepth, (RTHandle)data.Depth);
                Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), data.Material, 0);
            });
        }

        private void RecordDepthTexture(RenderGraph renderGraph, TextureHandle depth, TextureHandle cameraDepthTexture)
        {
            bool depthFormat = GraphicsFormatUtility.IsDepthFormat(renderGraph.GetTextureDesc(cameraDepthTexture).format);
            using var builder = renderGraph.AddRasterRenderPass<DepthTexturePassData>("Path Tracing Depth Texture", out var passData, _depthTextureSampler);
            passData.Material = _outputMaterial;
            passData.Depth = depth;
            passData.ShaderPass = depthFormat ? 1 : 2;
            builder.UseTexture(depth);
            if (depthFormat)
                builder.SetRenderAttachmentDepth(cameraDepthTexture, AccessFlags.WriteAll);
            else
                builder.SetRenderAttachment(cameraDepthTexture, 0, AccessFlags.WriteAll);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (DepthTexturePassData data, RasterGraphContext context) =>
            {
                data.Material.SetTexture(ShaderIDs._PathTracingDepth, (RTHandle)data.Depth);
                Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), data.Material, data.ShaderPass);
            });
        }

        private PathTracingCameraContext GetContext(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (!_contexts.TryGetValue(id, out var context))
            {
                context = new PathTracingCameraContext();
                _contexts.Add(id, context);
            }
            context.LastUsedFrame = PathTracingFrame.Index;
            return context;
        }

        private PathTracingWorld GetWorld(int layerMask)
        {
            if (!_worlds.TryGetValue(layerMask, out var world))
            {
                world = new PathTracingWorld(_emissiveShader, _motionShader);
                _worlds.Add(layerMask, world);
            }
            return world;
        }

        internal void ReleaseUnused()
        {
            _expired.Clear();
            foreach (var pair in _contexts)
            {
                if (PathTracingFrame.Index - pair.Value.LastUsedFrame > ContextReleaseFrames)
                    _expired.Add(pair.Key);
            }
            foreach (int id in _expired)
            {
                _contexts[id].Dispose();
                _contexts.Remove(id);
                _rayReconstruction?.Release(id);
            }

            _expired.Clear();
            foreach (var pair in _worlds)
            {
                if (PathTracingFrame.Index - pair.Value.LastUsedFrame > ContextReleaseFrames)
                    _expired.Add(pair.Key);
            }
            foreach (int layerMask in _expired)
            {
                _worlds[layerMask].Dispose();
                _worlds.Remove(layerMask);
            }
            if (_contexts.Count == 0)
                _environment.Release();
        }

        public void Dispose()
        {
            foreach (var context in _contexts.Values)
                context.Dispose();
            _contexts.Clear();
            foreach (var world in _worlds.Values)
                world.Dispose();
            _worlds.Clear();
            _rectangleLights.Dispose();
            DisposeRealtime();
            _resources.Dispose();
            _environment.Dispose();
            CoreUtils.Destroy(_outputMaterial);
        }
    }
}
