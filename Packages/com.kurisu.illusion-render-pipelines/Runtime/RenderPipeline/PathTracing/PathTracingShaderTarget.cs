using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal readonly struct PathTracingShaderTarget
    {
        private readonly RayTracingShader _rayTracing;
        private readonly ComputeShader _compute;
        private readonly int _kernel;

        public PathTracingShaderTarget(ComputeShader compute, int kernel)
        {
            _rayTracing = null;
            _compute = compute;
            _kernel = kernel;
        }

        private PathTracingShaderTarget(RayTracingShader rayTracing)
        {
            _rayTracing = rayTracing;
            _compute = null;
            _kernel = 0;
        }

        public static implicit operator PathTracingShaderTarget(RayTracingShader shader) => new(shader);

        public void SetTexture(CommandBuffer cmd, int id, RenderTargetIdentifier texture)
        {
            if (_compute)
                cmd.SetComputeTextureParam(_compute, _kernel, id, texture);
            else
                cmd.SetRayTracingTextureParam(_rayTracing, id, texture);
        }

        public void SetBuffer(CommandBuffer cmd, int id, GraphicsBuffer buffer)
        {
            if (_compute)
                cmd.SetComputeBufferParam(_compute, _kernel, id, buffer);
            else
                cmd.SetRayTracingBufferParam(_rayTracing, id, buffer);
        }

        public void SetFloat(CommandBuffer cmd, int id, float value)
        {
            if (_compute)
                cmd.SetComputeFloatParam(_compute, id, value);
            else
                cmd.SetRayTracingFloatParam(_rayTracing, id, value);
        }

        public void SetInt(CommandBuffer cmd, int id, int value)
        {
            if (_compute)
                cmd.SetComputeIntParam(_compute, id, value);
            else
                cmd.SetRayTracingIntParam(_rayTracing, id, value);
        }

        public void SetVector(CommandBuffer cmd, int id, Vector4 value)
        {
            if (_compute)
                cmd.SetComputeVectorParam(_compute, id, value);
            else
                cmd.SetRayTracingVectorParam(_rayTracing, id, value);
        }

        public void SetAccelerationStructure(CommandBuffer cmd, int id, RayTracingAccelerationStructure accelerationStructure)
        {
            if (_compute)
                cmd.SetRayTracingAccelerationStructure(_compute, _kernel, id, accelerationStructure);
            else
                cmd.SetRayTracingAccelerationStructure(_rayTracing, id, accelerationStructure);
        }

        public void SetShaderPass(CommandBuffer cmd, string passName)
        {
            if (!_compute)
                cmd.SetRayTracingShaderPass(_rayTracing, passName);
        }
    }
}
