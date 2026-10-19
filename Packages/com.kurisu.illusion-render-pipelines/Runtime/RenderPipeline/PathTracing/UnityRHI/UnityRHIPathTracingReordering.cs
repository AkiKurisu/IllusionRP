using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing.UnityRHI
{
    internal sealed class UnityRHIPathTracingReordering : IPathTracingReordering
    {
        private static readonly int ExtensionId = Shader.PropertyToID("g_NvidiaExt");
        private ComputeBuffer _extension;

        public UnityRHIPathTracingReordering()
        {
#if UNITY_EDITOR
            global::UnityRhi.RhiDomainReload.RegisterOwner(this);
#endif
        }

        public bool IsAvailable => global::UnityRhi.ShaderExecutionReordering.IsSupported;

        public void Bind(CommandBuffer commandBuffer, RayTracingShader shader)
        {
            if (_extension == null)
            {
                _extension = new ComputeBuffer(64, 256, ComputeBufferType.Counter);
                _extension.SetCounterValue(0);
            }
            commandBuffer.SetRayTracingBufferParam(shader, ExtensionId, _extension);
        }

        public void Dispose()
        {
            _extension?.Dispose();
            _extension = null;
        }
    }
}
