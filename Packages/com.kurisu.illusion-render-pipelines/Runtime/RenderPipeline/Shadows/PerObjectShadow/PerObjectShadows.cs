using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="DepthBits"/> value.
    /// </summary>
    [Serializable]
    public sealed class DepthBitsParameter : VolumeParameter<DepthBits>
    {
        /// <summary>
        /// Creates a new <see cref="DepthBitsParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public DepthBitsParameter(DepthBits value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="ShadowTileResolution"/> value.
    /// </summary>
    [Serializable]
    public sealed class ShadowTileResolutionParameter : VolumeParameter<ShadowTileResolution>
    {
        /// <summary>
        /// Creates a new <see cref="ShadowTileResolutionParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ShadowTileResolutionParameter(ShadowTileResolution value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Per Object Shadows")]
    public class PerObjectShadows : VolumeComponent
    {
        /// <summary>
        /// Sets the depth buffer precision for the per-object shadow map.
        /// </summary>
        [Tooltip("Sets the depth buffer precision for the per-object shadow map.")]
        public DepthBitsParameter perObjectShadowDepthBits = new(DepthBits.Depth16);

        /// <summary>
        /// Sets the resolution for each tile in the per-object shadow atlas.
        /// </summary>
        [Tooltip("Sets the resolution for each tile in the per-object shadow atlas.")]
        public ShadowTileResolutionParameter perObjectShadowTileResolution = new(ShadowTileResolution._1024);

        /// <summary>
        /// Distributes a fixed atlas budget from each caster's projected screen coverage and priority.
        /// </summary>
        [Tooltip("Distributes a fixed atlas budget from each caster's projected screen coverage and priority.")]
        public BoolParameter adaptiveTileResolution = new(true);

        /// <summary>
        /// Sets the fixed atlas budget used by adaptive tile allocation.
        /// </summary>
        [Tooltip("Sets the fixed atlas budget used by adaptive tile allocation.")]
        public ShadowTileResolutionParameter adaptiveAtlasResolution = new(ShadowTileResolution._4096);

        /// <summary>
        /// Limits the resolution requested by a single adaptive tile.
        /// </summary>
        [Tooltip("Limits the resolution requested by a single adaptive tile.")]
        public ShadowTileResolutionParameter maximumAdaptiveTileResolution = new(ShadowTileResolution._3072);

        /// <summary>
        /// Controls the offset distance for per-object shadow length calculation.
        /// </summary>
        [Header("Culling")]
        [AdditionalProperty]
        [Tooltip("Controls the offset distance for shadow length calculation.")]
        public ClampedFloatParameter perObjectShadowLengthOffset = new(50.0f, 0.0f, 1000.0f);
    }
}

