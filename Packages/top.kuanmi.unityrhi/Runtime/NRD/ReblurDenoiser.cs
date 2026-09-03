using System;
using System.Collections.Generic;

namespace UnityRhi.Nrd
{
    // -----------------------------------------------------------------------
    // REBLUR – diffuse / specular radiance + hit-distance denoiser
    // -----------------------------------------------------------------------
    public sealed class ReblurDenoiser : NrdDenoiser<ReblurSettings>
    {
        private static readonly HashSet<Denoiser> ValidDenoisers = new()
        {
            Denoiser.REBLUR_DIFFUSE,
            Denoiser.REBLUR_DIFFUSE_OCCLUSION,
            Denoiser.REBLUR_DIFFUSE_SH,
            Denoiser.REBLUR_SPECULAR,
            Denoiser.REBLUR_SPECULAR_OCCLUSION,
            Denoiser.REBLUR_SPECULAR_SH,
            Denoiser.REBLUR_DIFFUSE_SPECULAR,
            Denoiser.REBLUR_DIFFUSE_SPECULAR_OCCLUSION,
            Denoiser.REBLUR_DIFFUSE_SPECULAR_SH,
            Denoiser.REBLUR_DIFFUSE_DIRECTIONAL_OCCLUSION,
        };

        public ReblurDenoiser(string camName, Denoiser denoiser)
            : base(camName, new NrdDenoiserDesc(denoiser))
        {
            if (!ValidDenoisers.Contains(denoiser))
                throw new ArgumentException(
                    $"ReblurNrdDenoiser requires a REBLUR_* denoiser, got {denoiser}.", nameof(denoiser));
            _denoiser = denoiser;
        }
    }
}