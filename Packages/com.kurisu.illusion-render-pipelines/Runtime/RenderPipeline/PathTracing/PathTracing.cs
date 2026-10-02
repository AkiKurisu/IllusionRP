using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Illusion.Rendering.PathTracing
{
    public enum PathTracingMode
    {
        Reference = 0,

        Realtime = 1,
    }

    [Serializable]
    public sealed class PathTracingModeParameter : VolumeParameter<PathTracingMode>
    {
        public PathTracingModeParameter(PathTracingMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    public enum PathTracingRayReconstructionQuality
    {
        NativeAA = 0,

        Quality = 1,

        Balanced = 2,

        Performance = 3,

        UltraPerformance = 4,
    }

    [Serializable]
    public sealed class PathTracingRayReconstructionQualityParameter : VolumeParameter<PathTracingRayReconstructionQuality>
    {
        public PathTracingRayReconstructionQualityParameter(PathTracingRayReconstructionQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Path Tracing")]
    [DisplayInfo(name = "Path Tracing")]
    public sealed class PathTracing : VolumeComponent
    {
        [DisplayInfo(name = "State")]
        [Tooltip("Render the camera image with path tracing.")]
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        public PathTracingModeParameter mode = new(PathTracingMode.Reference);

        [Tooltip("Layers whose renderers take part in path tracing.")]
        public LayerMaskParameter layerMask = new(-1);

        [Tooltip("Samples accumulated before a still image stops refining.")]
        public ClampedIntParameter maximumSamples = new(256, 1, 65536);

        [Tooltip("Samples traced per frame in Realtime mode.")]
        public ClampedIntParameter realtimeSamplesPerPixel = new(1, 1, 8);

        [Tooltip("Reconstruct Realtime frames with DLSS Ray Reconstruction; off shows the raw path traced frame.")]
        public BoolParameter rayReconstruction = new(true);

        [Tooltip("Resolution Realtime frames are traced at before Ray Reconstruction upscales them; lower traces fewer rays.")]
        public PathTracingRayReconstructionQualityParameter rayReconstructionQuality = new(PathTracingRayReconstructionQuality.Balanced);

        [Tooltip("Maximum number of surface bounces of a path.")]
        public ClampedIntParameter bounceCount = new(16, 1, 96);

        [Tooltip("Maximum number of bounces off rough surfaces.")]
        public ClampedIntParameter diffuseBounceCount = new(2, 0, 96);

        [Tooltip("Sample analytic lights, emissive triangles and the environment directly.")]
        public BoolParameter lightSampling = new(true);

        [Tooltip("Angular diameter of directional lights in degrees; the sun is about 0.5.")]
        public ClampedFloatParameter directionalAngularDiameter = new(0.5f, 0.0f, 10.0f);

        [AdditionalProperty, FormerlySerializedAs("fireflyFilterThreshold")]
        [Tooltip("Reference firefly filter threshold, relative to the radiance that exposure maps to middle gray as in RTXPT: it clamps rare, very bright paths such as caustics off mirrors onto small lights. 0 keeps the result unbiased.")]
        public MinFloatParameter referenceFireflyFilterThreshold = new(5f, 0f);

        [AdditionalProperty]
        [Tooltip("Realtime firefly filter threshold, relative to the radiance that exposure maps to middle gray as in RTXPT. 0 disables the filter.")]
        public MinFloatParameter realtimeFireflyFilterThreshold = new(0.1f, 0f);

        [AdditionalProperty]
        [Tooltip("Environment MIP bias after the first diffuse bounce; 0 keeps the result unbiased.")]
        public ClampedFloatParameter environmentDiffuseMipOffset = new(0f, 0f, 6f);

        public bool IsActive() => active && enable.value;
    }
}
