using System;
using Illusion.Rendering.Shadows;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.AreaLights
{
    /// <summary>
    /// Shadow map resolutions available to area lights.
    /// </summary>
    public enum AreaShadowResolution
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    [Serializable]
    public sealed class AreaShadowResolutionParameter : VolumeParameter<AreaShadowResolution>
    {
        public AreaShadowResolutionParameter(AreaShadowResolution value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>
    /// Rectangle area light settings.
    /// </summary>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Area Lighting")]
    public class AreaLighting : VolumeComponent
    {
        /// <summary>
        /// When enabled, IllusionRP evaluates rectangle area lights for this Volume.
        /// </summary>
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Resolution of the area light shadow atlas.
        /// </summary>
        [Tooltip("Resolution of the area light shadow atlas.")]
        public AreaShadowResolutionParameter shadowAtlasResolution = new(AreaShadowResolution._4096);

        /// <summary>
        /// Maximum shadow map resolution a single area light can request.
        /// </summary>
        [Tooltip("Maximum shadow map resolution a single area light can request.")]
        public AreaShadowResolutionParameter maxShadowResolution = new(AreaShadowResolution._2048);

        /// <summary>
        /// Depth bits of the area light shadow atlas.
        /// </summary>
        [Tooltip("Depth bits of the area light shadow atlas.")]
        public DepthBitsParameter shadowAtlasDepthBits = new(DepthBits.Depth32);

        /// <summary>
        /// Maximum number of area light shadows rendered per camera.
        /// </summary>
        [Tooltip("Maximum number of area light shadows rendered per camera.")]
        public ClampedIntParameter maxShadowRequests = new(8, 1, 32);
    }
}
