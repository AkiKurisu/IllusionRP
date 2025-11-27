using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    /// <summary>
    /// Options to select a tonemapping algorithm to use for color grading.
    /// </summary>
    public enum AdvancedTonemappingMode
    {
        /// <summary>
        /// Use this option if you do not want to apply tonemapping
        /// </summary>
        None,
        
        [InspectorName("Gran-Turismo")]
        GranTurismo,
        
        [InspectorName("Filmic ACES")]
        Filmic_ACES,
    }
    
    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="AdvancedTonemappingMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class AdvancedTonemappingModeParameter : VolumeParameter<AdvancedTonemappingMode>
    {
        /// <summary>
        /// Creates a new <see cref="AdvancedTonemappingModeParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public AdvancedTonemappingModeParameter(AdvancedTonemappingMode value, bool overrideState = false) : base(value, overrideState) { }
    }
    
    [Serializable, VolumeComponentMenuForRenderPipeline("Illusion/Advanced Tonemapping", typeof(UniversalRenderPipeline))]
    public sealed class AdvancedTonemapping : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Use this to select a tonemapping algorithm to use for color grading.
        /// </summary>
        [Tooltip("Select a tonemapping algorithm to use for the color grading process.")]
        public AdvancedTonemappingModeParameter mode = new(AdvancedTonemappingMode.None);

        // GT.
        public ClampedFloatParameter maxBrightness = new(1, 0.1f, 5.0f);
        
        public ClampedFloatParameter contrast = new(1, 0.1f, 5.0f);

        public ClampedFloatParameter linearSectionStart = new(0.22f, 0.01f, 1.0f);

        public ClampedFloatParameter linearSectionLength = new(0.4f, 0.01f, 1.0f);

        // public ClampedFloatParameter blackTightness = new(1.33f, 1.0f, 3.0f);

        // Filmic ACES from Unreal Engine.
        [Tooltip("Film_Slope")]
        public ClampedFloatParameter slope = new(0.88f, 0f, 1f);
        
        [Tooltip("Film_Toe")]
        public ClampedFloatParameter toe = new(0.55f, 0.0f, 1.0f);

        [Tooltip("Film_Shoulder")]
        public ClampedFloatParameter shoulder = new(0.26f, 0.0f, 1.0f);

        [Tooltip("Film_BlackClip")]
        public ClampedFloatParameter blackClip = new(0.0f, 0.0f, 1.0f);

        [Tooltip("Film_WhiteClip")]
        public ClampedFloatParameter whiteClip = new(0.04f, 0.0f, 1.0f);
        
        public bool IsActive()
        {
            return mode.value != AdvancedTonemappingMode.None;
        }

        public bool IsTileCompatible()
        {
            return true;
        }
    }
}