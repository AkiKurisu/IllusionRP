using System;
using System.Collections.Generic;
using Illusion.Rendering.Shadows;
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

        private readonly PathTracingSharedScene _scene;

        private readonly Dictionary<int, PathTracingCameraContext> _contexts = new();

        private readonly List<int> _expired = new();

        private readonly ProfilingSampler _outputSampler = new("Path Tracing Output");

        private readonly ProfilingSampler _depthTextureSampler = new("Path Tracing Depth Texture");

        private static class Samplers
        {
            public static readonly ProfilingSampler LightFeedback = new("Path Tracing Light Feedback");
            public static readonly ProfilingSampler StablePlanes = new("Path Tracing Stable Planes");
            public static readonly ProfilingSampler Paths = new("Path Tracing Paths");
        }

        public PathTracingPass(IllusionRenderPipelineResources resources, IllusionRendererData rendererData, PathTracingSharedScene scene)
        {
            _scene = scene;
            profilingSampler = new ProfilingSampler("Path Tracing");
            renderPassEvent = IllusionRenderPassEvent.PathTracingPass;
            _outputMaterial = CoreUtils.CreateEngineMaterial(resources.pathTracingOutputShader);
            _rendererData = rendererData;
            InitializeReference(resources);
            InitializeRealtime(resources);
        }

        internal PathTracingLens Lens { get; set; }

        internal static bool UsesRealtime(Camera camera, PathTracing settings)
        {
            return settings.mode.value == PathTracingMode.Realtime && camera.cameraType != CameraType.SceneView
                && !IllusionRuntimeRenderingConfig.Get().ForcePathTracingReference;
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
            var perObjectSelector = _rendererData.AdditionalDirectionalPerObjectShadows
                && camera.TryGetComponent(out PerObjectShadowLightSource selector) && selector.isActiveAndEnabled ? selector.Source : null;
            var world = _scene.Update(camera, settings.layerMask.value, settings.directionalAngularDiameter.value, perObjectSelector,
                _rendererData.PerObjectShadowRenderingLayer, realtime);
            _scene.SkinnedUpdates.Keep(world.Scene.Instances);
            var registry = PathTracingInlineRegistry.Instance;
            _inlineTracing = settings.inlineRayTracing.value;
            _inlineSer = settings.shaderExecutionReordering.value;
            if (_inlineTracing)
            {
                try
                {
                    if (!SystemInfo.supportsInlineRayTracing) throw new NotSupportedException("The device does not support inline ray tracing.");
                    if (!PathTracingInlineResources.IsAvailable) throw new InvalidOperationException("The UnityRHI inline resource table is unavailable.");
                    if (!registry) throw new InvalidOperationException("Inline programs have not been generated.");
                    if (realtime)
                    {
                        _inlineBuild.SetProgram(registry.Build);
                        _inlineFill.SetProgram(registry.Fill);
                        _inlineBuild.Prepare(_inlineSer);
                        _inlineFill.Prepare(_inlineSer);
                    }
                    else
                    {
                        _inlineReference.SetProgram(registry.Reference);
                        _inlineReference.Prepare(_inlineSer);
                    }
                    world.UpdateInline();
                    if (!world.Inline.IsReady) throw new InvalidOperationException("Inline scene records are unavailable.");
                    _inlineFailure = null;
                }
                catch (Exception e)
                {
                    if (_inlineFailure != e.ToString()) Debug.LogError(_inlineFailure = e.ToString());
                    using var builder = renderGraph.AddRasterRenderPass<OutputPassData>("Path Tracing Inline Failed", out _);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (OutputPassData data, RasterGraphContext ctx) =>
                        ctx.cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 1, 0));
                    return;
                }
            }


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
        }

        private bool _inlineTracing;
        private bool _inlineSer;
        private string _inlineFailure;
        private readonly PathTracingInlineDispatcher _inlineReference = new();
        private readonly PathTracingInlineDispatcher _inlineBuild = new();
        private readonly PathTracingInlineDispatcher _inlineFill = new();

        public void Dispose()
        {
            foreach (var context in _contexts.Values)
                context.Dispose();
            _contexts.Clear();
            DisposeRealtime();
            _inlineReference.Dispose();
            _inlineBuild.Dispose();
            _inlineFill.Dispose();
            CoreUtils.Destroy(_outputMaterial);
        }
    }
}
