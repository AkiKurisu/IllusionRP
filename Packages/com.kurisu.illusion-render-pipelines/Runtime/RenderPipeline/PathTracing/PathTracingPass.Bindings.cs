using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingPass
    {
        private static void BindTraceResources(CommandBuffer cmd, PathTracingShaderTarget shader, TracePassData data)
        {
            if (data.InlineDispatch != null) data.World.BindInline(cmd, shader);
            data.Scene.BindTraceInputs(cmd, shader, data.World, data.LightBaker, data.PipelineResources, data.Profiles,
                data.SubsurfaceRayBias, data.ExposureTexture, PathTracingEnvironment.CameraBackground(data.Camera),
                data.Resources.Constants, data.Resources.MiniConstants);

            shader.SetTexture(cmd, ShaderIDs.u_OutputColor, data.Radiance);
            shader.SetTexture(cmd, ShaderIDs.u_Depth, data.Depth);
            shader.SetTexture(cmd, ShaderIDs.u_MotionVectors, data.MotionVectors);
            shader.SetTexture(cmd, ShaderIDs.u_Throughput, data.Throughput);
            shader.SetTexture(cmd, ShaderIDs.u_SpecularHitT, data.SpecularHitT);
        }

        private static void BindStablePlanes(CommandBuffer cmd, PathTracingShaderTarget shader, PathTracingRealtimeTargets targets)
        {
            shader.SetTexture(cmd, ShaderIDs.u_StablePlanesHeader, targets.StablePlanesHeader);
            shader.SetBuffer(cmd, ShaderIDs.u_StablePlanesBuffer, targets.StablePlanes);
            shader.SetTexture(cmd, ShaderIDs.u_StableRadiance, targets.StableRadiance);
        }

        private static class ShaderIDs
        {
            public static readonly int t_PathTracingConstants = Shader.PropertyToID("t_PathTracingConstants");
            public static readonly int u_RRDiffuseAlbedo = Shader.PropertyToID("u_RRDiffuseAlbedo");
            public static readonly int u_RRSpecAlbedo = Shader.PropertyToID("u_RRSpecAlbedo");
            public static readonly int u_RRNormalsAndRoughness = Shader.PropertyToID("u_RRNormalsAndRoughness");
            public static readonly int u_RRSpecMotionVectors = Shader.PropertyToID("u_RRSpecMotionVectors");
            public static readonly int u_OutputColor = Shader.PropertyToID("u_OutputColor");
            public static readonly int u_Throughput = Shader.PropertyToID("u_Throughput");
            public static readonly int u_MotionVectors = Shader.PropertyToID("u_MotionVectors");
            public static readonly int u_Depth = Shader.PropertyToID("u_Depth");
            public static readonly int u_SpecularHitT = Shader.PropertyToID("u_SpecularHitT");
            public static readonly int u_StablePlanesHeader = Shader.PropertyToID("u_StablePlanesHeader");
            public static readonly int u_StablePlanesBuffer = Shader.PropertyToID("u_StablePlanesBuffer");
            public static readonly int u_StableRadiance = Shader.PropertyToID("u_StableRadiance");
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
