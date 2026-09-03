using System;
using System.Reflection;
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
        private const string BackendTypeName =
            "Illusion.Rendering.UnityRHI.UnityRHIDLSSNeuralRenderingBackend, Illusion.RenderPipelines.DLSSNeuralRendering.UnityRHI";

        internal static IDLSSNeuralRenderingBackend Create(Shader prepareInputsShader)
        {
            Type type = Type.GetType(BackendTypeName, false);
            return type == null
                ? null
                : Activator.CreateInstance(type, new object[] { prepareInputsShader }) as IDLSSNeuralRenderingBackend;
        }

        public static DLSSNeuralRenderingRuntimeStatus GetStatus()
        {
            Type type = Type.GetType(BackendTypeName, false);
            MethodInfo method = type?.GetMethod(nameof(GetStatus),
                BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, null) is DLSSNeuralRenderingRuntimeStatus status
                ? status
                : DLSSNeuralRenderingRuntimeStatus.BackendMissing;
        }
    }
}
