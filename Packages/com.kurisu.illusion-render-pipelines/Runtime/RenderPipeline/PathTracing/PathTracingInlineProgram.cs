using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    [Serializable]
    public sealed class PathTracingInlineProgram
    {
        public ComputeShader Compute;
        public TextAsset RayGen;
        public TextAsset RayGenSer;
    }

    internal interface IPathTracingInlineDispatch : IDisposable
    {
        bool IsAvailable { get; }
        ulong GetDispatchCount(out int status);
        void Bind(CommandBuffer commandBuffer);
    }

    internal static class PathTracingInlineDispatchBackend
    {
        internal static Func<byte[], bool, IPathTracingInlineDispatch> Factory { get; private set; }
        internal static void Register(Func<byte[], bool, IPathTracingInlineDispatch> factory) => Factory = factory;
    }

    internal sealed class PathTracingInlineDispatcher : IDisposable
    {
        private PathTracingInlineProgram _program;
        private IPathTracingInlineDispatch _plain;
        private IPathTracingInlineDispatch _ser;
        private GraphicsBuffer _extension;
        private static readonly int ExtensionId = Shader.PropertyToID("_PathTracingInlineExtension");

        public bool SetProgram(PathTracingInlineProgram program)
        {
            if (_program != program)
            {
                Dispose();
                _program = program;
            }
            return program?.Compute && program.Compute.IsSupported(0);
        }

        public PathTracingShaderTarget Target => new(_program.Compute, 0);

        public ulong GetDispatchCount(bool ser, out int status)
        {
            status = 0;
            var backend = ser ? _ser : _plain;
            return backend != null ? backend.GetDispatchCount(out status) : 0;
        }

        public void Dispatch(CommandBuffer cmd, int width, int height, bool ser)
        {
            var factory = PathTracingInlineDispatchBackend.Factory;
            if (factory != null)
            {
                if (_plain == null && _program.RayGen)
                    _plain = factory(_program.RayGen.bytes, false);
                if (ser && _ser == null && _program.RayGenSer)
                    _ser = factory(_program.RayGenSer.bytes, true);
            }
            var backend = ser && _ser is { IsAvailable: true } ? _ser : _plain;
            _extension ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 256);
            cmd.SetComputeBufferParam(_program.Compute, 0, ExtensionId, _extension);
            backend?.Bind(cmd);
            cmd.DispatchCompute(_program.Compute, 0, (width + 7) / 8, (height + 3) / 4, 1);
        }

        public void Dispose()
        {
            _plain?.Dispose();
            _ser?.Dispose();
            _plain = _ser = null;
            _extension?.Dispose();
            _extension = null;
            _program = null;
        }
    }
}
