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
            PathTracingReorderingLoader.Register(() => new UnityRHIPathTracingReordering());
            PathTracingRayReconstructionLoader.Register(() => new UnityRHIPathTracingRayReconstruction());
        }
    }
}
