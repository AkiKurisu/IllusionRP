using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public enum PRTProbeAdjustmentMode
    {
        ApplyVirtualOffset,
        OverrideVirtualOffsetSettings,
        [InspectorName("Intensity Scale (Not Ready)")]
        IntensityScale,
        [InspectorName("Invalidate Probes (Not Ready)")]
        InvalidateProbes
    }

    public enum PRTProbeAdjustmentShape
    {
        Box,
        Sphere
    }

    [ExecuteAlways]
    [AddComponentMenu("Rendering/PRT Probe Adjustment Volume")]
    public class PRTProbeAdjustmentVolume : MonoBehaviour
    {
        [Tooltip("Shape of the adjustment volume")]
        public PRTProbeAdjustmentShape shape = PRTProbeAdjustmentShape.Box;

        [Tooltip("Size of the adjustment volume (Box only)")]
        public Vector3 size = Vector3.one;

        [Tooltip("Radius of the adjustment volume (Sphere only)")]
        public float radius = 1.0f;

        [Tooltip("How Unity overrides probes inside the Adjustment Volume")]
        public PRTProbeAdjustmentMode mode = PRTProbeAdjustmentMode.ApplyVirtualOffset;

        [Tooltip("Rotation angle for the Virtual Offset vector")]
        [Range(0f, 360f)]
        public float virtualOffsetRotation;

        [Tooltip("Distance to pushe probes along the Virtual Offset Rotation vector")]
        [Range(0f, 10f)]
        public float virtualOffsetDistance = 1f;
        
        [Tooltip("How far to push a probe's capture point out of geometry")]
        [Range(0f, 1f)]
        public float geometryBias = 0.1f;

        [Tooltip("Distance between a probe's center and the point URP uses for sampling ray origin")]
        [Range(0f, 1f)]
        public float rayOriginBias = 0.1f;

        [Tooltip("Change the brightness of all probes covered by this volume")]
        [Range(0f, 5f)]
        public float intensityScale = 1f;

#if UNITY_EDITOR
        [Tooltip("Show volume bounds in scene view")]
        [SerializeField]
        internal bool showVolumeBounds = true;
#endif

        private void OnEnable()
        {
            PRTVolumeManager.RegisterAdjustmentVolume(this);
        }

        private void OnDisable()
        {
            PRTVolumeManager.UnregisterAdjustmentVolume(this);
        }

        private void OnDestroy()
        {
            PRTVolumeManager.UnregisterAdjustmentVolume(this);
        }

        /// <summary>
        /// Check if a point is inside this adjustment volume
        /// </summary>
        /// <param name="point">World position to test</param>
        /// <returns>True if point is inside the volume</returns>
        public bool Contains(Vector3 point)
        {
            Vector3 localPoint = transform.InverseTransformPoint(point);

            if (shape == PRTProbeAdjustmentShape.Box)
            {
                return Mathf.Abs(localPoint.x) <= size.x * 0.5f &&
                       Mathf.Abs(localPoint.y) <= size.y * 0.5f &&
                       Mathf.Abs(localPoint.z) <= size.z * 0.5f;
            }

            // Sphere
            return localPoint.magnitude <= radius;
        }

        /// <summary>
        /// Calculate virtual offset for a probe at the given position
        /// </summary>
        /// <returns>Virtual offset vector for this probe</returns>
        public Vector3 GetAdditionalVirtualOffset()
        {
            if (mode != PRTProbeAdjustmentMode.ApplyVirtualOffset)
                return Vector3.zero;

            // Calculate offset direction based on rotation
            Vector3 offsetDirection = Quaternion.AngleAxis(virtualOffsetRotation, Vector3.up) * Vector3.forward;

            // Apply distance
            return offsetDirection * virtualOffsetDistance;
        }

        /// <summary>
        /// Get intensity scale for a probe at the given position
        /// </summary>
        /// <returns>Intensity scale multiplier</returns>
        public float GetIntensityScale()
        {
            if (mode != PRTProbeAdjustmentMode.IntensityScale)
                return 1f;

            return intensityScale;
        }

        /// <summary>
        /// Check if a probe should be invalidated
        /// </summary>
        /// <returns>True if probe should be invalidated</returns>
        public bool ShouldInvalidateProbe()
        {
            return mode == PRTProbeAdjustmentMode.InvalidateProbes;
        }
    }
}
