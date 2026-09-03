using UnityEngine;

namespace Illusion.Rendering
{
    internal readonly struct DLSSNeuralRenderingSettings
    {
        internal readonly DLSSNeuralRenderingPreset Preset;
        internal readonly DLSSNeuralRenderingStyle Style;
        internal readonly float Intensity;
        internal readonly float LocalToneStrength;
        internal readonly float LocalStructureStrength;
        internal readonly float SkinStructureStrength;
        internal readonly bool UseAutoMask;
        internal readonly bool UiCorrection;
        internal readonly Vector2 MotionVectorScale;
        internal readonly float CameraCutDistance;
        internal readonly float CameraCutAngle;
        internal readonly DLSSNeuralRenderingDebugMode DebugMode;
        internal readonly float DebugMotionRange;
        internal readonly float DebugDepthRange;

        internal DLSSNeuralRenderingSettings(DLSSNeuralRendering volume, IllusionRuntimeRenderingConfig config)
        {
            Preset = volume.preset.value;
            Style = volume.style.value;
            Intensity = volume.intensity.value;
            LocalToneStrength = volume.localToneStrength.value;
            LocalStructureStrength = volume.localStructureStrength.value;
            SkinStructureStrength = volume.skinStructureStrength.value;
            UseAutoMask = volume.useAutoMask.value;
            UiCorrection = volume.uiCorrection.value;
            MotionVectorScale = volume.motionVectorScale.value;
            CameraCutDistance = volume.cameraCutDistance.value;
            CameraCutAngle = volume.cameraCutAngle.value;
            DebugMode = config.DLSSNeuralRenderingDebugMode;
            DebugMotionRange = config.DLSSNeuralRenderingDebugMotionRange;
            DebugDepthRange = config.DLSSNeuralRenderingDebugDepthRange;
        }
    }
}
