using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    internal enum ConvolutionBloomQuality
    {
        Medium,
        High
    }
    
    [Serializable]
    internal sealed class ConvolutionBloomQualityParameter : VolumeParameter<ConvolutionBloomQuality>
    {
        public ConvolutionBloomQualityParameter(ConvolutionBloomQuality value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Convolution Bloom")]
    public sealed class ConvolutionBloom : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);
        
        [Header("Bloom")]
        [SerializeField]
        internal ConvolutionBloomQualityParameter quality = new(ConvolutionBloomQuality.Medium);
        
        [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new(0.8f, 0.0f);

        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new(1.0f, 0.0f);

        /// <summary>
        /// Controls the extent of the veiling effect.
        /// </summary>
        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new(0.7f, 0f, 1f);

        [Tooltip("Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new(65472f, 0f);

        [Header("Performance")]
        [AdditionalProperty]
        public BoolParameter disableDispatchMergeOptimization = new(false);

        [AdditionalProperty]
        public BoolParameter disableReadWriteOptimization = new(false);

        [Header("FFT")]
        public Vector2Parameter fftExtend = new(new Vector2(0.1f, 0.1f));
        
        public BoolParameter updateOTF = new(false);
        
        public BoolParameter generatePSF = new(false);

        public TextureParameter imagePSF = new(null);

        public FloatParameter imagePSFScale = new(1.0f);

        public FloatParameter imagePSFMinClamp = new(0.0f);

        public FloatParameter imagePSFMaxClamp = new(65472f);

        public FloatParameter imagePSFPow = new(1f);

        public bool IsActive()
        {
            return enable.value;
        }

        public bool IsTileCompatible()
        {
            return false;
        }

        public bool IsParamUpdated()
        {
            return updateOTF.value;
        }
    }
}