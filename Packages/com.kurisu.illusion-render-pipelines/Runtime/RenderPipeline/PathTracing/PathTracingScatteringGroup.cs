using UnityEngine;

namespace Illusion.Rendering.PathTracing
{
    /// <summary>
    /// Joins the renderers below this transform into one subsurface scattering volume.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Illusion/Path Tracing Scattering Group")]
    public sealed class PathTracingScatteringGroup : MonoBehaviour
    {
        internal static int Version { get; private set; }

        private void OnEnable() => Version++;

        private void OnDisable() => Version++;
    }
}
