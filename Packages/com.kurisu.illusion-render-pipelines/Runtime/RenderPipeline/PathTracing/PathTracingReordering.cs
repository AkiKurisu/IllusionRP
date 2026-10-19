using System;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal interface IPathTracingReordering : IDisposable
    {
        bool IsAvailable { get; }
        void Bind(CommandBuffer commandBuffer, RayTracingShader shader);
    }

    internal static class PathTracingReorderingLoader
    {
        public static IPathTracingReordering Create()
        {
            Type type = Type.GetType("Illusion.Rendering.PathTracing.UnityRHI.UnityRHIPathTracingReordering, Illusion.RenderPipelines.PathTracing.UnityRHI", false);
            return type == null ? null : Activator.CreateInstance(type) as IPathTracingReordering;
        }
    }
}
