using System;
using System.Collections.Generic;
using Illusion.Rendering.PathTracing;
using Illusion.Rendering.Shadows;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Ray traced global illumination for raster cameras: initial ReSTIR GI samples traced from the opaque raster surface
    /// through the shared path tracing scene, final shading, and publication as the indirect diffuse texture.
    /// </summary>
    internal sealed class RayTracedGlobalIlluminationPass : ScriptableRenderPass, IDisposable
    {
        private const int ContextReleaseFrames = 60;

        private const string InitialSamplesRayGenName = "RayGen_InitialSamples";

        private const string TemporalResamplingRayGenName = "RayGen_TemporalResampling";

        private const string SpatialResamplingRayGenName = "RayGen_SpatialResampling";

        private const string FinalShadingRayGenName = "RayGen_FinalShading";

        private const string CacheUpdateRayGenName = "RayGen_RadianceCacheUpdate";

        private const int InlineThreadGroupSize = 8;

        private const int CacheUpdateBlockSize = 5;

        private const int CacheThreadGroupSize = 256;

        private const int NeighborOffsetCount = 8192;

        private const float CutDistance = 2.0f;

        private const float RayBias = 0.001f;

        private const float EnvironmentDistance = 10000.0f;

        private readonly IllusionRendererData _rendererData;

        private readonly PathTracingSharedScene _scene;

        private readonly RayTracingShader _initialSamplesShader;

        private readonly RayTracingShader _resamplingShader;

        private readonly RayTracingShader _initialSamplesQualityShader;

        private readonly RayTracingShader _cacheUpdateShader;

        private readonly ComputeShader _cacheShader;

        private readonly int _cacheResolveKernel;

        private readonly int _cacheClearKernel;

        private readonly int _cacheDebugKernel;

        private readonly bool _cacheSupported;

        private bool _reportedCache;

        private readonly ComputeShader _compositeShader;

        private readonly int _compositeKernel;

        private readonly int _storeHistoryKernel;

        private readonly int _compositeSpecularKernel;

        private readonly int _mergeReflectionKernel;

        private readonly GraphicsBuffer _neighborOffsets;

        private readonly int _prepareGuidesKernel;

        private readonly IRayTracedGIDenoiser _denoiser;

        private readonly ComputeShader _upsampleShader;

        private readonly int _upsampleKernel;

        private ShaderVariablesBilateralUpsample _upsampleVariables;

        private bool _reportedDenoiser;

        private ComputeShader _inlineInitialSamples;

        private ComputeShader _inlineResampling;

        private int _inlineInitialTraceKernel;

        private int _inlineInitialShadeKernel;

        private GraphicsBuffer _inlineHits;

        private int _inlineTemporalKernel;

        private int _inlineSpatialKernel;

        private int _inlineFinalShadingKernel;

        private bool _reportedInline;

        private readonly GraphicsBuffer _constants;

        private readonly GraphicsBuffer _sampleConstants;

        private readonly GraphicsBuffer _miniConstants;

        private readonly RayTracedGIConstants[] _constantsData = new RayTracedGIConstants[1];

        private readonly PathTracingSampleConstants[] _sampleConstantsData = new PathTracingSampleConstants[1];

        private readonly Dictionary<int, RayTracedGlobalIlluminationContext> _contexts = new();

        private readonly List<int> _expired = new();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private readonly PathTracingInlineValidation _inlineValidation;
#endif

        private readonly ProfilingSampler _traceSampler = new("Ray Traced GI Trace");

        private readonly ProfilingSampler _compositeSampler = new("Ray Traced GI Composite");

        private readonly ProfilingSampler _guidesSampler = new("Ray Traced GI Denoiser Guides");

        private readonly ProfilingSampler _upsampleSampler = new("Ray Traced GI Upsample");

        private readonly ProfilingSampler _reflectionSampler = new("Ray Traced GI Reflection");

        private readonly ProfilingSampler _cacheDebugSampler = new("Ray Traced GI Radiance Cache Debug");

        private class ReflectionPassData
        {
            internal ComputeShader Shader;
            internal int Kernel;
            internal TextureHandle ScreenSpaceReflection;
            internal TextureHandle Specular;
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal TextureHandle Stencil;
            internal TextureHandle PostDepth;
            internal bool ComparePostDepth;
            internal TextureHandle Output;
            internal RayTracedGIView View;
            internal int Width;
            internal int Height;
        }

        private class UpsamplePassData
        {
            internal ComputeShader Shader;
            internal int Kernel;
            internal ShaderVariablesBilateralUpsample Variables;
            internal TextureHandle Input;
            internal TextureHandle Depth;
            internal TextureHandle Output;
            internal Vector4 HalfScreenSize;
            internal int Width;
            internal int Height;
        }

        private class TracePassData
        {
            internal PathTracingSharedScene Scene;
            internal PathTracingWorld World;
            internal PathTracingLightBaker LightBaker;
            internal IllusionRenderPipelineResources PipelineResources;
            internal PathTracingDiffusionProfiles Profiles;
            internal RayTracingShader InitialSamplesShader;
            internal RayTracingShader ResamplingShader;
            internal bool Inline;
            internal ComputeShader InlineInitialSamples;
            internal ComputeShader InlineResampling;
            internal int InlineInitialTraceKernel;
            internal int InlineInitialShadeKernel;
            internal GraphicsBuffer InlineHits;
            internal int InlineTemporalKernel;
            internal int InlineSpatialKernel;
            internal int InlineFinalShadingKernel;
            internal GraphicsBuffer NeighborOffsets;
            internal bool Temporal;
            internal bool Spatial;
            internal GraphicsBuffer Constants;
            internal GraphicsBuffer SampleConstants;
            internal GraphicsBuffer MiniConstants;
            internal RayTracedGIConstants ConstantsData;
            internal PathTracingSampleConstants SampleConstantsData;
            internal RayTracedGIConstants[] ConstantsUpload;
            internal PathTracingSampleConstants[] SampleConstantsUpload;
            internal Camera Camera;
            internal Matrix4x4 ViewMatrix;
            internal Matrix4x4 ProjectionMatrix;
            internal Matrix4x4 WorldToClip;
            internal float SubsurfaceRayBias;
            internal int Frame;
            internal int Width;
            internal int Height;
            internal int DispatchWidth;
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal TextureHandle ForwardGBuffer;
            internal TextureHandle Exposure;
            internal TextureHandle DiffuseLighting;
            internal TextureHandle SpecularLighting;
            internal TextureHandle PreviousDepth;
            internal TextureHandle PreviousNormalRoughness;
            internal TextureHandle Motion;
            internal BufferHandle Reservoirs;
            internal BufferHandle SecondarySurfaces;
            internal bool RadianceCache;
            internal bool CacheClear;
            internal RayTracingShader CacheUpdateShader;
            internal ComputeShader CacheShader;
            internal int CacheResolveKernel;
            internal int CacheClearKernel;
            internal BufferHandle CacheHashEntries;
            internal BufferHandle CacheAccumulation;
            internal BufferHandle CacheResolved;
            internal Vector4 CacheCameraPosition;
            internal Vector4 CacheCameraPositionPrev;
            internal uint CacheFrameIndex;
        }

        private class GuidesPassData
        {
            internal ComputeShader Shader;
            internal int Kernel;
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal TextureHandle ForwardGBuffer;
            internal TextureHandle Motion;
            internal TextureHandle ViewZ;
            internal TextureHandle NormalRoughness;
            internal TextureHandle MotionVectors;
            internal RayTracedGIView View;
            internal float DenoisingRange;
            internal int Width;
            internal int Height;
            internal Vector4 TextureSizeAndScale;
        }

        private class CacheDebugPassData
        {
            internal ComputeShader Shader;
            internal int Kernel;
            internal int View;
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal TextureHandle Output;
            internal BufferHandle HashEntries;
            internal BufferHandle Accumulation;
            internal BufferHandle Resolved;
            internal Vector4 CacheCameraPosition;
            internal Matrix4x4 TexelToWorld;
            internal Vector3 CameraPosition;
            internal float FarDepth;
            internal int Width;
            internal int Height;
        }

        private class CompositePassData
        {
            internal ComputeShader Shader;
            internal int Kernel;
            internal int StoreHistoryKernel;
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal TextureHandle ForwardGBuffer;
            internal TextureHandle PreviousDepth;
            internal TextureHandle PreviousNormalRoughness;
            internal RayTracedGIView View;
            internal Vector4 TextureSizeAndScale;
            internal uint DenoiserMode;
            internal TextureHandle Diffuse;
            internal TextureHandle Accumulation;
            internal TextureHandle Exposure;
            internal TextureHandle Output;
            internal int Width;
            internal int Height;
            internal float AccumulationWeight;
            internal bool Specular;
            internal int SpecularKernel;
            internal TextureHandle SpecularInput;
            internal TextureHandle SpecularAccumulation;
            internal TextureHandle SpecularOutput;
        }

        public RayTracedGlobalIlluminationPass(IllusionRenderPipelineResources resources, IllusionRendererData rendererData,
            PathTracingSharedScene scene)
        {
            profilingSampler = new ProfilingSampler("Ray Traced Global Illumination");
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceGlobalIlluminationPass;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Motion);
            _rendererData = rendererData;
            _scene = scene;
            _initialSamplesShader = resources.rayTracedGIInitialSamplesRS;
            _resamplingShader = resources.rayTracedGIResamplingRS;
            _initialSamplesQualityShader = resources.rayTracedGIInitialSamplesQualityRS;
            _cacheUpdateShader = resources.rayTracedGIRadianceCacheUpdateRS;
            _cacheShader = resources.rayTracedGIRadianceCacheCS;
            if (_cacheShader)
            {
                _cacheResolveKernel = _cacheShader.FindKernel("Resolve");
                _cacheClearKernel = _cacheShader.FindKernel("Clear");
                _cacheDebugKernel = _cacheShader.FindKernel("Debug");
            }
            // The cache shaders require native 16-bit types and 64-bit buffer atomics.
            _cacheSupported = _initialSamplesQualityShader && _cacheUpdateShader && _cacheShader && _cacheShader.IsSupported(_cacheResolveKernel);
            _compositeShader = resources.rayTracedGICompositeCS;
            _compositeKernel = _compositeShader.FindKernel("Composite");
            _storeHistoryKernel = _compositeShader.FindKernel("StoreHistory");
            _prepareGuidesKernel = _compositeShader.FindKernel("PrepareGuides");
            _compositeSpecularKernel = _compositeShader.FindKernel("CompositeSpecular");
            _mergeReflectionKernel = _compositeShader.FindKernel("MergeReflection");
            _denoiser = RayTracedGIDenoiserLoader.Create();
            _upsampleShader = resources.bilateralUpsampleCS;
            _upsampleKernel = _upsampleShader.FindKernel("BilateralUpSampleColor");
            _neighborOffsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, NeighborOffsetCount, sizeof(float) * 2) { name = "RayTracedGINeighborOffsets" };
            _neighborOffsets.SetData(BuildNeighborOffsets());
            _constants = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, RayTracedGIConstants.Stride) { name = "RayTracedGIConstants" };
            _sampleConstants = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, PathTracingSampleConstants.Stride) { name = "RayTracedGISampleConstants" };
            _miniConstants = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, PathTracingSampleMiniConstants.Stride) { name = "RayTracedGIMiniConstants" };
            _miniConstants.SetData(new PathTracingSampleMiniConstants[1]);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _inlineValidation = new PathTracingInlineValidation(resources.pathTracingInlineValidationRS);
