using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    // Enums
    internal enum AmbientOcclusionDepthSource
    {
        Depth = 0,
        DepthNormals = 1
    }

    internal enum AmbientOcclusionNormalQuality
    {
        Low,
        Medium,
        High
    }
    
    internal enum AmbientOcclusionBlurQuality
    {
        // CS
        Spatial,
        // PS
        Bilateral,
        Gaussian
    }

    [Serializable]
    internal sealed class AmbientOcclusionDepthSourceParameter : VolumeParameter<AmbientOcclusionDepthSource>
    {
        public AmbientOcclusionDepthSourceParameter(AmbientOcclusionDepthSource value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    internal sealed class AmbientOcclusionNormalQualityParameter : VolumeParameter<AmbientOcclusionNormalQuality>
    {
        public AmbientOcclusionNormalQualityParameter(AmbientOcclusionNormalQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }
    
    [Serializable]
    internal sealed class AmbientOcclusionBlurQualityParameter : VolumeParameter<AmbientOcclusionBlurQuality>
    {
        public AmbientOcclusionBlurQualityParameter(AmbientOcclusionBlurQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenuForRenderPipeline("Illusion/Ground Truth Ambient Occlusion", typeof(UniversalRenderPipeline))]
    public class GroundTruthAmbientOcclusion : VolumeComponent
    {
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        [Tooltip("Compute the Occlusion and Blur at half of the resolution")]
        public BoolParameter downSample = new(true, BoolParameter.DisplayType.EnumPopup);

        // ============================ Prefer Depth Normal ================================== //
        // ReSharper disable once InconsistentNaming
        // [SerializeField]
        internal AmbientOcclusionDepthSourceParameter source = new(AmbientOcclusionDepthSource.DepthNormals);
        
        // ReSharper disable once InconsistentNaming
        // [SerializeField]
        internal AmbientOcclusionNormalQualityParameter normalSamples = new(AmbientOcclusionNormalQuality.Medium);
        // ============================ Prefer Depth Normal ================================== //

        [SerializeField] 
        internal AmbientOcclusionBlurQualityParameter blurQuality = new(AmbientOcclusionBlurQuality.Spatial);

        /// <summary>
        /// Controls the strength of the ambient occlusion effect. Increase this value to produce darker areas.
        /// </summary>
        public ClampedFloatParameter intensity = new(1.0f, 0.0f, 4.0f);

        /// <summary>
        /// Controls how much the ambient occlusion affects direct lighting.
        /// </summary>
        public ClampedFloatParameter directLightingStrength = new(0.25f, 0.0f, 1.0f);

        /// <summary>
        /// Sampling radius. Bigger the radius, wider AO will be achieved,
        /// risking losing fine details and increasing cost of the effect due to increasing cache misses.
        /// </summary>
        public ClampedFloatParameter radius = new(2.0f, 0.25f, 5.0f);

        /// <summary>
        /// A heuristic to bias occlusion for thin or thick objects.
        /// </summary>
        public ClampedFloatParameter thickness = new(0.5f, 0.001f, 1.0f);
        
        /// <summary>
        /// Moving this factor closer to 0 will increase the amount of accepted samples during temporal accumulation, increasing the ghosting, but reducing the temporal noise.
        /// </summary>
        public ClampedFloatParameter spatialBilateralAggressiveness = new(0.15f, 0.0f, 1.0f);
        
        // Non-temporal only parameters
        /// <summary>
        /// Modify the non-temporal blur to change how sharp features are preserved. Lower values leads to blurrier/softer results, higher values gets a sharper result, but with the risk of noise.
        /// </summary>
        public ClampedFloatParameter blurSharpness = new(0.1f, 0.0f, 1.0f);
        
        // =================================== Not Ready ===================================== //
        // ReSharper disable once InconsistentNaming
        // Temporal only parameters
        /// <summary>
        /// Moving this factor closer to 0 will increase the amount of accepted samples during temporal accumulation, increasing the ghosting, but reducing the temporal noise.
        /// </summary>
        internal ClampedFloatParameter ghostingReduction = new(0.5f, 0.0f, 1.0f);
        // =================================== Not Ready ===================================== //

        /// <summary>
        /// Number of steps to take along one signed direction during horizon search
        /// (this is the number of steps in positive and negative direction).
        /// Increasing the value can lead to detection
        /// of finer details, but is not a guarantee of higher quality otherwise.
        /// Also note that increasing this value will lead to higher cost.
        /// </summary>
        [Header("Performance")]
        [AdditionalProperty]
        public ClampedIntParameter stepCount = new(6, 2, 32);

        /// <summary>
        /// This field imposes a maximum radius in pixels that will be considered.
        /// It is very important to keep this as tight as possible to preserve good performance.
        /// Note that the pixel value specified for this field is the value used for 1080p
        /// when *not* running the effect at full resolution, it will be scaled accordingly
        /// for other resolutions.
        /// </summary>
        [AdditionalProperty]
        public ClampedIntParameter maximumRadiusInPixels = new(40, 16, 256);

        /// <summary>
        /// Number of directions searched for occlusion at each pixel when temporal accumulation is disabled.
        /// </summary>
        [AdditionalProperty]
        public ClampedIntParameter directionCount = new(2, 1, 6);
    }
}
