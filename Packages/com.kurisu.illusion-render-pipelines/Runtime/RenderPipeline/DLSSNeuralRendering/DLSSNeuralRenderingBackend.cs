using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public readonly struct DLSSNeuralRenderingRuntimeStatus
    {
        public readonly bool BackendInstalled;
        public readonly bool D3D12Active;
        public readonly bool RuntimeAvailable;
        public readonly int InitResult;
        public readonly int LastCreateResult;
        public readonly int LastEvaluateResult;

        public DLSSNeuralRenderingRuntimeStatus(bool backendInstalled, bool d3D12Active, bool runtimeAvailable,
            int initResult, int lastCreateResult, int lastEvaluateResult)
        {
            BackendInstalled = backendInstalled;
            D3D12Active = d3D12Active;
            RuntimeAvailable = runtimeAvailable;
            InitResult = initResult;
            LastCreateResult = lastCreateResult;
            LastEvaluateResult = lastEvaluateResult;
        }

        public static DLSSNeuralRenderingRuntimeStatus BackendMissing => new(false, false, false, 0, 0, 0);
    }

    internal interface IDLSSNeuralRenderingBackend : IDisposable
    {
        bool IsAvailable { get; }
        void Enqueue(ScriptableRenderer renderer, ref RenderingData renderingData);
        void ResetHistory();
    }

    public static class DLSSNeuralRenderingBackendLoader
    {
        private static Func<Shader, IDLSSNeuralRenderingBackend> _factory;
        private static Func<DLSSNeuralRenderingRuntimeStatus> _getStatus;

        internal static void Register(Func<Shader, IDLSSNeuralRenderingBackend> factory,
            Func<DLSSNeuralRenderingRuntimeStatus> getStatus)
        {
            _factory = factory;
            _getStatus = getStatus;
        }

        internal static IDLSSNeuralRenderingBackend Create(Shader prepareInputsShader) => _factory?.Invoke(prepareInputsShader);

        public static DLSSNeuralRenderingRuntimeStatus GetStatus() =>
            _getStatus?.Invoke() ?? DLSSNeuralRenderingRuntimeStatus.BackendMissing;
    }
}
