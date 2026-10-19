using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// How secondary surfaces are lit.
    /// </summary>
    public enum RayTracedGIMode
    {
        Performance,
        Quality
    }

    /// <summary>
    /// How ReSTIR GI reuses samples across frames and neighboring pixels.
    /// </summary>
    public enum RayTracedGIResamplingMode
    {
        None,
        Temporal,
        Spatial,
        TemporalAndSpatial
    }

    /// <summary>
    /// How resampling corrects the bias of reusing samples from other pixels and frames.
    /// </summary>
    public enum RayTracedGIBiasCorrection
    {
        Off = 0,
        Basic = 1,
        [InspectorName("Ray Traced")]
        RayTraced = 3
    }

    /// <summary>
    /// The NRD denoiser of the indirect lighting signals, or none.
    /// </summary>
    public enum RayTracedGIDenoiser
    {
        None,
        [InspectorName("RELAX")]
        Relax,
        [InspectorName("REBLUR")]
        Reblur
    }

    public enum RayTracedGIDebugView
    {
        None,
        HashGrid,
        CacheOccupancy,
        HashCollisions
    }

    [Serializable]
    public sealed class RayTracedGIModeParameter : VolumeParameter<RayTracedGIMode>
    {
        public RayTracedGIModeParameter(RayTracedGIMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class RayTracedGIDenoiserParameter : VolumeParameter<RayTracedGIDenoiser>
    {
        public RayTracedGIDenoiserParameter(RayTracedGIDenoiser value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class RayTracedGIResamplingModeParameter : VolumeParameter<RayTracedGIResamplingMode>
    {
        public RayTracedGIResamplingModeParameter(RayTracedGIResamplingMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class RayTracedGIBiasCorrectionParameter : VolumeParameter<RayTracedGIBiasCorrection>
    {
        public RayTracedGIBiasCorrectionParameter(RayTracedGIBiasCorrection value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>
    /// Ray traced global illumination for raster cameras. The scene participation and directional light size follow the
    /// Path Tracing Volume.
    /// </summary>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Ray Traced Global Illumination")]
    public sealed class RayTracedGlobalIllumination : VolumeComponent
    {
        /// <summary>
        /// Enable or disable Ray Traced Global Illumination.
        /// </summary>
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        [Tooltip("Performance lights secondary surfaces with one light sample, for one bounce. Quality continues their paths into SHaRC's world-space radiance cache, for multiple bounces; it needs 160 MiB per camera, native 16-bit shader types and 64-bit buffer atomics.")]
        public RayTracedGIModeParameter mode = new(RayTracedGIMode.Performance);

        [Tooltip("Reorder Quality mode rays by their hits on supported NVIDIA GPUs. Unsupported devices use the original tracing path.")]
        public BoolParameter shaderExecutionReordering = new(true);

        [Tooltip("Trace, resample and denoise at half resolution, then upsample with the depth.")]
        public BoolParameter halfResolution = new(true, BoolParameter.DisplayType.EnumPopup);

        [Tooltip("While a denoiser runs, trace and resample half of the GI pixels each frame in a checkerboard, and let the denoiser reconstruct the other half.")]
        public BoolParameter checkerboard = new(true, BoolParameter.DisplayType.EnumPopup);

        [Tooltip("Also produce indirect specular, which fills what screen space reflections miss before reflection probes do.")]
        public BoolParameter specular = new(true, BoolParameter.DisplayType.EnumPopup);

        [Tooltip("How samples are reused across frames and neighboring pixels.")]
        public RayTracedGIResamplingModeParameter resamplingMode = new(RayTracedGIResamplingMode.TemporalAndSpatial);

        [Tooltip("Bias correction of temporal resampling.")]
        public RayTracedGIBiasCorrectionParameter temporalBiasCorrection = new(RayTracedGIBiasCorrection.RayTraced);

        [Tooltip("The number of frames a reservoir accumulates before older samples lose weight.")]
        public ClampedIntParameter maxHistoryLength = new(8, 1, 64);

        [Tooltip("The most frames a pixel's resampling history lasts before it restarts from fresh samples.")]
        public ClampedIntParameter maxReservoirAge = new(60, 1, 255);

        [Tooltip("Bias correction of spatial resampling.")]
        public RayTracedGIBiasCorrectionParameter spatialBiasCorrection = new(RayTracedGIBiasCorrection.RayTraced);

        [Tooltip("The number of neighbors spatial resampling draws per pixel.")]
        public ClampedIntParameter spatialSamples = new(2, 1, 32);

        [Tooltip("The radius in pixels within which spatial resampling draws neighbors.")]
        public ClampedFloatParameter spatialRadius = new(32.0f, 1.0f, 64.0f);

        [Tooltip("Maximum luminance of a secondary sample, relative to the radiance that exposure maps to middle gray. 0 keeps the estimate unbiased but lets rare caustic paths spread through resampling.")]
        public MinFloatParameter radianceClamp = new(100.0f, 0.0f);

        [Tooltip("The NRD denoiser of the indirect lighting. It needs the UnityRHI package and Direct3D 12.")]
        public RayTracedGIDenoiserParameter denoiser = new(RayTracedGIDenoiser.Relax);
    }
}
