using UnityEngine;

namespace Illusion.Rendering.PathTracing.UnityRHI
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    internal static class UnityRHIPathTracingRegistration
    {
        static UnityRHIPathTracingRegistration() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            PathTracingRayReconstructionLoader.Register(() => new UnityRHIPathTracingRayReconstruction());
            PathTracingInlineDispatchBackend.Register(program => new UnityRHIPathTracingInlineDispatch(program));
            PathTracingOpacityMicromapBackend.Register(() => new UnityRHIPathTracingOpacityMicromaps());
        }
    }
}
