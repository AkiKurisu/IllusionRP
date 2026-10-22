using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    /// <summary>
    /// Ray tracing scene inputs shared by every consumer of a renderer feature: worlds, lights, light tables,
    /// the lighting environment and the trace resources.
    /// </summary>
    internal sealed class PathTracingSharedScene : IDisposable
    {
        private const int ReleaseFrames = 60;

        private readonly Dictionary<int, PathTracingWorld> _worlds = new();

        private readonly List<int> _expired = new();

        private readonly ComputeShader _emissiveShader;

        private readonly ComputeShader _motionShader;

        private int _lastUsedFrame = -1;

        public PathTracingResources Resources { get; }

        public PathTracingEnvironment Environment { get; }

        public PathTracingLightCollector Lights { get; } = new();

        public PathTracingLightTables LightTables { get; } = new();

        public PathTracingLightBakerKernels LightBakerKernels { get; }

        public PathTracingSkinnedUpdates SkinnedUpdates { get; } = new();

        private readonly PathTracingRectangleLights _rectangleLights;

        private static class Samplers
        {
            public static readonly ProfilingSampler Environment = new("Path Tracing Environment");
            public static readonly ProfilingSampler Lights = new("Path Tracing Lights");
            public static readonly ProfilingSampler Scene = new("Path Tracing Acceleration Structure");
        }

        public PathTracingSharedScene(IllusionRenderPipelineResources resources)
        {
            Resources = new PathTracingResources(resources.pathTracingFGDShader);
            Environment = new PathTracingEnvironment(resources.pathTracingEnvironmentCS, resources.pathTracingEnvironmentLightingShader);
            LightBakerKernels = new PathTracingLightBakerKernels(resources.pathTracingLightsBakerCS);
            _rectangleLights = new PathTracingRectangleLights(resources.pathTracingRectangleLightShader);
            _emissiveShader = resources.pathTracingEmissiveCS;
            _motionShader = resources.pathTracingMotionCS;
        }

        public static bool IsSupported => SystemInfo.supportsRayTracing && SystemInfo.supportsInlineRayTracing;

        public static bool HasResources(IllusionRenderPipelineResources resources)
        {
            return resources.pathTracingFGDShader && resources.pathTracingLightsBakerCS && resources.pathTracingEnvironmentCS
                   && resources.pathTracingEnvironmentLightingShader && resources.pathTracingEmissiveCS
                   && resources.pathTracingRectangleLightShader && resources.pathTracingMotionCS;
        }

        /// <summary>
        /// Collects the lights and updates the world of the participation layers for a camera.
        /// </summary>
        public PathTracingWorld Update(Camera camera, int layerMask, float directionalAngularDiameter, Light perObjectSelector,
            uint perObjectLayers, bool trackMotion)
        {
            _lastUsedFrame = PathTracingFrame.Index;
            Lights.Collect(directionalAngularDiameter, perObjectSelector, perObjectLayers);
            _rectangleLights.Update(Lights);
            if (!_worlds.TryGetValue(layerMask, out var world))
            {
                world = new PathTracingWorld(_emissiveShader, _motionShader);
                _worlds.Add(layerMask, world);
            }
            world.Update(camera, layerMask, trackMotion);
            return world;
        }

        /// <summary>
        /// The environment may change the view and projection matrices; callers restore their own.
        /// </summary>
        public void Prepare(CommandBuffer cmd, PathTracingWorld world, PathTracingLightBaker lightBaker, RenderTargetIdentifier exposureTexture,
            Vector3 cameraPosition, Matrix4x4 worldToClip, int width, int height, int frame, bool temporalFeedback)
        {
            using (new ProfilingScope(cmd, Samplers.Environment))
                Environment.Update(cmd, Lights, exposureTexture);
            using (new ProfilingScope(cmd, Samplers.Lights))
            {
                world.Emissive.Prepare(cmd);
                lightBaker.UpdateBegin(cmd, Lights, Environment, world.Emissive, cameraPosition, worldToClip, width, height, temporalFeedback);
                LightTables.Upload(cmd, Lights);
            }
            using (new ProfilingScope(cmd, Samplers.Scene))
                world.Build(cmd, frame);
        }

        /// <summary>
        /// Binds the scene, lights, environment, sampling tables and constants that path tracing ray generation reads,
        /// with placeholders for the realtime and debug outputs.
        /// </summary>
        public void BindTraceInputs(CommandBuffer cmd, PathTracingShaderTarget shader, PathTracingWorld world, PathTracingLightBaker lightBaker,
            IllusionRenderPipelineResources pipelineResources, in PathTracingDiffusionProfiles profiles, float subsurfaceRayBias,
            RenderTargetIdentifier exposureTexture, Vector4 cameraBackground, GraphicsBuffer constants, GraphicsBuffer miniConstants)
        {
            shader.SetTexture(cmd, IllusionShaderProperties._OwenScrambledTexture, pipelineResources.owenScrambled256Tex);
            shader.SetTexture(cmd, IllusionShaderProperties._ScramblingTileXSPP, pipelineResources.scramblingTile256SPP);
            shader.SetTexture(cmd, IllusionShaderProperties._RankingTileXSPP, pipelineResources.rankingTile256SPP);
            shader.SetTexture(cmd, IllusionShaderProperties._ScramblingTexture, pipelineResources.scramblingTex);
            shader.SetFloat(cmd, ShaderIDs._PathTracingSubsurfaceRayBias, subsurfaceRayBias);
            ConstantBuffer.PushGlobal(cmd, profiles, ShaderIDs.ShaderVariablesSubsurface);
            shader.SetTexture(cmd, ShaderIDs._PreIntegratedFGD_GGXDisneyDiffuse, Resources.GetFGD(cmd));
            shader.SetAccelerationStructure(cmd, ShaderIDs.SceneBVH, world.Scene.AccelerationStructure);
            shader.SetBuffer(cmd, ShaderIDs.t_PathTracingConstants, constants);
            shader.SetBuffer(cmd, ShaderIDs.t_PathTracingMiniConstants, miniConstants);
            world.Emissive.Bind(cmd, shader);
            LightTables.Bind(cmd, shader);
            world.Scene.Instances.MaterialTable.Bind(cmd, shader);
            shader.SetBuffer(cmd, ShaderIDs._PathTracingInstanceData, world.Scene.Instances.Buffer);

            shader.SetTexture(cmd, ShaderIDs.t_EnvironmentMap, Environment.Cube);
            shader.SetTexture(cmd, ShaderIDs.t_PathTracingBackground, Environment.Background);
            shader.SetVector(cmd, ShaderIDs._PathTracingBackgroundColor, cameraBackground);
            shader.SetTexture(cmd, IllusionShaderProperties._ExposureTexture, exposureTexture);
            shader.SetTexture(cmd, ShaderIDs.t_EnvironmentMapImportanceMap, Environment.ImportanceMap);
            shader.SetBuffer(cmd, ShaderIDs.t_LightsCB, lightBaker.ControlBuffer);
            shader.SetBuffer(cmd, ShaderIDs.t_Lights, lightBaker.LightsBuffer);
            shader.SetBuffer(cmd, ShaderIDs.t_LightsEx, lightBaker.LightsExBuffer);
            shader.SetBuffer(cmd, ShaderIDs.t_LightProxyCounters, lightBaker.ProxyCounters);
            shader.SetBuffer(cmd, ShaderIDs.t_LightProxyIndices, lightBaker.ProxyIndices);
            shader.SetBuffer(cmd, ShaderIDs.t_LightLocalSamplingBuffer, lightBaker.LocalSamplingBuffer);
            shader.SetTexture(cmd, ShaderIDs.t_EnvLookupMap, lightBaker.EnvLightLookupMap);
            shader.SetTexture(cmd, ShaderIDs.u_LightFeedbackTotalWeight, lightBaker.FeedbackTotalWeight);
            shader.SetTexture(cmd, ShaderIDs.u_LightFeedbackCandidates, lightBaker.FeedbackCandidates);

            shader.SetTexture(cmd, ShaderIDs.u_ProcessedOutputColor, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_PostTonemapOutputColor, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_ScratchFloat1, Resources.DummyFloat);
            shader.SetTexture(cmd, ShaderIDs.u_StablePlanesHeader, Resources.DummyUIntArray);
            shader.SetTexture(cmd, ShaderIDs.u_StableRadiance, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_RRDiffuseAlbedo, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_RRSpecAlbedo, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_RRNormalsAndRoughness, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_RRSpecMotionVectors, Resources.DummyFloat2);
            shader.SetTexture(cmd, ShaderIDs.u_RRTransparencyLayer, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_DenoisingAvgLayerRadiance, Resources.DummyFloat4);
            shader.SetBuffer(cmd, ShaderIDs.u_StablePlanesBuffer, Resources.DummyStructured);
            shader.SetBuffer(cmd, ShaderIDs.u_SurfaceData, Resources.DummyStructured);
            shader.SetBuffer(cmd, ShaderIDs.u_FeedbackBuffer, Resources.DummyStructured);
            shader.SetBuffer(cmd, ShaderIDs.u_DebugLinesBuffer, Resources.DummyStructured);
            shader.SetBuffer(cmd, ShaderIDs.u_DebugDeltaPathTree, Resources.DummyStructured);
            shader.SetBuffer(cmd, ShaderIDs.u_DeltaPathSearchStack, Resources.DummyStructured);
        }

        /// <summary>
        /// Binds placeholders for the reference camera outputs of a ray generation shader that writes none of them.
        /// </summary>
        public void BindOutputPlaceholders(CommandBuffer cmd, PathTracingShaderTarget shader)
        {
            shader.SetTexture(cmd, ShaderIDs.u_OutputColor, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_Depth, Resources.DummyFloat);
            shader.SetTexture(cmd, ShaderIDs.u_MotionVectors, Resources.DummyFloat4);
            shader.SetTexture(cmd, ShaderIDs.u_Throughput, Resources.DummyUInt);
            shader.SetTexture(cmd, ShaderIDs.u_SpecularHitT, Resources.DummyFloat);
        }

        public void ReleaseUnused()
        {
            _expired.Clear();
            foreach (var pair in _worlds)
            {
                if (PathTracingFrame.Index - pair.Value.LastUsedFrame > ReleaseFrames)
                    _expired.Add(pair.Key);
            }
            foreach (int layerMask in _expired)
            {
                _worlds[layerMask].Dispose();
                _worlds.Remove(layerMask);
            }
            if (PathTracingFrame.Index - _lastUsedFrame > ReleaseFrames)
                Environment.Release();
        }

        public void Dispose()
        {
            foreach (var world in _worlds.Values)
                world.Dispose();
            _worlds.Clear();
            _rectangleLights.Dispose();
            SkinnedUpdates.Dispose();
            LightTables.Dispose();
            Resources.Dispose();
            Environment.Dispose();
        }

        private static class ShaderIDs
        {
            public static readonly int SceneBVH = Shader.PropertyToID("SceneBVH");
            public static readonly int ShaderVariablesSubsurface = Shader.PropertyToID("ShaderVariablesSubsurface");
            public static readonly int _PathTracingSubsurfaceRayBias = Shader.PropertyToID("_PathTracingSubsurfaceRayBias");
            public static readonly int _PreIntegratedFGD_GGXDisneyDiffuse = Shader.PropertyToID("_PreIntegratedFGD_GGXDisneyDiffuse");
            public static readonly int _PathTracingInstanceData = Shader.PropertyToID("_PathTracingInstanceData");
            public static readonly int t_PathTracingConstants = Shader.PropertyToID("t_PathTracingConstants");
            public static readonly int t_PathTracingMiniConstants = Shader.PropertyToID("t_PathTracingMiniConstants");
            public static readonly int u_OutputColor = Shader.PropertyToID("u_OutputColor");
            public static readonly int u_ProcessedOutputColor = Shader.PropertyToID("u_ProcessedOutputColor");
            public static readonly int u_PostTonemapOutputColor = Shader.PropertyToID("u_PostTonemapOutputColor");
            public static readonly int u_Throughput = Shader.PropertyToID("u_Throughput");
            public static readonly int u_MotionVectors = Shader.PropertyToID("u_MotionVectors");
            public static readonly int u_Depth = Shader.PropertyToID("u_Depth");
            public static readonly int u_SpecularHitT = Shader.PropertyToID("u_SpecularHitT");
            public static readonly int u_ScratchFloat1 = Shader.PropertyToID("u_ScratchFloat1");
            public static readonly int u_StablePlanesHeader = Shader.PropertyToID("u_StablePlanesHeader");
            public static readonly int u_StablePlanesBuffer = Shader.PropertyToID("u_StablePlanesBuffer");
            public static readonly int u_StableRadiance = Shader.PropertyToID("u_StableRadiance");
            public static readonly int u_SurfaceData = Shader.PropertyToID("u_SurfaceData");
            public static readonly int u_FeedbackBuffer = Shader.PropertyToID("u_FeedbackBuffer");
            public static readonly int u_DebugLinesBuffer = Shader.PropertyToID("u_DebugLinesBuffer");
            public static readonly int u_DebugDeltaPathTree = Shader.PropertyToID("u_DebugDeltaPathTree");
            public static readonly int u_DeltaPathSearchStack = Shader.PropertyToID("u_DeltaPathSearchStack");
            public static readonly int u_RRDiffuseAlbedo = Shader.PropertyToID("u_RRDiffuseAlbedo");
            public static readonly int u_RRSpecAlbedo = Shader.PropertyToID("u_RRSpecAlbedo");
            public static readonly int u_RRNormalsAndRoughness = Shader.PropertyToID("u_RRNormalsAndRoughness");
            public static readonly int u_RRSpecMotionVectors = Shader.PropertyToID("u_RRSpecMotionVectors");
            public static readonly int u_RRTransparencyLayer = Shader.PropertyToID("u_RRTransparencyLayer");
            public static readonly int u_DenoisingAvgLayerRadiance = Shader.PropertyToID("u_DenoisingAvgLayerRadiance");
            public static readonly int t_EnvironmentMap = Shader.PropertyToID("t_EnvironmentMap");
            public static readonly int t_PathTracingBackground = Shader.PropertyToID("t_PathTracingBackground");
            public static readonly int _PathTracingBackgroundColor = Shader.PropertyToID("_PathTracingBackgroundColor");
            public static readonly int t_EnvironmentMapImportanceMap = Shader.PropertyToID("t_EnvironmentMapImportanceMap");
            public static readonly int t_LightsCB = Shader.PropertyToID("t_LightsCB");
            public static readonly int t_Lights = Shader.PropertyToID("t_Lights");
            public static readonly int t_LightsEx = Shader.PropertyToID("t_LightsEx");
            public static readonly int t_LightProxyCounters = Shader.PropertyToID("t_LightProxyCounters");
            public static readonly int t_LightProxyIndices = Shader.PropertyToID("t_LightProxyIndices");
            public static readonly int t_LightLocalSamplingBuffer = Shader.PropertyToID("t_LightLocalSamplingBuffer");
            public static readonly int t_EnvLookupMap = Shader.PropertyToID("t_EnvLookupMap");
            public static readonly int u_LightFeedbackTotalWeight = Shader.PropertyToID("u_LightFeedbackTotalWeight");
            public static readonly int u_LightFeedbackCandidates = Shader.PropertyToID("u_LightFeedbackCandidates");
        }
    }
}
