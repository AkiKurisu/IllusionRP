using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing.UnityRHI
{
    internal sealed class UnityRHIPathTracingInlineDispatch : IPathTracingInlineDispatch
    {
        private readonly global::UnityRhi.InlineRayTracing _program;

        public UnityRHIPathTracingInlineDispatch(PathTracingInlineProgram program)
        {
            _program = new global::UnityRhi.InlineRayTracing(program.RayGenSer.bytes, "InlineMain", 8, 4);
            try
            {
                foreach (var material in program.Materials)
                    _program.AddMaterial(material.Code.bytes, material.ClosestHit);
                _program.AddLibrary(program.Coverage.bytes);
            }
            catch { _program.Dispose(); throw; }
        }

        public bool IsAvailable => _program.IsAvailable;
        public ulong GetDispatchCount(out int status) => _program.GetDispatchCount(out status);
        public void Bind(CommandBuffer commandBuffer) => _program.Bind(commandBuffer);
        public void Dispose() => _program.Dispose();
    }
}
