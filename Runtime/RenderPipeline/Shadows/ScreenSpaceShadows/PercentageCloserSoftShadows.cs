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
    }
}