#endif
        }

        public bool IsDenoiserLoaded => _denoiser is { IsAvailable: true };

        public bool IsDenoiserAvailable(RayTracedGIDenoiser denoiser)
        {
            if (denoiser == RayTracedGIDenoiser.None || IsDenoiserLoaded)
                return true;
            if (!_reportedDenoiser)
            {
                _reportedDenoiser = true;
                Debug.LogWarning("[RayTracedGI] The NRD denoiser needs the UnityRHI package and Direct3D 12; ray traced global illumination is off.");
            }
            return false;
        }

        private bool IsQualityModeAvailable()
        {
            if (_cacheSupported)
                return true;
            if (!_reportedCache)
            {
                _reportedCache = true;
                Debug.LogWarning("[RayTracedGI] Quality mode needs native 16-bit shader types and 64-bit buffer atomics; ray traced global illumination runs in Performance mode.");
            }
            return false;
        }

        internal PathTracingWorld UpdateScene(Camera camera)
        {
            var settings = VolumeManager.instance.stack.GetComponent<PathTracing.PathTracing>();
            var perObjectSelector = _rendererData.AdditionalDirectionalPerObjectShadows
                && camera.TryGetComponent(out PerObjectShadowLightSource selector) && selector.isActiveAndEnabled ? selector.Source : null;
            return _scene.Update(camera, settings.layerMask.value, settings.directionalAngularDiameter.value, perObjectSelector,
                _rendererData.PerObjectShadowRenderingLayer, false);
        }

        private bool UseInlineRayTracing(bool radianceCache, PathTracingWorld world)
        {
            if (radianceCache)
                return false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (IllusionRuntimeRenderingConfig.Get().RayTracedGlobalIlluminationHitShaders)
                return false;
#endif
            string reason = null;
            var registry = PathTracingInlineRegistry.Instance;
            if (!SystemInfo.supportsInlineRayTracing)
                reason = "the device does not support ray queries";
            else if (!PathTracingInlineResources.IsAvailable)
                reason = "the UnityRHI scene resource table is unavailable";
            else if (!registry || !registry.RayTracedGIInitialSamples || !registry.RayTracedGIResampling)
                reason = "the inline kernels have not been generated";
            else if (!FindInlineKernels(registry))
                reason = "the inline kernels are not supported";
            else
            {
                world.UpdateInline();
                if (!world.Inline.IsReady)
                    reason = "the inline scene records are unavailable";
            }
            if (reason == null)
                return true;
            if (!_reportedInline)
            {
                _reportedInline = true;
                Debug.LogWarning($"[RayTracedGI] Performance mode evaluates hits with hit shaders because {reason}.");
            }
            return false;
        }

        private bool FindInlineKernels(PathTracingInlineRegistry registry)
        {
            if (_inlineInitialSamples != registry.RayTracedGIInitialSamples)
            {
                _inlineInitialSamples = registry.RayTracedGIInitialSamples;
                _inlineInitialTraceKernel = _inlineInitialSamples.FindKernel("RayTracedGIInitialTrace");
                _inlineInitialShadeKernel = _inlineInitialSamples.FindKernel("RayTracedGIInitialShade");
            }
            if (_inlineResampling != registry.RayTracedGIResampling)
            {
                _inlineResampling = registry.RayTracedGIResampling;
                _inlineTemporalKernel = _inlineResampling.FindKernel("RayTracedGITemporalResampling");
                _inlineSpatialKernel = _inlineResampling.FindKernel("RayTracedGISpatialResampling");
                _inlineFinalShadingKernel = _inlineResampling.FindKernel("RayTracedGIFinalShading");
            }
            return _inlineInitialSamples.IsSupported(_inlineInitialTraceKernel) && _inlineInitialSamples.IsSupported(_inlineInitialShadeKernel)
                && _inlineResampling.IsSupported(_inlineTemporalKernel);
        }

        public static bool HasResources(IllusionRenderPipelineResources resources)
        {
            return resources.rayTracedGIInitialSamplesRS && resources.rayTracedGIResamplingRS && resources.rayTracedGICompositeCS;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var camera = cameraData.camera;
            var wetData = frameData.GetOrCreate<WetSurfaceFrameData>();
            var forwardGBuffer = wetData.ModifiedForwardBuffer.IsValid()
                ? wetData.ModifiedForwardBuffer
                : wetData.ForwardBuffer.IsValid()
                    ? wetData.ForwardBuffer
                    : _rendererData.ForwardGBufferRT != null
                        ? renderGraph.ImportTexture(_rendererData.ForwardGBufferRT)
                        : TextureHandle.nullHandle;
            if (_rendererData.DepthPyramidRT == null || !resourceData.cameraNormalsTexture.IsValid() || !forwardGBuffer.IsValid())
                return;

            var settings = VolumeManager.instance.stack.GetComponent<PathTracing.PathTracing>();
            var volume = VolumeManager.instance.stack.GetComponent<RayTracedGlobalIllumination>();
            int textureWidth = cameraData.cameraTargetDescriptor.width;
            int textureHeight = cameraData.cameraTargetDescriptor.height;
            bool halfResolution = volume.halfResolution.value;
            bool specular = volume.specular.value;
            int scale = halfResolution ? 2 : 1;
            int width = (textureWidth + scale - 1) / scale;
            int height = (textureHeight + scale - 1) / scale;
            var denoiserType = volume.denoiser.value;
            bool denoise = denoiserType != RayTracedGIDenoiser.None;
            bool checkerboard = denoise && volume.checkerboard.value;
            int dispatchWidth = checkerboard ? (width + 1) / 2 : width;
            var context = GetContext(camera);
            // The denoiser wraps the targets, so it lets them go before they are reallocated or released.
            if (context.TargetsChange(width, height, checkerboard, denoise, specular))
                _denoiser?.Release(camera.GetInstanceID());
            context.EnsureTargets(width, height, checkerboard);
            bool radianceCache = volume.mode.value == RayTracedGIMode.Quality && IsQualityModeAvailable();
            context.EnsureRadianceCache(radianceCache);

            var world = UpdateScene(camera);
            bool inlineTracing = UseInlineRayTracing(radianceCache, world);

            var worldToView = cameraData.GetViewMatrix();
            var projection = cameraData.GetProjectionMatrix();
            var viewToClip = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true);
            var cameraConstants = PathTracingFrameConstants.BuildCamera(camera, textureWidth, textureHeight, default, height);

            var mode = volume.resamplingMode.value;
            bool temporal = mode is RayTracedGIResamplingMode.Temporal or RayTracedGIResamplingMode.TemporalAndSpatial;
            bool spatial = mode is RayTracedGIResamplingMode.Spatial or RayTracedGIResamplingMode.TemporalAndSpatial;
            context.EnsureDenoiserTargets(denoise, specular);
            bool historyValid = context.BeginFrame(camera.transform, HashCode.Combine(mode, settings.layerMask.value, denoiserType, halfResolution, checkerboard, volume.radianceClamp.value, specular, radianceCache),
                _rendererData.ScaleWorldDistance(CutDistance));
            uint frameIndex = context.FrameIndex;
            var constants = BuildConstants(camera, worldToView, projection, volume, context, historyValid, width, height,
                new Vector4(textureWidth, textureHeight, scale, 0.0f));
            // As in the RTXDI sample's black checkerboard mode, the active field alternates every frame.
            constants.RuntimeParams.ActiveCheckerboardField = !checkerboard ? 0u : (frameIndex & 1u) != 0 ? 1u : 2u;
            uint denoiserMode = !denoise ? 0u : denoiserType == RayTracedGIDenoiser.Relax ? 2u : 1u;
            constants.DenoiserMode = denoiserMode;
            bool screenMotion = resourceData.motionVectorColor.IsValid();
            constants.MotionVectors = screenMotion ? 1u : 0u;
            constants.Specular = specular ? 1u : 0u;
            constants.RadianceClamp = volume.radianceClamp.value;
            var motion = screenMotion ? resourceData.motionVectorColor : renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());
            constants.ReblurHitDistParams = new Vector4(3.0f, 0.1f, 20.0f, 0.0f);

            var exposure = renderGraph.ImportTexture(_rendererData.GetExposureTexture());
            var diffuseLighting = renderGraph.ImportTexture(context.DiffuseLighting);

            using (var builder = renderGraph.AddUnsafePass<TracePassData>("Ray Traced GI Trace", out var passData, _traceSampler))
            {
                passData.Scene = _scene;
                passData.World = world;
                passData.LightBaker = context.GetLightBaker(_scene.LightBakerKernels);
                passData.PipelineResources = _rendererData.RuntimeResources;
                passData.Profiles = PathTracingDiffusionProfiles.Capture(_rendererData);
                passData.InitialSamplesShader = radianceCache ? _initialSamplesQualityShader : _initialSamplesShader;
                passData.ResamplingShader = _resamplingShader;
                passData.Inline = inlineTracing;
                passData.InlineInitialSamples = _inlineInitialSamples;
                passData.InlineResampling = _inlineResampling;
                passData.InlineInitialTraceKernel = _inlineInitialTraceKernel;
                passData.InlineInitialShadeKernel = _inlineInitialShadeKernel;
                passData.InlineHits = inlineTracing ? EnsureInlineHits(context.SecondarySurfaces.count) : null;
                passData.InlineTemporalKernel = _inlineTemporalKernel;
                passData.InlineSpatialKernel = _inlineSpatialKernel;
                passData.InlineFinalShadingKernel = _inlineFinalShadingKernel;
                passData.NeighborOffsets = _neighborOffsets;
                passData.Temporal = temporal;
                passData.Spatial = spatial;
                passData.Constants = _constants;
                passData.SampleConstants = _sampleConstants;
                passData.MiniConstants = _miniConstants;
                passData.ConstantsData = constants;
                passData.SampleConstantsData = BuildSampleConstants(settings, cameraConstants, worldToView, viewToClip, context, width, height);
                passData.ConstantsUpload = _constantsData;
                passData.SampleConstantsUpload = _sampleConstantsData;
                passData.Camera = camera;
                passData.ViewMatrix = worldToView;
                passData.ProjectionMatrix = projection;
                passData.WorldToClip = viewToClip * worldToView;
                passData.SubsurfaceRayBias = _rendererData.ScaleWorldDistance(0.001f);
                passData.Frame = PathTracingFrame.Index;
                passData.Width = width;
                passData.Height = height;
                passData.DispatchWidth = dispatchWidth;
                passData.Depth = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
                passData.Normals = resourceData.cameraNormalsTexture;
                passData.ForwardGBuffer = forwardGBuffer;
                passData.Exposure = exposure;
                passData.DiffuseLighting = diffuseLighting;
                passData.SpecularLighting = renderGraph.ImportTexture(context.SpecularLighting);
                passData.PreviousDepth = renderGraph.ImportTexture(context.PreviousDepth);
                passData.PreviousNormalRoughness = renderGraph.ImportTexture(context.PreviousNormalRoughness);
                passData.Motion = motion;
                passData.Reservoirs = renderGraph.ImportBuffer(context.Reservoirs);
                passData.SecondarySurfaces = renderGraph.ImportBuffer(context.SecondarySurfaces);

                builder.UseTexture(passData.Depth);
                builder.UseTexture(passData.Normals);
                builder.UseTexture(passData.ForwardGBuffer);
                builder.UseTexture(passData.Exposure);
                builder.UseTexture(passData.DiffuseLighting, AccessFlags.Write);
                builder.UseTexture(passData.SpecularLighting, AccessFlags.Write);
                builder.UseTexture(passData.PreviousDepth);
                builder.UseTexture(passData.PreviousNormalRoughness);
                builder.UseTexture(passData.Motion);
                builder.UseBuffer(passData.Reservoirs, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.SecondarySurfaces, AccessFlags.ReadWrite);
                passData.RadianceCache = radianceCache;
                if (radianceCache)
                {
                    passData.CacheClear = context.CacheNeedsClear;
                    context.CacheNeedsClear = false;
                    passData.CacheUpdateShader = _cacheUpdateShader;
                    passData.CacheShader = _cacheShader;
                    passData.CacheResolveKernel = _cacheResolveKernel;
                    passData.CacheClearKernel = _cacheClearKernel;
                    passData.CacheHashEntries = renderGraph.ImportBuffer(context.CacheHashEntries);
                    passData.CacheAccumulation = renderGraph.ImportBuffer(context.CacheAccumulation);
                    passData.CacheResolved = renderGraph.ImportBuffer(context.CacheResolved);
                    passData.CacheCameraPosition = context.CacheCameraPosition;
                    passData.CacheCameraPositionPrev = context.CacheCameraPositionPrev;
                    passData.CacheFrameIndex = frameIndex;
                    builder.UseBuffer(passData.CacheHashEntries, AccessFlags.ReadWrite);
                    builder.UseBuffer(passData.CacheAccumulation, AccessFlags.ReadWrite);
                    builder.UseBuffer(passData.CacheResolved, AccessFlags.ReadWrite);
                }
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (TracePassData data, UnsafeGraphContext graphContext) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(graphContext.cmd);
                    data.Scene.Prepare(cmd, data.World, data.LightBaker, data.Exposure, data.Camera.transform.position,
                        data.WorldToClip, data.Width, data.Height, data.Frame, false);
                    cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjectionMatrix);

                    data.ConstantsUpload[0] = data.ConstantsData;
                    data.SampleConstantsUpload[0] = data.SampleConstantsData;
                    cmd.SetBufferData(data.Constants, data.ConstantsUpload);
                    cmd.SetBufferData(data.SampleConstants, data.SampleConstantsUpload);
                    if (data.RadianceCache)
                        UpdateRadianceCache(cmd, data);
                    if (data.Inline)
                    {
                        TraceInline(cmd, data);
                        return;
                    }

                    var initial = data.InitialSamplesShader;
                    data.Scene.BindTraceInputs(cmd, initial, data.World, data.LightBaker, data.PipelineResources, data.Profiles,
                        data.SubsurfaceRayBias, data.Exposure, PathTracingEnvironment.CameraBackground(data.Camera),
                        data.SampleConstants, data.MiniConstants);
                    data.Scene.BindOutputPlaceholders(cmd, initial);
                    BindReceiver(cmd, initial, data);
                    cmd.SetRayTracingBufferParam(initial, ShaderIDs.u_SecondaryGBuffer, data.SecondarySurfaces);
                    cmd.SetRayTracingBufferParam(initial, ShaderIDs.u_GIReservoirs, data.Reservoirs);
                    if (data.RadianceCache)
                        BindCache(cmd, initial, data);
                    using (new ProfilingScope(cmd, Samplers.InitialSamples))
                        cmd.DispatchRays(initial, InitialSamplesRayGenName, (uint)data.DispatchWidth, (uint)data.Height, 1, data.Camera);

                    // The resampling passes share one shader, so every resource of the final shading pass is bound before the first.
                    var resampling = data.ResamplingShader;
                    BindResampling(cmd, resampling, data);
                    cmd.SetRayTracingTextureParam(resampling, ShaderIDs.u_DiffuseLighting, data.DiffuseLighting);
                    cmd.SetRayTracingTextureParam(resampling, ShaderIDs.u_SpecularLighting, data.SpecularLighting);
                    cmd.SetRayTracingTextureParam(resampling, ShaderIDs.t_RayTracedGIPreIntegratedFGD, data.Scene.Resources.GetFGD(cmd));
                    if (data.Temporal)
                    {
                        using (new ProfilingScope(cmd, Samplers.TemporalResampling))
                            cmd.DispatchRays(resampling, TemporalResamplingRayGenName, (uint)data.DispatchWidth, (uint)data.Height, 1, data.Camera);
                    }
                    if (data.Spatial)
                    {
                        using (new ProfilingScope(cmd, Samplers.SpatialResampling))
                            cmd.DispatchRays(resampling, SpatialResamplingRayGenName, (uint)data.DispatchWidth, (uint)data.Height, 1, data.Camera);
                    }
                    using (new ProfilingScope(cmd, Samplers.FinalShading))
                        cmd.DispatchRays(resampling, FinalShadingRayGenName, (uint)data.DispatchWidth, (uint)data.Height, 1, data.Camera);
                });
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var inlineView = IllusionRuntimeRenderingConfig.Get().PathTracingInlineValidationView;
            if (inlineView != PathTracingInlineValidationView.None)
            {
                var inlineOutput = _inlineValidation.Record(renderGraph, camera, world, textureWidth, textureHeight, inlineView);
                if (inlineOutput.IsValid())
                    _rendererData.RayTracedGIDebugTexture = inlineOutput;
            }

            var debugView = IllusionRuntimeRenderingConfig.Get().RayTracedGlobalIlluminationDebugView;
            if (radianceCache && debugView != RayTracedGIDebugView.None)
                _rendererData.RayTracedGIDebugTexture = RecordCacheDebug(renderGraph, context, constants.View, resourceData.cameraNormalsTexture,
                    debugView, textureWidth, textureHeight);
