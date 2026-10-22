using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal readonly struct PathTracingShaderTarget
    {
        private readonly ComputeShader _compute;
        private readonly int _kernel;

        public PathTracingShaderTarget(ComputeShader compute, int kernel)
        {
            _compute = compute;
            _kernel = kernel;
        }

        public void SetTexture(CommandBuffer cmd, int id, RenderTargetIdentifier texture) => cmd.SetComputeTextureParam(_compute, _kernel, id, texture);
        public void SetBuffer(CommandBuffer cmd, int id, GraphicsBuffer buffer) => cmd.SetComputeBufferParam(_compute, _kernel, id, buffer);
        public void SetFloat(CommandBuffer cmd, int id, float value) => cmd.SetComputeFloatParam(_compute, id, value);
        public void SetInt(CommandBuffer cmd, int id, int value) => cmd.SetComputeIntParam(_compute, id, value);
        public void SetVector(CommandBuffer cmd, int id, Vector4 value) => cmd.SetComputeVectorParam(_compute, id, value);
        public void SetAccelerationStructure(CommandBuffer cmd, int id, RayTracingAccelerationStructure accelerationStructure) =>
            cmd.SetRayTracingAccelerationStructure(_compute, _kernel, id, accelerationStructure);
    }
}
