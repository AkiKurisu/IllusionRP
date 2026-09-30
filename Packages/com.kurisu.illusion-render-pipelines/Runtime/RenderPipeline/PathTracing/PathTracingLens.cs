using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PathTracing
{
    internal readonly struct PathTracingLens
    {
        public readonly float ApertureRadius;

        public readonly float FocusDistance;

        private PathTracingLens(float apertureRadius, float focusDistance)
        {
            ApertureRadius = apertureRadius;
            FocusDistance = focusDistance;
        }

        public bool IsThinLens => ApertureRadius > 0.0f;

        public static PathTracingLens From(Camera camera, bool postProcessEnabled, VolumeStack stack)
        {
            var depthOfField = stack.GetComponent<DepthOfField>();
            if (!postProcessEnabled || camera.cameraType == CameraType.SceneView || depthOfField == null
                || !depthOfField.IsActive() || depthOfField.mode.value != DepthOfFieldMode.Bokeh)
                return default;
            float apertureRadius = 0.5f * 0.001f * depthOfField.focalLength.value / depthOfField.aperture.value;
            return new PathTracingLens(apertureRadius, depthOfField.focusDistance.value);
        }
    }
}
