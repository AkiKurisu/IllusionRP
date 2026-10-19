using UnityEngine;

namespace Illusion.Rendering.RayTracedGlobalIllumination.UnityRHI
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    internal static class UnityRHIRayTracedGIRegistration
    {
        static UnityRHIRayTracedGIRegistration() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            PathTracingInlineBackend.Register(() => new UnityRHIPathTracingInlineResourceTable());
            RayTracedGIDenoiserLoader.Register(() => new UnityRHIRayTracedGIDenoiser());
        }
    }
}
