using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingPass
    {
        private static void BindTraceResources(CommandBuffer cmd, RayTracingShader shader, TracePassData data)
        {
            var resources = data.Resources;
            var lightBaker = data.LightBaker;
            var environment = data.Environment;

            cmd.SetRayTracingTextureParam(shader, IllusionShaderProperties._OwenScrambledTexture, data.PipelineResources.owenScrambled256Tex);
            cmd.SetRayTracingTextureParam(shader, IllusionShaderProperties._ScramblingTileXSPP, data.PipelineResources.scramblingTile256SPP);
            cmd.SetRayTracingTextureParam(shader, IllusionShaderProperties._RankingTileXSPP, data.PipelineResources.rankingTile256SPP);
            cmd.SetRayTracingTextureParam(shader, IllusionShaderProperties._ScramblingTexture, data.PipelineResources.scramblingTex);
            cmd.SetRayTracingFloatParam(shader, "_PathTracingSubsurfaceRayBias", data.SubsurfaceRayBias);
            ConstantBuffer.PushGlobal(cmd, data.Profiles, Shader.PropertyToID("ShaderVariablesSubsurface"));
            cmd.SetRayTracingTextureParam(shader, "_PreIntegratedFGD_GGXDisneyDiffuse", resources.GetFGD(cmd));
            cmd.SetRayTracingShaderPass(shader, MaterialPassName);
            cmd.SetRayTracingAccelerationStructure(shader, ShaderIDs.SceneBVH, data.World.Scene.AccelerationStructure);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_PathTracingConstants, resources.Constants);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_PathTracingMiniConstants, resources.MiniConstants);
            data.World.Emissive.Bind(cmd, shader);
            data.World.Scene.Instances.MaterialTable.Bind(cmd, shader);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs._PathTracingInstanceData, data.World.Scene.Instances.Buffer);

            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_OutputColor, data.Radiance);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_Depth, data.Depth);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_MotionVectors, data.MotionVectors);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_Throughput, data.Throughput);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_SpecularHitT, data.SpecularHitT);

            cmd.SetRayTracingTextureParam(shader, ShaderIDs.t_EnvironmentMap, environment.Cube);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.t_EnvironmentMapImportanceMap, environment.ImportanceMap);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_LightsCB, lightBaker.ControlBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_Lights, lightBaker.LightsBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_LightsEx, lightBaker.LightsExBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_LightProxyCounters, lightBaker.ProxyCounters);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_LightProxyIndices, lightBaker.ProxyIndices);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.t_LightLocalSamplingBuffer, lightBaker.LocalSamplingBuffer);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.t_EnvLookupMap, lightBaker.EnvLightLookupMap);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_LightFeedbackTotalWeight, lightBaker.FeedbackTotalWeight);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_LightFeedbackCandidates, lightBaker.FeedbackCandidates);

            BindPlaceholders(cmd, shader, resources);
        }

        private static void BindPlaceholders(CommandBuffer cmd, RayTracingShader shader, PathTracingResources resources)
        {
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_ProcessedOutputColor, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_PostTonemapOutputColor, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_ScratchFloat1, resources.DummyFloat);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_StablePlanesHeader, resources.DummyUIntArray);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_StableRadiance, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_RRDiffuseAlbedo, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_RRSpecAlbedo, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_RRNormalsAndRoughness, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_RRSpecMotionVectors, resources.DummyFloat2);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_RRTransparencyLayer, resources.DummyFloat4);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_DenoisingAvgLayerRadiance, resources.DummyFloat4);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_StablePlanesBuffer, resources.DummyStructured);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_SurfaceData, resources.DummyStructured);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_FeedbackBuffer, resources.DummyStructured);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_DebugLinesBuffer, resources.DummyStructured);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_DebugDeltaPathTree, resources.DummyStructured);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_DeltaPathSearchStack, resources.DummyStructured);
        }

        private static void BindStablePlanes(CommandBuffer cmd, RayTracingShader shader, PathTracingRealtimeTargets targets)
        {
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_StablePlanesHeader, targets.StablePlanesHeader);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.u_StablePlanesBuffer, targets.StablePlanes);
            cmd.SetRayTracingTextureParam(shader, ShaderIDs.u_StableRadiance, targets.StableRadiance);
        }

        private static class ShaderIDs
        {
            public static readonly int SceneBVH = Shader.PropertyToID("SceneBVH");
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
            public static readonly int _PathTracingRadiance = Shader.PropertyToID("_PathTracingRadiance");
            public static readonly int _PathTracingAccumulation = Shader.PropertyToID("_PathTracingAccumulation");
            public static readonly int _PathTracingSize = Shader.PropertyToID("_PathTracingSize");
            public static readonly int _PathTracingSampleWeight = Shader.PropertyToID("_PathTracingSampleWeight");
            public static readonly int _PathTracingDepth = Shader.PropertyToID("_PathTracingDepth");
            public static readonly int t_Depth = Shader.PropertyToID("t_Depth");
            public static readonly int t_MotionVectors = Shader.PropertyToID("t_MotionVectors");
            public static readonly int t_SpecularHitT = Shader.PropertyToID("t_SpecularHitT");
            public static readonly int t_StablePlanesHeader = Shader.PropertyToID("t_StablePlanesHeader");
            public static readonly int t_StablePlanesBuffer = Shader.PropertyToID("t_StablePlanesBuffer");
            public static readonly int t_StableRadiance = Shader.PropertyToID("t_StableRadiance");
        }
    }
}
