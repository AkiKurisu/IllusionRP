namespace UnityRhi
{
    /// <summary>Matches nri::UpscalerMode and the reference integration.</summary>
    public enum UpscalerMode : byte
    {
        NATIVE,
        ULTRA_QUALITY,
        QUALITY,
        BALANCED,
        PERFORMANCE,
        ULTRA_PERFORMANCE,
    }

    /// <summary>
    /// DLSS Ray Reconstruction network preset passed directly to NGX.
    /// Letter values intentionally match NVIDIA's preset identifiers.
    /// </summary>
    public enum DlssRrPreset : byte
    {
        Default = 0,
        PresetD = 4,
        PresetE = 5,
        PresetF = 6,
    }
}
