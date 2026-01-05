using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    /// <summary>
    /// A volume component that holds settings for the Micro Shadows effect.
    /// </summary>
    [Serializable]
#if UNITY_2023_1_OR_NEWER
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Micro Shadows")]
#else
    [VolumeComponentMenuForRenderPipeline("Illusion/Micro Shadows", typeof(UniversalRenderPipeline))]
#endif
    public class MicroShadows : VolumeComponent
    {
        /// <summary>
        /// When enabled, URP processes Micro Shadows for this Volume.
        /// </summary>
        [Tooltip("Enables micro shadows for directional lights.")]
        [DisplayInfo(name = "State")]
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Controls the opacity of the micro shadows.
        /// </summary>
        [Tooltip("Controls the opacity of the micro shadows.")]
        public ClampedFloatParameter opacity = new(1.0f, 0.0f, 1.0f);
    }
}