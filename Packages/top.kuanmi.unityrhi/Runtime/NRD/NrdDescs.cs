namespace UnityRhi.Nrd
{
    public enum ResourceType : uint
    {
        //=============================================================================================================================
        // NON-NOISY INPUTS
        //=============================================================================================================================

        // 3D world-space motion (RGBA16f+) or 2D screen-space motion (RG16f+), MVs must be non-jittered, MV = previous - current
        IN_MV,

        // Data must match encoding in "NRD_FrontEnd_PackNormalAndRoughness" and "NRD_FrontEnd_UnpackNormalAndRoughness" (RGBA8+)
        IN_NORMAL_ROUGHNESS,

        // Linear view depth for primary rays (R16f+)
        IN_VIEWZ,

        // (Optional) User-provided history confidence in range 0-1, i.e. antilag (R8+)
        // Used only if "CommonSettings::isHistoryConfidenceAvailable = true" and "NRD_SUPPORTS_HISTORY_CONFIDENCE = 1"
        IN_DIFF_CONFIDENCE,
        IN_SPEC_CONFIDENCE,

        // (Optional) User-provided disocclusion threshold selector in range 0-1 (R8+)
        // Disocclusion threshold is mixed between "disocclusionThreshold" and "disocclusionThresholdAlternate"
        // Used only if "CommonSettings::isDisocclusionThresholdMixAvailable = true" and "NRD_SUPPORTS_DISOCCLUSION_THRESHOLD_MIX = 1"
        IN_DISOCCLUSION_THRESHOLD_MIX,

        //=============================================================================================================================
        // NOISY INPUTS
        //=============================================================================================================================

        // Radiance and hit distance (RGBA16f+)
        //      REBLUR: use "REBLUR_FrontEnd_PackRadianceAndNormHitDist" for encoding
        //      RELAX: use "RELAX_FrontEnd_PackRadianceAndHitDist" for encoding
        IN_DIFF_RADIANCE_HITDIST,
        IN_SPEC_RADIANCE_HITDIST,

        // Hit distance (R8+)
        //      REBLUR: use "REBLUR_FrontEnd_GetNormHitDist" for encoding
        IN_DIFF_HITDIST,
        IN_SPEC_HITDIST,

        // Sampling direction and normalized hit distance (RGBA8+)
        //      REBLUR: use "REBLUR_FrontEnd_PackDirectionalOcclusion" for encoding
        IN_DIFF_DIRECTION_HITDIST,

        // SH data (2x RGBA16f+)
        //      REBLUR: use "REBLUR_FrontEnd_PackSh" for encoding
        //      RELAX: use "RELAX_FrontEnd_PackSh" for encoding
        IN_DIFF_SH0,
        IN_DIFF_SH1,
        IN_SPEC_SH0,
        IN_SPEC_SH1,

        // Penumbra and optional translucency (R16f+ and RGBA8+ for translucency)
        //      SIGMA: use "SIGMA_FrontEnd_PackPenumbra" for penumbra properties encoding
        //      SIGMA: use "SIGMA_FrontEnd_PackTranslucency" for translucency encoding
        IN_PENUMBRA,
        IN_TRANSLUCENCY,

        // Some signal (R8+)
        IN_SIGNAL,

        //=============================================================================================================================
        // OUTPUTS
        //=============================================================================================================================

        // IMPORTANT: Most of denoisers do not write into output pixels outside of "CommonSettings::denoisingRange"!

        // Radiance and normalized hit distance (occlusion) or history length
        //      REBLUR: use "REBLUR_BackEnd_UnpackRadianceAndNormHitDist" for decoding (R11G11B10f+)
        //          .w = diffuse or specular occlusion (default) or history length in frames if "ReblurSettings::returnHistoryLengthInsteadOfOcclusion = true"
        //      RELAX: use "RELAX_BackEnd_UnpackRadiance" for decoding (R11G11B10f+)
        //          .w = diffuse history length in frames
        OUT_DIFF_RADIANCE_HITDIST,
        OUT_SPEC_RADIANCE_HITDIST,

        // SH data
        //      REBLUR: use "REBLUR_BackEnd_UnpackSh" for decoding (2x RGBA16f+)
        //          .normHitDist = diffuse or specular occlusion (default) or history length in frames if "ReblurSettings::returnHistoryLengthInsteadOfOcclusion = true"
        //      RELAX: use "RELAX_BackEnd_UnpackSh" for decoding (2x RGBA16f+)
        //          .normHitDist = diffuse history length in frames
        OUT_DIFF_SH0,
        OUT_DIFF_SH1,
        OUT_SPEC_SH0,
        OUT_SPEC_SH1,

        // Normalized hit distance (R8+)
        OUT_DIFF_HITDIST,
        OUT_SPEC_HITDIST,

        // Bent normal and normalized hit distance (RGBA8+)
        //      REBLUR: use "REBLUR_BackEnd_UnpackDirectionalOcclusion" for decoding
        OUT_DIFF_DIRECTION_HITDIST,

        // Shadow and optional transcluceny (R8+ or RGBA8+)
        //      SIGMA: use "SIGMA_BackEnd_UnpackShadow" for decoding
        OUT_SHADOW_TRANSLUCENCY, // IMPORTANT: used as history if "stabilizationStrength != 0"

        // Denoised signal (R8+)
        OUT_SIGNAL,

        // (Optional) Debug output (RGBA8+), .w = transparency
        // Used if "CommonSettings::enableValidation = true"
        OUT_VALIDATION,

        //=============================================================================================================================
        // POOLS
        //=============================================================================================================================

        // Can be reused after denoising
        TRANSIENT_POOL,

        // Dedicated to NRD, can't be reused
        PERMANENT_POOL,

        MAX_NUM,
    };

    // Must match "nrd::Denoiser" in NRDDescs.h (order-sensitive).
    public enum Denoiser : uint
    {
        // REBLUR
        REBLUR_DIFFUSE = 0,
        REBLUR_DIFFUSE_OCCLUSION,
        REBLUR_DIFFUSE_SH,
        REBLUR_SPECULAR,
        REBLUR_SPECULAR_OCCLUSION,
        REBLUR_SPECULAR_SH,
        REBLUR_DIFFUSE_SPECULAR,
        REBLUR_DIFFUSE_SPECULAR_OCCLUSION,
        REBLUR_DIFFUSE_SPECULAR_SH,
        REBLUR_DIFFUSE_DIRECTIONAL_OCCLUSION,

        // RELAX
        RELAX_DIFFUSE,
        RELAX_DIFFUSE_SH,
        RELAX_SPECULAR,
        RELAX_SPECULAR_SH,
        RELAX_DIFFUSE_SPECULAR,
        RELAX_DIFFUSE_SPECULAR_SH,

        // SIGMA
        SIGMA_SHADOW,
        SIGMA_SHADOW_TRANSLUCENCY,

        // REFERENCE
        REFERENCE,

        MAX_NUM
    }

    // Creation-time denoiser descriptor passed to CreateDenoiserInstance.
    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct NrdDenoiserDesc
    {
        public uint     identifier;
        public Denoiser denoiser;

        public NrdDenoiserDesc(Denoiser denoiser)
        {
            identifier    = 0;
            this.denoiser = denoiser;
        }
    }
}