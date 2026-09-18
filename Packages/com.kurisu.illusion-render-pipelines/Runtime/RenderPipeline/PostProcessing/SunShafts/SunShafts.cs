using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    public enum SunShaftsResolution
    {
        Low,
        Normal,
        High
    }

    public enum SunShaftsBlendMode
    {
        Screen,
        Add
    }

    [Serializable]
    public sealed class SunShaftsResolutionParameter : VolumeParameter<SunShaftsResolution>
    {
        public SunShaftsResolutionParameter(SunShaftsResolution value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SunShaftsBlendModeParameter : VolumeParameter<SunShaftsBlendMode>
    {
        public SunShaftsBlendModeParameter(SunShaftsBlendMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Sun Shafts")]
    public sealed class SunShafts : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        [Header("Shafts")]
        [Tooltip("Colour below which a pixel contributes nothing to the shafts.")]
        public ColorParameter thresholdColor = new(new Color(0.50196f, 0.50196f, 0.50196f, 1.0f), false, false, true);

        [Tooltip("Tint of the shafts.")]
        public ColorParameter shaftsColor = new(Color.white, false, false, true);

        [Tooltip("Screen space radius the shafts reach around the caster.")]
        public ClampedFloatParameter maxRadius = new(0.75f, 0.1f, 1.0f);

        [Tooltip("Length of the radial blur.")]
        public ClampedFloatParameter blurRadius = new(5.0f, 1.0f, 10.0f);

        [Tooltip("Brightness of the shafts.")]
        public ClampedFloatParameter intensity = new(5.0f, 0.0f, 10.0f);

        [Header("Performance & Quality")]
        [Tooltip("Radial blur iterations. Each iteration costs two blits.")]
        public ClampedIntParameter iterations = new(2, 1, 4);

        [Tooltip("Resolution the shafts are accumulated at: a quarter, a half, or the full camera resolution.")]
        public SunShaftsResolutionParameter resolution = new(SunShaftsResolution.High);

        [Tooltip("How the shafts are combined with the camera colour.")]
        public SunShaftsBlendModeParameter blendMode = new(SunShaftsBlendMode.Add);

        public bool IsActive()
        {
            return enable.value && intensity.value > 0.0f && maxRadius.value > 0.0f;
        }

        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
