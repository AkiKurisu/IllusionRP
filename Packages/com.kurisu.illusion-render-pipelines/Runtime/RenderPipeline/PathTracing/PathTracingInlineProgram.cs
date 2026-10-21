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
        private Exception _plainFailure;
        private Exception _serFailure;
        private GraphicsBuffer _extension;
        private static readonly int ExtensionId = Shader.PropertyToID("_PathTracingInlineExtension");

        public void SetProgram(PathTracingInlineProgram program)
        {
            if (_program != program)
            {
                Dispose();
                _program = program;
            }
            if (program?.Compute == null || !program.Compute.IsSupported(0))
                throw new InvalidOperationException("Inline RayGen binding carrier is missing or unsupported. Regenerate inline programs.");
        }

        public PathTracingShaderTarget Target => new(_program.Compute, 0);

        public ulong GetDispatchCount(bool ser, out int status)
        {
            status = 1;
            var backend = ser ? _ser : _plain;
            return backend != null ? backend.GetDispatchCount(out status) : 0;
        }

        public void Dispatch(CommandBuffer cmd, int width, int height, bool ser)
        {
            var backend = Prepare(ser);
            _extension ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 256);
            cmd.SetComputeBufferParam(_program.Compute, 0, ExtensionId, _extension);
            backend.Bind(cmd);
            cmd.DispatchCompute(_program.Compute, 0, (width + 7) / 8, (height + 3) / 4, 1);
        }

        public IPathTracingInlineDispatch Prepare(bool ser)
        {
            var factory = PathTracingInlineDispatchBackend.Factory;
            string name = _program?.Compute ? _program.Compute.name : "unassigned";
            if (factory == null) throw new InvalidOperationException($"Inline RayGen '{name}' requires the UnityRHI backend.");
            var backend = ser ? _ser : _plain;
            if (backend == null)
            {
                var failure = ser ? _serFailure : _plainFailure;
                if (failure != null) throw failure;
                var code = ser ? _program?.RayGenSer : _program?.RayGen;
                if (!code || code.bytes.Length == 0)
                    throw new InvalidOperationException($"Inline RayGen '{name}' bytecode is missing (SER={ser}).");
                try { backend = factory(code.bytes, ser); }
                catch (Exception e)
                {
                    failure = new InvalidOperationException($"Inline RayGen '{name}' initialization failed (SER={ser}).", e);
                    if (ser) _serFailure = failure; else _plainFailure = failure;
                    throw failure;
                }
                if (ser) _ser = backend; else _plain = backend;
            }
            if (backend is not { IsAvailable: true })
                throw new InvalidOperationException($"Inline RayGen '{name}' is unavailable (SER={ser}).");
            backend.GetDispatchCount(out int status);
            if (status < 0) throw new InvalidOperationException($"Inline RayGen '{name}' dispatch failed (SER={ser}, 0x{status:X8}).");
            return backend;
        }

        public void Dispose()
        {
            _plain?.Dispose();
            _ser?.Dispose();
            _plain = _ser = null;
            _plainFailure = _serFailure = null;
            _extension?.Dispose();
            _extension = null;
            _program = null;
        }
    }
}