#endif

            var compositeInput = diffuseLighting;
            var compositeSpecularInput = renderGraph.ImportTexture(context.SpecularLighting);
            if (denoise)
            {
                float denoisingRange = camera.farClipPlane;
                using (var builder = renderGraph.AddComputePass<GuidesPassData>("Ray Traced GI Denoiser Guides", out var passData, _guidesSampler))
                {
                    passData.Shader = _compositeShader;
                    passData.Kernel = _prepareGuidesKernel;
                    passData.Depth = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
                    passData.Normals = resourceData.cameraNormalsTexture;
                    passData.ForwardGBuffer = forwardGBuffer;
                    passData.Motion = motion;
                    passData.ViewZ = renderGraph.ImportTexture(context.ViewZ);
                    passData.NormalRoughness = renderGraph.ImportTexture(context.NormalRoughness);
                    passData.MotionVectors = renderGraph.ImportTexture(context.MotionVectors);
                    passData.View = constants.View;
                    passData.DenoisingRange = denoisingRange;
                    passData.Width = width;
                    passData.Height = height;
                    passData.TextureSizeAndScale = constants.View.TextureSizeAndScale;
                    builder.UseTexture(passData.Depth);
                    builder.UseTexture(passData.Normals);
                    builder.UseTexture(passData.ForwardGBuffer);
                    builder.UseTexture(passData.Motion);
                    builder.UseTexture(passData.ViewZ, AccessFlags.WriteAll);
                    builder.UseTexture(passData.NormalRoughness, AccessFlags.WriteAll);
                    builder.UseTexture(passData.MotionVectors, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (GuidesPassData data, ComputeGraphContext graphContext) =>
                    {
                        var cmd = graphContext.cmd;
                        int kernel = data.Kernel;
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIDepth, data.Depth);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGINormals, data.Normals);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIForwardGBuffer, data.ForwardGBuffer);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIMotion, data.Motion);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIViewZ, data.ViewZ);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGINormalRoughness, data.NormalRoughness);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIMotionVectors, data.MotionVectors);
                        SetViewDepth(cmd, data.Shader, data.View);
                        cmd.SetComputeFloatParam(data.Shader, ShaderIDs._RayTracedGIDenoisingRange, data.DenoisingRange);
                        cmd.SetComputeIntParams(data.Shader, ShaderIDs._RayTracedGISize, data.Width, data.Height);
                        SetTextureScale(cmd, data.Shader, data.TextureSizeAndScale);
                        cmd.DispatchCompute(data.Shader, kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
                    });
                }

                _denoiser.Record(renderGraph, camera, new RayTracedGIDenoiserInputs
                {
                    ViewZ = context.ViewZ,
                    NormalRoughness = context.NormalRoughness,
                    MotionVectors = context.MotionVectors,
                    ScreenMotion = screenMotion,
                    Diffuse = context.DiffuseLighting,
                    Output = context.DenoisedDiffuse,
                    Specular = specular ? context.SpecularLighting : null,
                    SpecularOutput = context.DenoisedSpecular,
                    WorldToView = camera.worldToCameraMatrix,
                    ViewToClip = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, SystemInfo.graphicsUVStartsAtTop),
                    Jitter = TexelJitter(projection, camera.nonJitteredProjectionMatrix, new Vector2(textureWidth, textureHeight)) / scale,
                    Width = width,
                    Height = height,
                    DenoisingRange = denoisingRange,
                    Denoiser = denoiserType,
                    Checkerboard = checkerboard,
                    FrameIndex = frameIndex
                }, !historyValid);
                compositeInput = renderGraph.ImportTexture(context.DenoisedDiffuse);
                if (specular)
                    compositeSpecularInput = renderGraph.ImportTexture(context.DenoisedSpecular);
            }

            var config = IllusionRuntimeRenderingConfig.Get();
            int stateHash = HashCode.Combine(worldToView, projection, world.Scene.SceneHash, _scene.Lights.Hash,
                PathTracingEnvironment.ComputeHash(_scene.Lights), width, height);
            float accumulationWeight = context.UpdateAccumulation(config.EnableRayTracedGlobalIlluminationAccumulation, specular,
                stateHash, world.Scene.TransformsChanged);

            var output = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                name = halfResolution ? "_RayTracedGIHalfResolution" : "_IndirectDiffuseTexture"
            });
            var specularOutput = specular
                ? renderGraph.CreateTexture(new TextureDesc(width, height)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "_RayTracedGISpecular"
                })
                : TextureHandle.nullHandle;
            using (var builder = renderGraph.AddComputePass<CompositePassData>("Ray Traced GI Composite", out var passData, _compositeSampler))
            {
                passData.Shader = _compositeShader;
                passData.Kernel = _compositeKernel;
                passData.StoreHistoryKernel = _storeHistoryKernel;
                passData.Depth = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
                passData.Normals = resourceData.cameraNormalsTexture;
                passData.ForwardGBuffer = forwardGBuffer;
                passData.PreviousDepth = renderGraph.ImportTexture(context.PreviousDepth);
                passData.PreviousNormalRoughness = renderGraph.ImportTexture(context.PreviousNormalRoughness);
                passData.View = constants.View;
                passData.TextureSizeAndScale = constants.View.TextureSizeAndScale;
                passData.Diffuse = compositeInput;
                passData.DenoiserMode = denoiserMode;
                passData.Accumulation = context.Accumulation != null ? renderGraph.ImportTexture(context.Accumulation) : output;
                passData.Exposure = exposure;
                passData.Output = output;
                passData.Width = width;
                passData.Height = height;
                passData.AccumulationWeight = accumulationWeight;
                passData.Specular = specular;
                if (specular)
                {
                    passData.SpecularKernel = _compositeSpecularKernel;
                    passData.SpecularInput = compositeSpecularInput;
                    passData.SpecularOutput = specularOutput;
                    passData.SpecularAccumulation = context.SpecularAccumulation != null
                        ? renderGraph.ImportTexture(context.SpecularAccumulation)
                        : specularOutput;
                    builder.UseTexture(passData.SpecularInput);
                    builder.UseTexture(passData.SpecularOutput, AccessFlags.Write);
                    if (context.SpecularAccumulation != null)
                        builder.UseTexture(passData.SpecularAccumulation, AccessFlags.ReadWrite);
                }
                builder.UseTexture(passData.Diffuse);
                if (context.Accumulation != null)
                    builder.UseTexture(passData.Accumulation, AccessFlags.ReadWrite);
                builder.UseTexture(passData.Exposure);
                builder.UseTexture(passData.Output, AccessFlags.Write);
                builder.UseTexture(passData.Depth);
                builder.UseTexture(passData.Normals);
                builder.UseTexture(passData.ForwardGBuffer);
                builder.UseTexture(passData.PreviousDepth, AccessFlags.Write);
                builder.UseTexture(passData.PreviousNormalRoughness, AccessFlags.Write);
                if (!halfResolution)
                    builder.SetGlobalTextureAfterPass(output, ShaderIDs._IndirectDiffuseTexture);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (CompositePassData data, ComputeGraphContext graphContext) =>
                {
                    var cmd = graphContext.cmd;
                    cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._RayTracedGIDiffuse, data.Diffuse);
                    cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._RayTracedGIAccumulation, data.Accumulation);
                    cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._RayTracedGIOutput, data.Output);
                    cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._ExposureTexture, data.Exposure);
                    cmd.SetComputeIntParams(data.Shader, ShaderIDs._RayTracedGISize, data.Width, data.Height);
                    cmd.SetComputeFloatParam(data.Shader, ShaderIDs._RayTracedGIAccumulationWeight, data.AccumulationWeight);
                    cmd.SetComputeIntParam(data.Shader, ShaderIDs._RayTracedGIDenoiserMode, (int)data.DenoiserMode);
                    cmd.DispatchCompute(data.Shader, data.Kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);

                    if (data.Specular)
                    {
                        int kernel = data.SpecularKernel;
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGISpecular, data.SpecularInput);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGISpecularAccumulation, data.SpecularAccumulation);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGISpecularOutput, data.SpecularOutput);
                        cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._ExposureTexture, data.Exposure);
                        cmd.DispatchCompute(data.Shader, kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
                    }

                    int store = data.StoreHistoryKernel;
                    cmd.SetComputeTextureParam(data.Shader, store, ShaderIDs._RayTracedGIDepth, data.Depth);
                    cmd.SetComputeTextureParam(data.Shader, store, ShaderIDs._RayTracedGINormals, data.Normals);
                    cmd.SetComputeTextureParam(data.Shader, store, ShaderIDs._RayTracedGIForwardGBuffer, data.ForwardGBuffer);
                    cmd.SetComputeTextureParam(data.Shader, store, ShaderIDs._RayTracedGIPrevDepth, data.PreviousDepth);
                    cmd.SetComputeTextureParam(data.Shader, store, ShaderIDs._RayTracedGIPrevNormalRoughness, data.PreviousNormalRoughness);
                    SetViewDepth(cmd, data.Shader, data.View);
                    SetTextureScale(cmd, data.Shader, data.TextureSizeAndScale);
                    cmd.DispatchCompute(data.Shader, store, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
                });
            }

            if (halfResolution)
            {
                RecordUpsample(renderGraph, output, "_IndirectDiffuseTexture", ShaderIDs._IndirectDiffuseTexture,
                    textureWidth, textureHeight, width, height);
                if (specular)
                    specularOutput = RecordUpsample(renderGraph, specularOutput, "_RayTracedGISpecular", 0,
                        textureWidth, textureHeight, width, height);
            }
            if (specular)
                RecordReflection(renderGraph, frameData, specularOutput, constants.View, textureWidth, textureHeight);

            context.EndFrame(constants.View);
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private TextureHandle RecordCacheDebug(RenderGraph renderGraph, RayTracedGlobalIlluminationContext context, RayTracedGIView view,
            TextureHandle normals, RayTracedGIDebugView debugView, int width, int height)
        {
            var output = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                enableRandomWrite = true,
                name = "_RayTracedGIRadianceCacheDebug"
            });
            using var builder = renderGraph.AddComputePass<CacheDebugPassData>("Ray Traced GI Radiance Cache Debug", out var passData, _cacheDebugSampler);
            passData.Shader = _cacheShader;
            passData.Kernel = _cacheDebugKernel;
            passData.View = (int)debugView;
            passData.Depth = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            passData.Normals = normals;
            passData.Output = output;
            passData.HashEntries = renderGraph.ImportBuffer(context.CacheHashEntries);
            passData.Accumulation = renderGraph.ImportBuffer(context.CacheAccumulation);
            passData.Resolved = renderGraph.ImportBuffer(context.CacheResolved);
            passData.CacheCameraPosition = context.CacheCameraPosition;
            passData.TexelToWorld = view.TexelToWorld;
            passData.CameraPosition = view.CameraPosition;
            passData.FarDepth = view.FarDepth;
            passData.Width = width;
            passData.Height = height;
            builder.UseTexture(passData.Depth);
            builder.UseTexture(passData.Normals);
            builder.UseTexture(passData.Output, AccessFlags.Write);
            builder.UseBuffer(passData.HashEntries);
            builder.UseBuffer(passData.Accumulation);
            builder.UseBuffer(passData.Resolved);
            builder.SetRenderFunc(static (CacheDebugPassData data, ComputeGraphContext graphContext) =>
            {
                var cmd = graphContext.cmd;
                cmd.SetComputeBufferParam(data.Shader, data.Kernel, ShaderIDs.u_RayTracedGICacheHashEntries, data.HashEntries);
                cmd.SetComputeBufferParam(data.Shader, data.Kernel, ShaderIDs.u_RayTracedGICacheAccumulation, data.Accumulation);
                cmd.SetComputeBufferParam(data.Shader, data.Kernel, ShaderIDs.u_RayTracedGICacheResolved, data.Resolved);
                cmd.SetComputeVectorParam(data.Shader, ShaderIDs._RayTracedGICacheCameraPosition, data.CacheCameraPosition);
                cmd.SetComputeIntParam(data.Shader, ShaderIDs._RayTracedGICacheCapacity, RayTracedGlobalIlluminationContext.CacheCapacity);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._RayTracedGIDepth, data.Depth);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._RayTracedGINormals, data.Normals);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._RayTracedGIDebugOutput, data.Output);
                cmd.SetComputeMatrixParam(data.Shader, ShaderIDs._RayTracedGIDebugTexelToWorld, data.TexelToWorld);
                cmd.SetComputeVectorParam(data.Shader, ShaderIDs._RayTracedGIDebugCameraPosition, data.CameraPosition);
                cmd.SetComputeFloatParam(data.Shader, ShaderIDs._RayTracedGIFarDepth, data.FarDepth);
                cmd.SetComputeIntParams(data.Shader, ShaderIDs._RayTracedGIDebugSize, data.Width, data.Height);
                cmd.SetComputeIntParam(data.Shader, ShaderIDs._RayTracedGIDebugView, data.View);
                cmd.DispatchCompute(data.Shader, data.Kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
            });
            return output;
        }
