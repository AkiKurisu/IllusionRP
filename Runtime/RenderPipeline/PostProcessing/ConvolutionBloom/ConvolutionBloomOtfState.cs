using System;
using UnityEngine;

namespace Illusion.Rendering.PostProcessing
{
    internal readonly struct ConvolutionBloomOtfSettings : IEquatable<ConvolutionBloomOtfSettings>
    {
        internal readonly ConvolutionBloomQuality Quality;

        internal readonly Vector2 FftExtend;

        internal readonly bool GeneratePsf;

        internal readonly Texture ImagePsf;

        internal readonly float ImagePsfScale;

        internal readonly float ImagePsfMinClamp;

        internal readonly float ImagePsfMaxClamp;

        internal readonly float ImagePsfPow;

        internal ConvolutionBloomOtfSettings(
            ConvolutionBloomQuality quality,
            Vector2 fftExtend,
            bool generatePsf,
            Texture imagePsf,
            float imagePsfScale,
            float imagePsfMinClamp,
            float imagePsfMaxClamp,
            float imagePsfPow)
        {
            Quality = quality;
            FftExtend = fftExtend;
            GeneratePsf = generatePsf;
            ImagePsf = imagePsf;
            ImagePsfScale = imagePsfScale;
            ImagePsfMinClamp = imagePsfMinClamp;
            ImagePsfMaxClamp = imagePsfMaxClamp;
            ImagePsfPow = imagePsfPow;
        }

        internal static ConvolutionBloomOtfSettings From(ConvolutionBloom settings)
        {
            return new ConvolutionBloomOtfSettings(
                settings.quality.value,
                settings.fftExtend.value,
                settings.generatePSF.value,
                settings.imagePSF.value,
                settings.imagePSFScale.value,
                settings.imagePSFMinClamp.value,
                settings.imagePSFMaxClamp.value,
                settings.imagePSFPow.value);
        }

        public bool Equals(ConvolutionBloomOtfSettings other)
        {
            return Quality == other.Quality
                   && FftExtend.Equals(other.FftExtend)
                   && GeneratePsf == other.GeneratePsf
                   && ReferenceEquals(ImagePsf, other.ImagePsf)
                   && ImagePsfScale.Equals(other.ImagePsfScale)
                   && ImagePsfMinClamp.Equals(other.ImagePsfMinClamp)
                   && ImagePsfMaxClamp.Equals(other.ImagePsfMaxClamp)
                   && ImagePsfPow.Equals(other.ImagePsfPow);
        }

        public override bool Equals(object obj)
        {
            return obj is ConvolutionBloomOtfSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Quality;
                hashCode = (hashCode * 397) ^ FftExtend.GetHashCode();
                hashCode = (hashCode * 397) ^ GeneratePsf.GetHashCode();
                hashCode = (hashCode * 397) ^ (ImagePsf ? ImagePsf.GetInstanceID() : 0);
                hashCode = (hashCode * 397) ^ ImagePsfScale.GetHashCode();
                hashCode = (hashCode * 397) ^ ImagePsfMinClamp.GetHashCode();
                hashCode = (hashCode * 397) ^ ImagePsfMaxClamp.GetHashCode();
                hashCode = (hashCode * 397) ^ ImagePsfPow.GetHashCode();
                return hashCode;
            }
        }
    }

    internal sealed class ConvolutionBloomOtfState
    {
        private bool _isValid;

        private ConvolutionBloomOtfSettings _settings;

        private uint _latestScheduledVersion;

        internal bool RequiresUpdate(
            in ConvolutionBloomOtfSettings settings,
            bool resourceReallocated,
            bool resourceCreated,
            bool forceUpdate)
        {
            if (resourceReallocated || !resourceCreated)
            {
                _isValid = false;
            }

            return forceUpdate || !_isValid || !_settings.Equals(settings);
        }

        internal uint ScheduleUpdate()
        {
            return ++_latestScheduledVersion;
        }

        internal void MarkUpdated(in ConvolutionBloomOtfSettings settings, uint scheduledVersion)
        {
            if (scheduledVersion != _latestScheduledVersion) return;

            _settings = settings;
            _isValid = true;
        }

        internal void Reset()
        {
            _isValid = false;
            ++_latestScheduledVersion;
        }
    }
}
