using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Screen Space Reflection Mode
    /// </summary>
    public enum ScreenSpaceReflectionMode
    {
        LinearVS = 0,
        LinearSS = 1,
        HizSS = 2
    }
    
    /// <summary>
    /// Screen Space Reflection Algorithm
    /// </summary>
    public enum ScreenSpaceReflectionAlgorithm
    {
        /// <summary>Legacy SSR approximation.</summary>
        Approximation,
        /// <summary>Screen Space Reflection, Physically Based with Accumulation through multiple frame.</summary>
        PBRAccumulation
    }

    [Serializable]
    public sealed class ScreenSpaceReflectionModeParameter : VolumeParameter<ScreenSpaceReflectionMode>
    {
        public ScreenSpaceReflectionModeParameter(ScreenSpaceReflectionMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }
    
    [Serializable]
    public sealed class ScreenSpaceReflectionAlgoParameter : VolumeParameter<ScreenSpaceReflectionAlgorithm>
    {
        public ScreenSpaceReflectionAlgoParameter(ScreenSpaceReflectionAlgorithm value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
#if UNITY_2023_1_OR_NEWER
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Screen Space Reflection")]
#else
    [VolumeComponentMenuForRenderPipeline("Illusion/Screen Space Reflection", typeof(UniversalRenderPipeline))]
#endif
    public class ScreenSpaceReflection : VolumeComponent
    {
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        public ScreenSpaceReflectionModeParameter mode = new(ScreenSpaceReflectionMode.HizSS);

        /// <summary>
        /// Screen Space Reflections Algorithm used.
        /// </summary>
        public ScreenSpaceReflectionAlgoParameter usedAlgorithm = new(ScreenSpaceReflectionAlgorithm.Approximation);
        
        // Half resolution need additional sampler or use upsampling and have much more artifacts, so we hide it now.
        internal readonly BoolParameter DownSample = new(false, BoolParameter.DisplayType.EnumPopup);
        
        public ClampedFloatParameter intensity = new(1f, 0.01f, 2f);

        /// <summary>
        /// Controls the distance at which URP fades out SSR near the edge of the screen.
        /// </summary>
        public ClampedFloatParameter thickness = new(0.01f, 0.0f, 1f);

        public ClampedFloatParameter minSmoothness = new(0.25f, 0.01f, 1f);

        /// <summary>
        /// Controls the smoothness value at which the smoothness-controlled fade out starts.
        /// The fade is in the range [Min Smoothness, Smoothness Fade Start]
        /// </summary>
        public ClampedFloatParameter smoothnessFadeStart = new(0.9f, 0.0f, 1.0f);

        /// <summary>
        /// Controls the typical thickness of objects the reflection rays may pass behind.
        /// </summary>
        public ClampedFloatParameter screenFadeDistance = new(0.1f, 0.0f, 1.0f);
        
        /// <summary>
        /// Controls the amount of accumulation (0 no accumulation, 1 just accumulate)
        /// </summary>
        [Header("Accumulation")]
        public ClampedFloatParameter accumulationFactor = new(0.75f, 0.0f, 1.0f);
        
        /// <summary>
        /// For PBR: Controls the bias of accumulation (0 no bias, 1 bias ssr)
        /// </summary>
        public ClampedFloatParameter biasFactor = new(0.5f, 0.0f, 1.0f);
        
        /// <summary>
        /// Controls the likelihood history will be rejected based on the previous frame motion vectors of both the surface and the hit object in world space.
        /// </summary>
        // If change this value, must change on ScreenSpaceReflections.compute on 'float speed = saturate((speedDst + speedSrc) * 128.0f / (...)'
        public ClampedFloatParameter speedRejectionParam = new(0.5f, 0.0f, 1.0f);

        /// <summary>
        /// Controls the upper range of speed. The faster the objects or camera are moving, the higher this number should be.
        /// </summary>
        // If change this value, must change on ScreenSpaceReflections.compute on 'float speed = saturate((speedDst + speedSrc) * 128.0f / (...)'
        public ClampedFloatParameter speedRejectionScalerFactor = new(0.2f, 0.001f, 1f);
        
        /// <summary>
        /// When enabled, world space speed from Motion vector is used to reject samples.
        /// </summary>
        public BoolParameter enableWorldSpeedRejection = new(false);
        
        [Header("Performance")]
        [AdditionalProperty]
        public ClampedIntParameter steps = new(128, 60, 500);

        [AdditionalProperty]
        [Tooltip("Linear search step size, invalid when use hiz mode")]
        public ClampedFloatParameter stepSize = new(0.1f, 0.01f, 0.25f);
        
#if UNITY_EDITOR
        [Header("Debug")]
        [AdditionalProperty]
        [SerializeField]
        internal BoolParameter fullScreenDebugMode = new(false);
#endif
    }
}