#endif

        private static void SetTextureScale(ComputeCommandBuffer cmd, ComputeShader shader, Vector4 textureSizeAndScale)
        {
            cmd.SetComputeIntParam(shader, ShaderIDs._RayTracedGITextureScale, (int)textureSizeAndScale.z);
            cmd.SetComputeIntParams(shader, ShaderIDs._RayTracedGITextureSize, (int)textureSizeAndScale.x, (int)textureSizeAndScale.y);
        }

        // The view depth of a camera texel is its camera-relative position projected onto the camera forward.
        private static void SetViewDepth(ComputeCommandBuffer cmd, ComputeShader shader, in RayTracedGIView view)
        {
            var texelToWorld = view.TexelToWorld;
            var forward = view.CameraForward;
            cmd.SetComputeVectorParam(shader, ShaderIDs._RayTracedGIViewDepthZ,
                forward.x * texelToWorld.GetRow(0) + forward.y * texelToWorld.GetRow(1) + forward.z * texelToWorld.GetRow(2));
            cmd.SetComputeVectorParam(shader, ShaderIDs._RayTracedGIViewDepthW, texelToWorld.GetRow(3));
            cmd.SetComputeFloatParam(shader, ShaderIDs._RayTracedGIFarDepth, view.FarDepth);
        }

        // The jitter of the camera depth in camera texels, from the jittered and the unjittered perspective or orthographic projection.
        private static Vector2 TexelJitter(Matrix4x4 projection, Matrix4x4 nonJittered, Vector2 textureSize)
        {
            float x = (projection.m02 - nonJittered.m02) * nonJittered.m32 + (projection.m03 - nonJittered.m03) * nonJittered.m33;
            float y = (projection.m12 - nonJittered.m12) * nonJittered.m32 + (projection.m13 - nonJittered.m13) * nonJittered.m33;
            float yFlip = SystemInfo.graphicsUVStartsAtTop ? -1.0f : 1.0f;
            return new Vector2(-x * textureSize.x, yFlip * y * textureSize.y) * 0.5f;
        }

        // RTGI specular fills what SSR leaves to reflection probes, on the opaque receivers that SSR traces, and the result is
        // published as the SSR lighting texture.
        private void RecordReflection(RenderGraph renderGraph, ContextContainer frameData, TextureHandle specular,
            in RayTracedGIView view, int width, int height)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var stencil = frameData.GetDepthWriteTextureHandle();
            if (!stencil.IsValid())
                return;
            var transparentDepth = frameData.Contains<TransparentDepthData>() ? frameData.Get<TransparentDepthData>() : null;
            bool comparePostDepth = transparentDepth != null && transparentDepth.PostDepthTexture.IsValid();

            using var builder = renderGraph.AddComputePass<ReflectionPassData>("Ray Traced GI Reflection", out var passData, _reflectionSampler);
            passData.Shader = _compositeShader;
            passData.Kernel = _mergeReflectionKernel;
            passData.ScreenSpaceReflection = _rendererData.SampleScreenSpaceReflection && _rendererData.ScreenSpaceReflectionTexture.IsValid()
                ? _rendererData.ScreenSpaceReflectionTexture
                : renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());
            passData.Specular = specular;
            passData.Depth = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            passData.Normals = resourceData.cameraNormalsTexture;
            passData.Stencil = stencil;
            passData.ComparePostDepth = comparePostDepth;
            passData.PostDepth = comparePostDepth ? transparentDepth.PostDepthTexture : passData.Depth;
            passData.Output = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                name = "_SsrLightingTexture"
            });
            passData.View = view;
            passData.Width = width;
            passData.Height = height;
            builder.UseTexture(passData.ScreenSpaceReflection);
            builder.UseTexture(passData.Specular);
            builder.UseTexture(passData.Depth);
            builder.UseTexture(passData.Normals);
            builder.UseTexture(passData.Stencil);
            if (comparePostDepth)
                builder.UseTexture(passData.PostDepth);
            builder.UseTexture(passData.Output, AccessFlags.WriteAll);
            builder.SetGlobalTextureAfterPass(passData.Output, IllusionShaderProperties.SsrLightingTexture);
            builder.AllowPassCulling(false);
            _rendererData.ScreenSpaceReflectionTexture = passData.Output;

            builder.SetRenderFunc(static (ReflectionPassData data, ComputeGraphContext graphContext) =>
            {
                var cmd = graphContext.cmd;
                int kernel = data.Kernel;
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIScreenSpaceReflection, data.ScreenSpaceReflection);
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGISpecularLighting, data.Specular);
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIDepth, data.Depth);
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGINormals, data.Normals);
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIStencil, data.Stencil, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIPostDepth, data.PostDepth);
                cmd.SetComputeIntParam(data.Shader, ShaderIDs._RayTracedGIComparePostDepth, data.ComparePostDepth ? 1 : 0);
                cmd.SetComputeTextureParam(data.Shader, kernel, ShaderIDs._RayTracedGIReflection, data.Output);
                SetViewDepth(cmd, data.Shader, data.View);
                cmd.SetComputeIntParams(data.Shader, ShaderIDs._RayTracedGISize, data.Width, data.Height);
                cmd.DispatchCompute(data.Shader, kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
            });
        }

        private unsafe TextureHandle RecordUpsample(RenderGraph renderGraph, TextureHandle input, string name, int globalTextureId,
            int width, int height, int halfWidth, int halfHeight)
        {
            using var builder = renderGraph.AddComputePass<UpsamplePassData>("Ray Traced GI Upsample", out var passData, _upsampleSampler);
            var halfScreenSize = new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight);
            _upsampleVariables._HalfScreenSize = halfScreenSize;
            for (int i = 0; i < 16; ++i)
                _upsampleVariables._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];
            for (int i = 0; i < 32; ++i)
                _upsampleVariables._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
            passData.Shader = _upsampleShader;
            passData.Kernel = _upsampleKernel;
            passData.Variables = _upsampleVariables;
            passData.Input = input;
            passData.Depth = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            passData.Output = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                enableRandomWrite = true,
                name = name
            });
            passData.HalfScreenSize = halfScreenSize;
            passData.Width = width;
            passData.Height = height;
            builder.UseTexture(passData.Input);
            builder.UseTexture(passData.Depth);
            builder.UseTexture(passData.Output, AccessFlags.WriteAll);
            if (globalTextureId != 0)
                builder.SetGlobalTextureAfterPass(passData.Output, globalTextureId);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (UpsamplePassData data, ComputeGraphContext graphContext) =>
            {
                var cmd = graphContext.cmd;
                ConstantBuffer.Push(cmd, data.Variables, data.Shader, ShaderIDs.ShaderVariablesBilateralUpsample);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._LowResolutionTexture, data.Input);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, IllusionShaderProperties._DepthPyramid, data.Depth);
                cmd.SetComputeVectorParam(data.Shader, ShaderIDs._HalfScreenSize, data.HalfScreenSize);
                cmd.SetComputeTextureParam(data.Shader, data.Kernel, ShaderIDs._OutputUpscaledTexture, data.Output);
                cmd.DispatchCompute(data.Shader, data.Kernel, (data.Width + 7) / 8, (data.Height + 7) / 8, 1);
            });
            return passData.Output;
        }

        private static void TraceInline(CommandBuffer cmd, TracePassData data)
        {
            int groupsX = (data.DispatchWidth + InlineThreadGroupSize - 1) / InlineThreadGroupSize;
            int groupsY = (data.Height + InlineThreadGroupSize - 1) / InlineThreadGroupSize;

            using (new ProfilingScope(cmd, Samplers.InitialSamples))
            {
                DispatchInlineInitialSamples(cmd, data, data.InlineInitialTraceKernel, groupsX, groupsY);
                DispatchInlineInitialSamples(cmd, data, data.InlineInitialShadeKernel, groupsX, groupsY);
            }

            if (data.Temporal)
            {
                using (new ProfilingScope(cmd, Samplers.TemporalResampling))
                    DispatchInlineResampling(cmd, data, data.InlineTemporalKernel, groupsX, groupsY);
            }
            if (data.Spatial)
            {
                using (new ProfilingScope(cmd, Samplers.SpatialResampling))
                    DispatchInlineResampling(cmd, data, data.InlineSpatialKernel, groupsX, groupsY);
            }
            using (new ProfilingScope(cmd, Samplers.FinalShading))
                DispatchInlineResampling(cmd, data, data.InlineFinalShadingKernel, groupsX, groupsY);
        }

        private static void DispatchInlineInitialSamples(CommandBuffer cmd, TracePassData data, int kernel, int groupsX, int groupsY)
        {
            var initial = new PathTracingShaderTarget(data.InlineInitialSamples, kernel);
            data.World.BindInline(cmd, initial);
            data.Scene.BindTraceInputs(cmd, initial, data.World, data.LightBaker, data.PipelineResources, data.Profiles,
                data.SubsurfaceRayBias, data.Exposure, PathTracingEnvironment.CameraBackground(data.Camera),
                data.SampleConstants, data.MiniConstants);
            data.Scene.BindOutputPlaceholders(cmd, initial);
            BindReceiver(cmd, initial, data);
            initial.SetBuffer(cmd, ShaderIDs.u_SecondaryGBuffer, data.SecondarySurfaces);
            initial.SetBuffer(cmd, ShaderIDs.u_GIReservoirs, data.Reservoirs);
            initial.SetBuffer(cmd, ShaderIDs.u_RayTracedGIHits, data.InlineHits);
            cmd.DispatchCompute(data.InlineInitialSamples, kernel, groupsX, groupsY, 1);
        }

        // Must match RAY_TRACED_GI_HIT_STRIDE.
        private GraphicsBuffer EnsureInlineHits(int reservoirs)
        {
            int words = Mathf.Max(1, reservoirs) * 64;
            if (_inlineHits == null || _inlineHits.count < words)
            {
                _inlineHits?.Release();
                _inlineHits = new GraphicsBuffer(GraphicsBuffer.Target.Raw, words, sizeof(uint)) { name = "RayTracedGIInlineHits" };
            }
            return _inlineHits;
        }

        private static void DispatchInlineResampling(CommandBuffer cmd, TracePassData data, int kernel, int groupsX, int groupsY)
        {
            var resampling = new PathTracingShaderTarget(data.InlineResampling, kernel);
            data.World.BindInline(cmd, resampling);
            BindResampling(cmd, resampling, data);
            resampling.SetTexture(cmd, ShaderIDs.u_DiffuseLighting, data.DiffuseLighting);
            resampling.SetTexture(cmd, ShaderIDs.u_SpecularLighting, data.SpecularLighting);
            resampling.SetTexture(cmd, ShaderIDs.t_RayTracedGIPreIntegratedFGD, data.Scene.Resources.GetFGD(cmd));
            cmd.DispatchCompute(data.InlineResampling, kernel, groupsX, groupsY, 1);
        }

        private static void BindReceiver(CommandBuffer cmd, PathTracingShaderTarget shader, TracePassData data)
        {
            shader.SetBuffer(cmd, ShaderIDs.t_RayTracedGIConstants, data.Constants);
            shader.SetTexture(cmd, ShaderIDs.t_RayTracedGIDepth, data.Depth);
            shader.SetTexture(cmd, ShaderIDs.t_RayTracedGINormals, data.Normals);
            shader.SetTexture(cmd, ShaderIDs.t_RayTracedGIForwardGBuffer, data.ForwardGBuffer);
            shader.SetTexture(cmd, ShaderIDs.t_RayTracedGIPrevDepth, data.PreviousDepth);
            shader.SetTexture(cmd, ShaderIDs.t_RayTracedGIPrevNormalRoughness, data.PreviousNormalRoughness);
            shader.SetTexture(cmd, ShaderIDs.t_RayTracedGIMotion, data.Motion);
        }

        private static void UpdateRadianceCache(CommandBuffer cmd, TracePassData data)
        {
            var cache = data.CacheShader;
            const int groups = RayTracedGlobalIlluminationContext.CacheCapacity / CacheThreadGroupSize;
            using (new ProfilingScope(cmd, Samplers.CacheUpdate))
            {
                if (data.CacheClear)
                {
                    BindCache(cmd, cache, data.CacheClearKernel, data);
                    cmd.DispatchCompute(cache, data.CacheClearKernel, groups, 1, 1);
                }
                var update = data.CacheUpdateShader;
                data.Scene.BindTraceInputs(cmd, update, data.World, data.LightBaker, data.PipelineResources, data.Profiles,
                    data.SubsurfaceRayBias, data.Exposure, PathTracingEnvironment.CameraBackground(data.Camera),
                    data.SampleConstants, data.MiniConstants);
                data.Scene.BindOutputPlaceholders(cmd, update);
                BindReceiver(cmd, update, data);
                BindCache(cmd, update, data);
                cmd.DispatchRays(update, CacheUpdateRayGenName, (uint)((data.Width + CacheUpdateBlockSize - 1) / CacheUpdateBlockSize),
                    (uint)((data.Height + CacheUpdateBlockSize - 1) / CacheUpdateBlockSize), 1, data.Camera);
            }
            using (new ProfilingScope(cmd, Samplers.CacheResolve))
            {
                BindCache(cmd, cache, data.CacheResolveKernel, data);
                cmd.SetComputeVectorParam(cache, ShaderIDs._RayTracedGICacheCameraPositionPrev, data.CacheCameraPositionPrev);
                cmd.SetComputeIntParam(cache, ShaderIDs._RayTracedGICacheFrameIndex, (int)data.CacheFrameIndex);
                cmd.DispatchCompute(cache, data.CacheResolveKernel, groups, 1, 1);
            }
        }

        private static void BindCache(CommandBuffer cmd, RayTracingShader shader, TracePassData data)
        {
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_RayTracedGICacheHashEntries, data.CacheHashEntries);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_RayTracedGICacheAccumulation, data.CacheAccumulation);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_RayTracedGICacheResolved, data.CacheResolved);
            cmd.SetRayTracingVectorParam(shader, ShaderIDs._RayTracedGICacheCameraPosition, data.CacheCameraPosition);
            cmd.SetRayTracingIntParam(shader, ShaderIDs._RayTracedGICacheCapacity, RayTracedGlobalIlluminationContext.CacheCapacity);
        }

        private static void BindCache(CommandBuffer cmd, ComputeShader shader, int kernel, TracePassData data)
        {
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_RayTracedGICacheHashEntries, data.CacheHashEntries);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_RayTracedGICacheAccumulation, data.CacheAccumulation);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_RayTracedGICacheResolved, data.CacheResolved);
            cmd.SetComputeVectorParam(shader, ShaderIDs._RayTracedGICacheCameraPosition, data.CacheCameraPosition);
            cmd.SetComputeIntParam(shader, ShaderIDs._RayTracedGICacheCapacity, RayTracedGlobalIlluminationContext.CacheCapacity);
        }

        private static void BindResampling(CommandBuffer cmd, PathTracingShaderTarget shader, TracePassData data)
        {
            shader.SetShaderPass(cmd, PathTracingPass.MaterialPassName);
            shader.SetAccelerationStructure(cmd, ShaderIDs.SceneBVH, data.World.Scene.AccelerationStructure);
            BindReceiver(cmd, shader, data);
            shader.SetBuffer(cmd, ShaderIDs.t_NeighborOffsets, data.NeighborOffsets);
            shader.SetBuffer(cmd, ShaderIDs.u_SecondaryGBuffer, data.SecondarySurfaces);
            shader.SetBuffer(cmd, ShaderIDs.u_GIReservoirs, data.Reservoirs);
        }

        // The low-discrepancy neighbor offsets of the RTXDI SDK within a unit disc, as its signed 8-bit table stores them.
        private static Vector2[] BuildNeighborOffsets()
        {
            const int radius = 250;
            const float phi2 = 1.0f / 1.3247179572447f;
            var offsets = new Vector2[NeighborOffsetCount];
            float u = 0.5f;
            float v = 0.5f;
            for (int count = 0; count < NeighborOffsetCount;)
            {
                u += phi2;
                v += phi2 * phi2;
                if (u >= 1.0f) u -= 1.0f;
                if (v >= 1.0f) v -= 1.0f;
                float rSq = (u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f);
                if (rSq > 0.25f)
                    continue;
                offsets[count++] = new Vector2((sbyte)((u - 0.5f) * radius) / 127.0f, (sbyte)((v - 0.5f) * radius) / 127.0f);
            }
            return offsets;
        }

        // The buffer roles of the RTXDI ReSTIR GI context for each resampling mode.
        private static RtxdiGIBufferIndices BuildBufferIndices(RayTracedGIResamplingMode mode, uint frameIndex)
        {
            var indices = new RtxdiGIBufferIndices();
            switch (mode)
            {
                case RayTracedGIResamplingMode.Temporal:
                    indices.SecondarySurfaceReSTIRDIOutputBufferIndex = frameIndex & 1;
                    indices.TemporalResamplingInputBufferIndex = indices.SecondarySurfaceReSTIRDIOutputBufferIndex ^ 1;
                    indices.TemporalResamplingOutputBufferIndex = indices.SecondarySurfaceReSTIRDIOutputBufferIndex;
                    indices.FinalShadingInputBufferIndex = indices.TemporalResamplingOutputBufferIndex;
                    break;
                case RayTracedGIResamplingMode.Spatial:
                    indices.SpatialResamplingOutputBufferIndex = 1;
                    indices.FinalShadingInputBufferIndex = 1;
                    break;
                case RayTracedGIResamplingMode.TemporalAndSpatial:
                    indices.TemporalResamplingInputBufferIndex = 1;
                    indices.SpatialResamplingOutputBufferIndex = 1;
                    indices.FinalShadingInputBufferIndex = 1;
                    break;
            }
            return indices;
        }

        private RayTracedGIConstants BuildConstants(Camera camera, Matrix4x4 worldToView, Matrix4x4 projection,
            RayTracedGlobalIllumination volume, RayTracedGlobalIlluminationContext context, bool historyValid, int width, int height,
            Vector4 textureSizeAndScale)
        {
            // Receivers are reconstructed through the projection the camera depth was rendered with, jitter included,
            // so perspective, orthographic, lens shifted and oblique projections all match raster. The matrices are camera-relative
            // to keep their precision.
            var viewToWorld = worldToView.inverse;
            var relativeWorldToView = worldToView;
            relativeWorldToView.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            var relativeViewToWorld = viewToWorld;
            relativeViewToWorld.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            var gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
            float yFlip = SystemInfo.graphicsUVStartsAtTop ? -1.0f : 1.0f;
            var texelToClip = Matrix4x4.identity;
            texelToClip.m00 = 2.0f / textureSizeAndScale.x;
            texelToClip.m03 = -1.0f;
            texelToClip.m11 = 2.0f * yFlip / textureSizeAndScale.y;
            texelToClip.m13 = -yFlip;
            float pixelSize = 2.0f / (Mathf.Abs(projection.m11) * height);

            return new RayTracedGIConstants
            {
                View = new RayTracedGIView
                {
                    WorldToTexel = texelToClip.inverse * gpuProjection * relativeWorldToView,
                    TexelToWorld = relativeViewToWorld * gpuProjection.inverse * texelToClip,
                    CameraPosition = viewToWorld.GetColumn(3),
                    RayBias = _rendererData.ScaleWorldDistance(RayBias),
                    CameraForward = -((Vector3)viewToWorld.GetColumn(2)).normalized,
                    FarDepth = SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f,
                    EnvironmentDistance = _rendererData.ScaleWorldDistance(EnvironmentDistance),
                    PixelConeWidth = camera.orthographic ? pixelSize : 0.0f,
                    PixelConeSpreadAngle = camera.orthographic ? 0.0f : Mathf.Atan(pixelSize),
                    Orthographic = camera.orthographic ? 1u : 0u,
                    ViewportSize = new Vector2(width, height),
                    ViewportSizeInv = new Vector2(1.0f / width, 1.0f / height),
                    TextureSizeAndScale = textureSizeAndScale
                },
                PrevView = context.PreviousView,
                HistoryValid = historyValid ? 1u : 0u,
                RuntimeParams = new RtxdiRuntimeParameters { NeighborOffsetMask = NeighborOffsetCount - 1, FrameIndex = context.FrameIndex },
                ReSTIRGI = new RtxdiGIParameters
                {
                    ReservoirBufferParams = context.ReservoirParameters,
                    BufferIndices = BuildBufferIndices(volume.resamplingMode.value, context.FrameIndex),
                    TemporalResamplingParams = new RtxdiGITemporalResamplingParameters
                    {
                        DepthThreshold = 0.1f,
                        NormalThreshold = 0.6f,
                        MaxHistoryLength = (uint)volume.maxHistoryLength.value,
                        EnableFallbackSampling = 1,
                        BiasCorrectionMode = (uint)volume.temporalBiasCorrection.value,
                        MaxReservoirAge = (uint)volume.maxReservoirAge.value
                    },
                    SpatialResamplingParams = new RtxdiGISpatialResamplingParameters
                    {
                        DepthThreshold = 0.1f,
                        NormalThreshold = 0.6f,
                        NumSamples = (uint)volume.spatialSamples.value,
                        SamplingRadius = volume.spatialRadius.value,
                        BiasCorrectionMode = (uint)volume.spatialBiasCorrection.value
                    },
                    FinalShadingParams = new RtxdiGIFinalShadingParameters { EnableFinalVisibility = 1, EnableFinalMIS = 1 }
                }
            };
        }

        private PathTracingSampleConstants BuildSampleConstants(PathTracing.PathTracing settings, in PathTracingCameraData cameraData,
            Matrix4x4 worldToView, Matrix4x4 viewToClip, RayTracedGlobalIlluminationContext context, int width, int height)
        {
            var view = PathTracingFrameConstants.BuildView(worldToView, viewToClip, width, height);
            return new PathTracingSampleConstants
            {
                View = view,
                PreviousView = view,
                EnvMapSceneParams = PathTracingFrameConstants.BuildEnvironment(true, 1.0f, Quaternion.identity),
                EnvMapImportanceSamplingParams = new PathTracingEnvMapImportanceSamplingParams
                {
                    ImportanceInvDim = Vector2.one / PathTracingEnvironment.ImportanceMapSize,
                    ImportanceBaseMip = (uint)(_scene.Environment.ImportanceMapMipCount - 1)
                },
                PtConsts = new PathTracerConstants
                {
                    ImageWidth = (uint)width,
                    ImageHeight = (uint)height,
                    SampleBaseIndex = context.FrameIndex,
                    BounceCount = 2,
                    DiffuseBounceCount = 2,
                    EnvironmentMapDiffuseSampleMIPLevel = settings.environmentDiffuseMipOffset.value,
                    InvSubSampleCount = 1.0f,
                    PreExposedGrayLuminance = 1.0f,
                    FrameIndex = (uint)PathTracingFrame.Index,
                    NEEEnabled = 1,
                    NEEType = 2,
                    NEECandidateSamples = 5,
                    NEEFullSamples = 1,
                    Camera = cameraData,
                    PrevCamera = cameraData
                },
                Debug = new PathTracingDebugConstants { PickX = -1, PickY = -1, ImageWidth = width, ImageHeight = height }
            };
        }

        private RayTracedGlobalIlluminationContext GetContext(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (!_contexts.TryGetValue(id, out var context))
            {
                context = new RayTracedGlobalIlluminationContext();
                _contexts.Add(id, context);
            }
            context.LastUsedFrame = PathTracingFrame.Index;
            return context;
        }

        public void ReleaseUnused()
        {
            _expired.Clear();
            foreach (var pair in _contexts)
            {
                if (PathTracingFrame.Index - pair.Value.LastUsedFrame > ContextReleaseFrames)
                    _expired.Add(pair.Key);
            }
            foreach (int id in _expired)
            {
                _denoiser?.Release(id);
                _contexts[id].Dispose();
                _contexts.Remove(id);
            }
        }

        public void Dispose()
        {
            _denoiser?.Dispose();
            foreach (var context in _contexts.Values)
                context.Dispose();
            _contexts.Clear();
            _constants.Release();
            _sampleConstants.Release();
            _miniConstants.Release();
            _neighborOffsets.Release();
            _inlineHits?.Release();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _inlineValidation.Dispose();
#endif
        }

        private static class Samplers
        {
            public static readonly ProfilingSampler InitialSamples = new("Ray Traced GI Initial Samples");
            public static readonly ProfilingSampler TemporalResampling = new("Ray Traced GI Temporal Resampling");
            public static readonly ProfilingSampler SpatialResampling = new("Ray Traced GI Spatial Resampling");
            public static readonly ProfilingSampler FinalShading = new("Ray Traced GI Final Shading");
            public static readonly ProfilingSampler CacheUpdate = new("Ray Traced GI Cache Update");
            public static readonly ProfilingSampler CacheResolve = new("Ray Traced GI Cache Resolve");
        }

        private static class ShaderIDs
        {
            public static readonly int SceneBVH = Shader.PropertyToID("SceneBVH");
            public static readonly int t_RayTracedGIConstants = Shader.PropertyToID("t_RayTracedGIConstants");
            public static readonly int t_RayTracedGIDepth = Shader.PropertyToID("t_RayTracedGIDepth");
            public static readonly int t_RayTracedGINormals = Shader.PropertyToID("t_RayTracedGINormals");
            public static readonly int t_RayTracedGIForwardGBuffer = Shader.PropertyToID("t_RayTracedGIForwardGBuffer");
            public static readonly int t_RayTracedGIPrevDepth = Shader.PropertyToID("t_RayTracedGIPrevDepth");
            public static readonly int t_RayTracedGIPrevNormalRoughness = Shader.PropertyToID("t_RayTracedGIPrevNormalRoughness");
            public static readonly int t_NeighborOffsets = Shader.PropertyToID("t_NeighborOffsets");
            public static readonly int _RayTracedGIDepth = Shader.PropertyToID("_RayTracedGIDepth");
            public static readonly int _RayTracedGINormals = Shader.PropertyToID("_RayTracedGINormals");
            public static readonly int _RayTracedGIForwardGBuffer = Shader.PropertyToID("_RayTracedGIForwardGBuffer");
            public static readonly int _RayTracedGIPrevDepth = Shader.PropertyToID("_RayTracedGIPrevDepth");
            public static readonly int _RayTracedGIPrevNormalRoughness = Shader.PropertyToID("_RayTracedGIPrevNormalRoughness");
            public static readonly int _RayTracedGIViewDepthZ = Shader.PropertyToID("_RayTracedGIViewDepthZ");
            public static readonly int _RayTracedGIViewDepthW = Shader.PropertyToID("_RayTracedGIViewDepthW");
            public static readonly int _RayTracedGIFarDepth = Shader.PropertyToID("_RayTracedGIFarDepth");
            public static readonly int _RayTracedGIDenoiserMode = Shader.PropertyToID("_RayTracedGIDenoiserMode");
            public static readonly int _RayTracedGIMotion = Shader.PropertyToID("_RayTracedGIMotion");
            public static readonly int t_RayTracedGIPreIntegratedFGD = Shader.PropertyToID("t_RayTracedGIPreIntegratedFGD");
            public static readonly int _RayTracedGISpecular = Shader.PropertyToID("_RayTracedGISpecular");
            public static readonly int _RayTracedGISpecularAccumulation = Shader.PropertyToID("_RayTracedGISpecularAccumulation");
            public static readonly int _RayTracedGISpecularOutput = Shader.PropertyToID("_RayTracedGISpecularOutput");
            public static readonly int _RayTracedGIScreenSpaceReflection = Shader.PropertyToID("_RayTracedGIScreenSpaceReflection");
            public static readonly int _RayTracedGISpecularLighting = Shader.PropertyToID("_RayTracedGISpecularLighting");
            public static readonly int _RayTracedGIStencil = Shader.PropertyToID("_RayTracedGIStencil");
            public static readonly int _RayTracedGIPostDepth = Shader.PropertyToID("_RayTracedGIPostDepth");
            public static readonly int _RayTracedGIComparePostDepth = Shader.PropertyToID("_RayTracedGIComparePostDepth");
            public static readonly int _RayTracedGIReflection = Shader.PropertyToID("_RayTracedGIReflection");
            public static readonly int t_RayTracedGIMotion = Shader.PropertyToID("t_RayTracedGIMotion");
            public static readonly int _RayTracedGITextureScale = Shader.PropertyToID("_RayTracedGITextureScale");
            public static readonly int _RayTracedGITextureSize = Shader.PropertyToID("_RayTracedGITextureSize");
            public static readonly int ShaderVariablesBilateralUpsample = Shader.PropertyToID("ShaderVariablesBilateralUpsample");
            public static readonly int _LowResolutionTexture = Shader.PropertyToID("_LowResolutionTexture");
            public static readonly int _HalfScreenSize = Shader.PropertyToID("_HalfScreenSize");
            public static readonly int _OutputUpscaledTexture = Shader.PropertyToID("_OutputUpscaledTexture");
            public static readonly int _RayTracedGIDenoisingRange = Shader.PropertyToID("_RayTracedGIDenoisingRange");
            public static readonly int _RayTracedGIViewZ = Shader.PropertyToID("_RayTracedGIViewZ");
            public static readonly int _RayTracedGINormalRoughness = Shader.PropertyToID("_RayTracedGINormalRoughness");
            public static readonly int _RayTracedGIMotionVectors = Shader.PropertyToID("_RayTracedGIMotionVectors");
            public static readonly int u_SecondaryGBuffer = Shader.PropertyToID("u_SecondaryGBuffer");
            public static readonly int u_RayTracedGICacheHashEntries = Shader.PropertyToID("u_RayTracedGICacheHashEntries");
            public static readonly int u_RayTracedGICacheAccumulation = Shader.PropertyToID("u_RayTracedGICacheAccumulation");
            public static readonly int u_RayTracedGICacheResolved = Shader.PropertyToID("u_RayTracedGICacheResolved");
            public static readonly int _RayTracedGICacheCameraPosition = Shader.PropertyToID("_RayTracedGICacheCameraPosition");
            public static readonly int _RayTracedGICacheCameraPositionPrev = Shader.PropertyToID("_RayTracedGICacheCameraPositionPrev");
            public static readonly int _RayTracedGICacheCapacity = Shader.PropertyToID("_RayTracedGICacheCapacity");
            public static readonly int _RayTracedGICacheFrameIndex = Shader.PropertyToID("_RayTracedGICacheFrameIndex");
            public static readonly int _RayTracedGIDebugOutput = Shader.PropertyToID("_RayTracedGIDebugOutput");
            public static readonly int _RayTracedGIDebugTexelToWorld = Shader.PropertyToID("_RayTracedGIDebugTexelToWorld");
            public static readonly int _RayTracedGIDebugCameraPosition = Shader.PropertyToID("_RayTracedGIDebugCameraPosition");
            public static readonly int _RayTracedGIDebugSize = Shader.PropertyToID("_RayTracedGIDebugSize");
            public static readonly int _RayTracedGIDebugView = Shader.PropertyToID("_RayTracedGIDebugView");
            public static readonly int u_GIReservoirs = Shader.PropertyToID("u_GIReservoirs");
            public static readonly int u_RayTracedGIHits = Shader.PropertyToID("u_RayTracedGIHits");
            public static readonly int u_DiffuseLighting = Shader.PropertyToID("u_DiffuseLighting");
            public static readonly int u_SpecularLighting = Shader.PropertyToID("u_SpecularLighting");
            public static readonly int _RayTracedGIDiffuse = Shader.PropertyToID("_RayTracedGIDiffuse");
            public static readonly int _RayTracedGIAccumulation = Shader.PropertyToID("_RayTracedGIAccumulation");
            public static readonly int _RayTracedGIOutput = Shader.PropertyToID("_RayTracedGIOutput");
            public static readonly int _RayTracedGISize = Shader.PropertyToID("_RayTracedGISize");
            public static readonly int _RayTracedGIAccumulationWeight = Shader.PropertyToID("_RayTracedGIAccumulationWeight");
            public static readonly int _ExposureTexture = Shader.PropertyToID("_ExposureTexture");
            public static readonly int _IndirectDiffuseTexture = Shader.PropertyToID("_IndirectDiffuseTexture");
        }
    }
}
