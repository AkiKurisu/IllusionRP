using System;

namespace UnityRhi.Nrd
{
    // -----------------------------------------------------------------------
    // REFERENCE – identity / debug denoiser
    // -----------------------------------------------------------------------
    public sealed class ReferenceDenoiser : NrdDenoiser<ReferenceSettings>
    {
        public ReferenceDenoiser(string camName, Denoiser denoiser)
            : base(camName, new NrdDenoiserDesc(denoiser))
        {
            if (denoiser != Denoiser.REFERENCE)
                throw new ArgumentException(
                    $"ReferenceDenoiser requires the REFERENCE denoiser, got {denoiser}.", nameof(denoiser));
            _denoiser = denoiser;
        }
    }
}