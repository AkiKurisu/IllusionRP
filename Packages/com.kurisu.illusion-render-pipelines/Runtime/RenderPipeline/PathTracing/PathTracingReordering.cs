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
        private static Func<IPathTracingReordering> _factory;

        internal static void Register(Func<IPathTracingReordering> factory) => _factory = factory;

        public static IPathTracingReordering Create() => _factory?.Invoke();
    }
}
