using System;
using System.Collections.Generic;

namespace UnityRhi.Nrd
{
    // -----------------------------------------------------------------------
    // RELAX – spatiotemporal accumulation denoiser
    // -----------------------------------------------------------------------
    public sealed class RelaxDenoiser : NrdDenoiser<RelaxSettings>
    {
        private static readonly HashSet<Denoiser> ValidDenoisers = new()
        {
            Denoiser.RELAX_DIFFUSE,
            Denoiser.RELAX_DIFFUSE_SH,
            Denoiser.RELAX_SPECULAR,
            Denoiser.RELAX_SPECULAR_SH,
            Denoiser.RELAX_DIFFUSE_SPECULAR,
            Denoiser.RELAX_DIFFUSE_SPECULAR_SH,
        };

        public RelaxDenoiser(string camName, Denoiser denoiser)
            : base(camName, new NrdDenoiserDesc(denoiser))
        {
            if (!ValidDenoisers.Contains(denoiser))
                throw new ArgumentException(
                    $"RelaxNrdDenoiser requires a RELAX_* denoiser, got {denoiser}.", nameof(denoiser));
            _denoiser = denoiser;
        }
    }
}