using UnityEngine;

namespace Illusion.Rendering.PathTracing
{
    /// <summary>
    /// Joins the renderers below this transform into one subsurface scattering volume.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Illusion/Path Tracing Scattering Group")]
    public sealed class PathTracingScatteringGroup : MonoBehaviour
    {
    }
}
