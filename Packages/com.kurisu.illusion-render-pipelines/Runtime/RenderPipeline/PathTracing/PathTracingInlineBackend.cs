using System;

namespace Illusion.Rendering
{
    internal interface IPathTracingInlineResourceTable : IDisposable
    {
        int Capacity { get; }

        void SetTexture(int slot, IntPtr resource, int dxgiFormat);

        void SetBuffer(int slot, IntPtr resource, uint byteSize);

        void Clear(int slot);
    }

    internal static class PathTracingInlineBackend
    {
        private static Func<IPathTracingInlineResourceTable> _factory;

        internal static void Register(Func<IPathTracingInlineResourceTable> factory) => _factory = factory;

        public static bool IsCompiled => _factory != null;

        public static IPathTracingInlineResourceTable Create() => _factory?.Invoke();
    }
}
