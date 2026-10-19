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

    internal static class PathTracingReorderingKeyword
    {
        private const string Name = "ILLUSION_PT_SER";

        public static void Apply(CommandBuffer commandBuffer, RayTracingShader shader, IPathTracingReordering reordering)
        {
            var keyword = shader.keywordSpace.FindKeyword(Name);
            if (!keyword.isValid)
                return;
            commandBuffer.SetKeyword(shader, keyword, reordering != null);
            reordering?.Bind(commandBuffer, shader);
        }
    }
}
