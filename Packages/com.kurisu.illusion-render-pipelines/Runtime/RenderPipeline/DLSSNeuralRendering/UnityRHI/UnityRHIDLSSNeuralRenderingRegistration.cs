using UnityEngine;

namespace Illusion.Rendering.UnityRHI
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    internal static class UnityRHIDLSSNeuralRenderingRegistration
    {
        static UnityRHIDLSSNeuralRenderingRegistration() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            DLSSNeuralRenderingBackendLoader.Register(shader => new UnityRHIDLSSNeuralRenderingBackend(shader),
                UnityRHIDLSSNeuralRenderingBackend.GetStatus);
        }
    }
}
