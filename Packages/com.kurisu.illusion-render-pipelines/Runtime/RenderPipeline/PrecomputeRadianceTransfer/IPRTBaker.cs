using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    /// <summary>
    /// PRT bake cubemap resolution
    /// </summary>
    public enum PRTBakeResolution
    {
        [InspectorName("128 * 128")]
        _128 = 128,
        [InspectorName("256 * 256")]
        _256 = 256,
        [InspectorName("512 * 512")]
        _512 = 512
    }

#if UNITY_EDITOR
    /// <summary>
    /// PRT bake interface
    /// </summary>
    internal interface IPRTBaker
    {
        /// <summary>
        /// Update baking progress.
        /// </summary>
        /// <param name="status">Status message</param>
        /// <param name="progress">Progress value</param>
        void UpdateProgress(string status, float progress);

        /// <summary>
        /// Bake surfel data at the specified position.
        /// </summary>
        /// <param name="probePosition"></param>
        Surfel[] BakeSurfelData(Vector3 probePosition);
    }
#endif
}