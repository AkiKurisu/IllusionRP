using System;

namespace Illusion.Rendering
{
    internal interface IPathTracingInlineResourceTable
    {
        int Capacity { get; }

        void SetTexture(int slot, IntPtr resource, int dxgiFormat);

        void SetBuffer(int slot, IntPtr resource, uint byteSize);
    }

    internal static class PathTracingInlineBackend
    {
        private const string BackendTypeName =
            "Illusion.Rendering.RayTracedGlobalIllumination.UnityRHI.UnityRHIPathTracingInlineResourceTable, Illusion.RenderPipelines.RayTracedGlobalIllumination.UnityRHI";

        public static bool IsCompiled => Type.GetType(BackendTypeName, false) != null;

        public static IPathTracingInlineResourceTable Create()
        {
            Type type = Type.GetType(BackendTypeName, false);
            return type == null ? null : Activator.CreateInstance(type) as IPathTracingInlineResourceTable;
        }
    }
}
