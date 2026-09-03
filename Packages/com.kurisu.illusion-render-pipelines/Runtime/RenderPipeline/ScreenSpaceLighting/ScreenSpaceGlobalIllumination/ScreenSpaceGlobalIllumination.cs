using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// This defines the order in which the fall backs are used if a screen space global illumination ray misses.
    /// </summary>
    public enum RayMarchingFallbackHierarchy
    {
        /// <summary>
        /// When selected, ray tracing will fall back on reflection probes (if any) then on the sky.
        /// </summary>
        [InspectorName("Reflection Probes and Sky")]
        ReflectionProbesAndSky = 0x03,
        /// <summary>
        /// When selected, ray tracing will fall back on reflection probes (if any).
        /// </summary>
        [InspectorName("Reflection Probes")]
        ReflectionProbes = 0x02,
        /// <summary>
        /// When selected, ray tracing will fall back on the sky.
        /// </summary>
        [InspectorName("Sky")]
        Sky = 0x01,
        /// <summary>
        /// When selected, ray tracing will return a black color.
        /// </summary>
        [InspectorName("None")]
        None = 0x00,
    }

    [Serializable]
    public sealed class RayMarchingFallbackHierarchyParameter : VolumeParameter<RayMarchingFallbackHierarchy>
    {
        public RayMarchingFallbackHierarchyParameter(RayMarchingFallbackHierarchy value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Screen Space Global Illumination")]
    public class ScreenSpaceGlobalIllumination : VolumeComponent
    {
        /// <summary>
        /// Enable or disable Screen Space Global Illumination.
        /// </summary>
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Compute and reproject at half resolution.
        /// </summary>
        [Tooltip("Compute and reproject at half resolution for better performance.")]
        public BoolParameter halfResolution = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// The thickness of the depth buffer value used for the ray marching step
        /// </summary>
        [Tooltip("Controls the thickness of the depth buffer used for ray marching.")]
        public ClampedFloatParameter depthBufferThickness = new(0.1f, 0.0f, 0.5f);

        /// <summary>
        /// Maximum number of ray marching steps.
        /// </summary>
        [Header("Ray Marching")]
        [Tooltip("Maximum number of ray marching steps. Higher values improve quality but reduce performance.")]
        public ClampedIntParameter maxRaySteps = new(64, 1, 256);

        /// <summary>
        /// Fallback mode when ray misses geometry.
        /// </summary>
        [Tooltip("Fallback mode when ray misses geometry.")]
        public RayMarchingFallbackHierarchyParameter rayMiss = new(RayMarchingFallbackHierarchy.ReflectionProbesAndSky);
        
        [Tooltip("When enabled, probe volumes will be sampled when ray misses geometry.")]
        public BoolParameter enableProbeVolumes = new(true, BoolParameter.DisplayType.EnumPopup);
        
        /// <summary>
        /// Enable denoising for SSGI.
        /// </summary>
        [Header("Denoising")]
        [Tooltip("Enable temporal and spatial denoising for smoother results.")]
        public BoolParameter denoise = new(true, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Denoiser radius for spatial filter.
        /// </summary>
        [Tooltip("Controls the radius of the GI denoiser (First Pass).")]
        public ClampedFloatParameter denoiserRadius = new(0.6f, 0.001f, 10.0f);
        
        /// <summary>
        /// Defines if the second denoising pass should be enabled.
        /// </summary>
        [Tooltip("Enable second denoising pass.")]
        public BoolParameter secondDenoiserPass = new(true, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Use half resolution denoising for better performance.
        /// </summary>
        [Tooltip("Apply the bilateral filter at half resolution and upsample. Improves performance.")]
        public BoolParameter halfResolutionDenoiser = new(false, BoolParameter.DisplayType.EnumPopup);
    }
}

