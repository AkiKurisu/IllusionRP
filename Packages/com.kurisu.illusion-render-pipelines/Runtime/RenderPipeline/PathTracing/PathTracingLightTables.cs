using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingLightTables : IDisposable
    {
        private GraphicsBuffer _targets;

        private readonly GraphicsBuffer _distantLights = new(GraphicsBuffer.Target.Structured,
            PathTracingLightCollector.MaxDirectionalLights, Marshal.SizeOf<PathTracingDistantLight>()) { name = "PathTracingDistantLights" };

        private int _distantLightCount;

        public void Upload(CommandBuffer cmd, PathTracingLightCollector lights)
        {
            int count = lights.Targets.Count;
            if (_targets == null || _targets.count < count)
            {
                _targets?.Release();
                _targets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(count), Marshal.SizeOf<PathTracingLightTarget>())
                {
                    name = "PathTracingLightTargets"
                };
            }
            cmd.SetBufferData(_targets, lights.Targets);
            _distantLightCount = lights.DistantLights.Count;
            if (_distantLightCount > 0)
                cmd.SetBufferData(_distantLights, lights.DistantLights);
        }

        public void Bind(CommandBuffer cmd, RayTracingShader shader)
        {
            cmd.SetRayTracingBufferParam(shader, ShaderIDs._PathTracingLightTargets, _targets);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs._PathTracingDistantLights, _distantLights);
            cmd.SetRayTracingIntParam(shader, ShaderIDs._PathTracingDistantLightCount, _distantLightCount);
        }

        public void Dispose()
        {
            _targets?.Release();
            _targets = null;
            _distantLights.Release();
        }

        private static class ShaderIDs
        {
            public static readonly int _PathTracingLightTargets = Shader.PropertyToID("_PathTracingLightTargets");
            public static readonly int _PathTracingDistantLights = Shader.PropertyToID("_PathTracingDistantLights");
            public static readonly int _PathTracingDistantLightCount = Shader.PropertyToID("_PathTracingDistantLightCount");
        }
    }
}
