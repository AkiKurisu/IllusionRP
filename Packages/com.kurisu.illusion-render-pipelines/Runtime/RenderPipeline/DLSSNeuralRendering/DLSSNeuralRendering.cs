using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public enum DLSSNeuralRenderingPreset : byte
    {
        Default = 0,
        Preset1 = 1,
        Preset2 = 2,
        Preset3 = 3,
    }

    public enum DLSSNeuralRenderingStyle : byte
    {
        Default = 0,
        Natural = 1,
        Cinematic = 2,
    }

    public enum DLSSNeuralRenderingDebugMode
    {
        Off,
        Color,
        MotionVectors,
        MotionMagnitude,
        DeviceDepth,
        LinearEyeDepth,
    }

    [Serializable]
    public sealed class DLSSNeuralRenderingPresetParameter : VolumeParameter<DLSSNeuralRenderingPreset>
    {
        public DLSSNeuralRenderingPresetParameter(DLSSNeuralRenderingPreset value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class DLSSNeuralRenderingStyleParameter : VolumeParameter<DLSSNeuralRenderingStyle>
    {
        public DLSSNeuralRenderingStyleParameter(DLSSNeuralRenderingStyle value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>Camera-blended controls for the experimental full-resolution DLSS Neural Rendering pass.</summary>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/DLSS Neural Rendering")]
    [DisplayInfo(name = "DLSS Neural Rendering")]
    public sealed class DLSSNeuralRendering : VolumeComponent, IPostProcessComponent
    {
        [DisplayInfo(name = "State")]
        [Tooltip("Enable DLSS Neural Rendering for this camera.")]
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        public DLSSNeuralRenderingPresetParameter preset = new(DLSSNeuralRenderingPreset.Default);
        public DLSSNeuralRenderingStyleParameter style = new(DLSSNeuralRenderingStyle.Default);

        [Tooltip("Overall Neural Rendering intensity.")]
        public ClampedFloatParameter intensity = new(1f, 0f, 2f);

        [DisplayInfo(name = "Local Tone")]
        public ClampedFloatParameter localToneStrength = new(1f, 0f, 2f);

        [DisplayInfo(name = "Local Structure")]
        public ClampedFloatParameter localStructureStrength = new(1f, 0f, 2f);

        [DisplayInfo(name = "Skin Structure")]
        public ClampedFloatParameter skinStructureStrength = new(-1f, -1f, 2f);

        [DisplayInfo(name = "Auto Mask")]
        public BoolParameter useAutoMask = new(false);

        [DisplayInfo(name = "UI Correction")]
        public BoolParameter uiCorrection = new(false);

        [AdditionalProperty]
        [Tooltip("Multiplier applied while converting URP motion to current-to-previous pixel motion.")]
        public Vector2Parameter motionVectorScale = new(Vector2.one);

        [AdditionalProperty]
        [Tooltip("Camera movement larger than this resets temporal history.")]
        public MinFloatParameter cameraCutDistance = new(5f, 0f);

        [AdditionalProperty]
        [Tooltip("Camera rotation larger than this resets temporal history.")]
        public ClampedFloatParameter cameraCutAngle = new(45f, 0f, 180f);

        public bool IsActive() => active && enable.value;
        public bool IsTileCompatible() => false;
    }
}
