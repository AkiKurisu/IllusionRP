using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    [Serializable]
    public sealed class PathTracingInlineProgram
    {
        public ComputeShader Compute;
        public TextAsset RayGenSer;
        public MaterialModule[] Materials = Array.Empty<MaterialModule>();

        [Serializable]
        public sealed class MaterialModule
        {
            public TextAsset Code;
            public string ClosestHit;
            public string Candidate;
        }
    }

    internal interface IPathTracingInlineDispatch : IDisposable
    {
        bool IsAvailable { get; }
        ulong GetDispatchCount(out int status);
        void Bind(CommandBuffer commandBuffer);
    }

    internal static class PathTracingInlineDispatchBackend
    {
        internal static Func<byte[], PathTracingInlineProgram.MaterialModule[], IPathTracingInlineDispatch> Factory { get; private set; }
        internal static void Register(Func<byte[], PathTracingInlineProgram.MaterialModule[], IPathTracingInlineDispatch> factory) => Factory = factory;
    }

    internal sealed class PathTracingInlineDispatcher : IDisposable
    {
        private PathTracingInlineProgram _program;
        private IPathTracingInlineDispatch _backend;
        private Exception _failure;
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
            if (program.Materials.Length == 0)
                throw new InvalidOperationException("Inline material modules are missing. Regenerate inline programs.");
        }

        public PathTracingShaderTarget Target => new(_program.Compute, 0);

        public ulong GetDispatchCount(out int status)
        {
            status = 1;
            return _backend != null ? _backend.GetDispatchCount(out status) : 0;
        }

        public void Dispatch(CommandBuffer cmd, int width, int height)
        {
            var backend = Prepare();
            _extension ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 256);
            cmd.SetComputeBufferParam(_program.Compute, 0, ExtensionId, _extension);
            backend.Bind(cmd);
            cmd.DispatchCompute(_program.Compute, 0, (width + 7) / 8, (height + 3) / 4, 1);
        }

        public IPathTracingInlineDispatch Prepare()
        {
            var factory = PathTracingInlineDispatchBackend.Factory;
            string name = _program?.Compute ? _program.Compute.name : "unassigned";
            if (factory == null) throw new InvalidOperationException($"Inline RayGen '{name}' requires the UnityRHI backend.");
            if (_backend == null)
            {
                if (_failure != null) throw _failure;
                var code = _program?.RayGenSer;
                if (!code || code.bytes.Length == 0)
                    throw new InvalidOperationException($"Inline RayGen '{name}' bytecode is missing.");
                try { _backend = factory(code.bytes, _program.Materials); }
                catch (Exception e)
                {
                    _failure = new InvalidOperationException($"Inline RayGen '{name}' initialization failed.", e);
                    throw _failure;
                }
            }
            if (_backend is not { IsAvailable: true })
                throw new InvalidOperationException($"Inline RayGen '{name}' is unavailable.");
            _backend.GetDispatchCount(out int status);
            if (status < 0) throw new InvalidOperationException($"Inline RayGen '{name}' dispatch failed (0x{status:X8}).");
            return _backend;
        }

        public void Dispose()
        {
            _backend?.Dispose();
            _backend = null;
            _failure = null;
            _extension?.Dispose();
            _extension = null;
            _program = null;
        }
    }
}
