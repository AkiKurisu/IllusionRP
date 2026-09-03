using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Percentage Closer Soft Shadows")]
    public class PercentageCloserSoftShadows : VolumeComponent
    {
        /// <summary>
        /// The angular diameter of the light source in degrees.
        /// </summary>
        [Tooltip("The angular diameter of the light source in degrees. Affects the penumbra size.")]
        public MinFloatParameter angularDiameter = new(1.5f, 0.01f);

        /// <summary>
        /// The angular diameter for blocker search in degrees.
        /// </summary>
        [Tooltip("The angular diameter for blocker search in degrees. Larger values search a wider area.")]
        public MinFloatParameter blockerSearchAngularDiameter = new(12.0f, 0.01f);

        /// <summary>
        /// The minimum filter max angular diameter in degrees.
        /// </summary>
        [Tooltip("The minimum filter max angular diameter in degrees.")]
        public MinFloatParameter minFilterMaxAngularDiameter = new(10.0f, 0.01f);

        /// <summary>
        /// Maximum penumbra size in world units.
        /// </summary>
        [Tooltip("Maximum penumbra size in world units.")]
        public ClampedFloatParameter maxPenumbraSize = new(0.56f, 0.0f, 10.0f);

        /// <summary>
        /// Maximum sampling distance for PCSS.
        /// </summary>
        [Tooltip("Maximum sampling distance for PCSS.")]
        public ClampedFloatParameter maxSamplingDistance = new(0.5f, 0.0f, 10.0f);

        /// <summary>
        /// Minimum filter size in texels.
        /// </summary>
        [Tooltip("Minimum filter size in texels.")]
        public ClampedFloatParameter minFilterSizeTexels = new(1.5f, 0.1f, 10.0f);
        
        /// <summary>
        /// Number of samples for blocker search in PCSS.
        /// </summary>
        [Header("Optimization")]
        [AdditionalProperty]
        [Tooltip("Number of samples for blocker search in PCSS. Higher values give better quality but lower performance.")]
        public ClampedIntParameter findBlockerSampleCount = new(24, 4, 64);

        /// <summary>
        /// Number of samples for PCF filtering in PCSS.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Number of samples for PCF filtering in PCSS. Higher values give smoother shadows but lower performance.")]
        public ClampedIntParameter pcfSampleCount = new(16, 4, 64);

        /// <summary>
        /// Scale factor for the penumbra mask texture.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Scale factor for the penumbra mask texture. Higher values use smaller textures (better performance, lower quality).")]
        public ClampedIntParameter penumbraMaskScale = new(4, 1, 32);

        /// <summary>
        /// Conservative dilation radius for the penumbra mask in mask texels.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Conservative dilation radius for the penumbra mask in mask texels. Larger values avoid PCSS edge artifacts at the cost of running PCSS on more pixels.")]
        public ClampedIntParameter penumbraMaskDilation = new(16, 0, 16);

        /// <summary>
        /// Minimum conservative dilation radius for distant penumbra mask pixels.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Minimum conservative dilation radius for distant penumbra mask pixels.")]
        public ClampedIntParameter penumbraMaskMinDilation = new(4, 0, 16);

        /// <summary>
        /// Camera-space distance where penumbra mask dilation starts fading down.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Camera-space distance where penumbra mask dilation starts fading down from the maximum value.")]
        public ClampedFloatParameter penumbraMaskDilationFadeStart = new(8.0f, 0.0f, 200.0f);

        /// <summary>
        /// Camera-space distance where penumbra mask dilation reaches the minimum value.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Camera-space distance where penumbra mask dilation reaches the minimum value.")]
        public ClampedFloatParameter penumbraMaskDilationFadeEnd = new(30.0f, 0.0f, 500.0f);

        /// <summary>
        /// Use the screen-space penumbra mask to skip PCSS work outside detected shadow edges.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Use the screen-space penumbra mask to skip PCSS work outside detected shadow edges. Disable this to match HDRP directional PCSS more closely.")]
        public BoolParameter usePenumbraMask = new(true);

        /// <summary>
        /// Enable temporal accumulation for screen-space PCSS shadows.
        /// </summary>
        [Header("Denoiser")]
        [AdditionalProperty]
        [Tooltip("Enable temporal accumulation for screen-space shadows.")]
        public BoolParameter shadowTemporalAccumulation = new(true);

        /// <summary>
        /// Enable spatial bilateral denoising after temporal accumulation.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Enable spatial bilateral denoising after temporal accumulation.")]
        public BoolParameter shadowSpatialDenoise = new(false);

        /// <summary>
        /// Bilateral denoiser filter radius for screen-space shadows.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Filter radius for spatial bilateral shadow denoiser.")]
        public ClampedIntParameter shadowDenoiseKernelSize = new(4, 1, 16);
    }
}
