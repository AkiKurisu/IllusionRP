using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing.UnityRHI
{
    internal sealed class UnityRHIPathTracingInlineDispatch : IPathTracingInlineDispatch
    {
        private readonly global::UnityRhi.InlineRayTracing _program;

        public UnityRHIPathTracingInlineDispatch(byte[] code, bool ser)
        {
            _program = new global::UnityRhi.InlineRayTracing(code, "InlineMain", 8, 4, ser);
        }

        public bool IsAvailable => _program.IsAvailable;
        public ulong GetDispatchCount(out int status) => _program.GetDispatchCount(out status);
        public void Bind(CommandBuffer commandBuffer) => _program.Bind(commandBuffer);
        public void Dispose() => _program.Dispose();
    }
}
