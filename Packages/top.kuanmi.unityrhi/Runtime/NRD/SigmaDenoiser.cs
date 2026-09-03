using System;

namespace UnityRhi.Nrd
{
    // -----------------------------------------------------------------------
    // SIGMA – shadow / translucency denoiser
    // -----------------------------------------------------------------------
    public sealed class SigmaDenoiser : NrdDenoiser<SigmaSettings>
    {
        public SigmaDenoiser(string camName, Denoiser denoiser)
            : base(camName, new NrdDenoiserDesc(denoiser))
        {
            if (denoiser != Denoiser.SIGMA_SHADOW && denoiser != Denoiser.SIGMA_SHADOW_TRANSLUCENCY)
                throw new ArgumentException(
                    $"SigmaNrdDenoiser requires a SIGMA_* denoiser, got {denoiser}.", nameof(denoiser));
            _denoiser = denoiser;
        }
    }
}