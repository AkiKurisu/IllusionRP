using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    public enum ShadowDenoiser
    {
        None,
        Spatial
    }
    
    [Serializable]
    public sealed class ShadowDenoiserParameter : VolumeParameter<ShadowDenoiser>
    {
        public ShadowDenoiserParameter(ShadowDenoiser value, bool overrideState = false)
            : base(value, overrideState) { }
    }
    
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Contact Shadows")]
    public class ContactShadows : VolumeComponent
    {
        /// <summary>
        /// When enabled, IllusionRP processes Contact Shadows for this Volume.
        /// </summary>
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Controls the length of the rays IllusionRP uses to calculate Contact Shadows.
        /// It is in meters, but it gets scaled by a factor depending on Distance Scale Factor
        /// and the depth of the point from where the contact shadow ray is traced.
        /// </summary>
        [Tooltip("Controls the length of the rays IllusionRP uses to calculate Contact Shadows. " +
                 "It is in meters, but it gets scaled by a factor depending on Distance Scale Factor " +
                 "and the depth of the point from where the contact shadow ray is traced.")]
        public ClampedFloatParameter length = new(0.15f, 0.0f, 1.0f);

        // /// <summary>
        // /// Controls the opacity of the contact shadows.
        // /// </summary>
        // public ClampedFloatParameter opacity = new(1.0f, 0.0f, 1.0f);

        /// <summary>
        /// Scales the length of the contact shadow ray based on the linear depth value at the origin of the ray.
        /// </summary>
        [Tooltip("Scales the length of the contact shadow ray based on the linear depth value at the origin of the ray.")]
        public ClampedFloatParameter distanceScaleFactor = new(0.5f, 0.0f, 1.0f);

        /// <summary>
        /// The distance from the camera, in meters, at which IllusionRP begins to fade out Contact Shadows.
        /// </summary>
        [Tooltip("The distance from the camera, in meters, at which IllusionRP begins to fade out Contact Shadows.")]
        public MinFloatParameter maxDistance = new(50.0f, 0.0f);

        /// <summary>
        /// The distance from the camera, in meters, at which IllusionRP begins to fade in Contact Shadows.
        /// </summary>
        [Tooltip("The distance from the camera, in meters, at which IllusionRP begins to fade in Contact Shadows.")]
        public MinFloatParameter minDistance = new(0.0f, 0.0f);

        /// <summary>
        /// The distance, in meters, over which IllusionRP fades Contact Shadows out when past the Max Distance.
        /// </summary>
        [Tooltip("The distance, in meters, over which IllusionRP fades Contact Shadows out when past the Max Distance.")]
        public MinFloatParameter fadeDistance = new(5.0f, 0.0f);

        /// <summary>
        /// The distance, in meters, over which IllusionRP fades Contact Shadows in when past the Min Distance.
        /// </summary>
        [Tooltip("The distance, in meters, over which IllusionRP fades Contact Shadows in when past the Min Distance.")]
        public MinFloatParameter fadeInDistance = new(0.0f, 0.0f);

        /// <summary>
        /// Controls the bias applied to the screen space ray cast to get contact shadows.
        /// </summary>
        [Tooltip("Controls the bias applied to the screen space ray cast to get contact shadows.")]
        public ClampedFloatParameter rayBias = new(0.2f, 0.0f, 1.0f);

        /// <summary>
        /// Controls the thickness of the objects found along the ray, essentially thickening the contact shadows.
        /// </summary>
        [Tooltip("Controls the thickness of the objects found along the ray, essentially thickening the contact shadows.")]
        public ClampedFloatParameter thicknessScale = new(0.15f, 0.02f, 10.0f);

        public ShadowDenoiserParameter shadowDenoiser = new(ShadowDenoiser.None);
        
        /// <summary>
        /// Controls the numbers of samples taken during the ray-marching process for shadows. Increasing this might lead to higher quality at the expenses of performance.
        /// </summary>
        [Tooltip("Controls the numbers of samples taken during the ray-marching process for shadows. Increasing this might lead to higher quality at the expenses of performance.")]
        public ClampedIntParameter sampleCount = new(10, 4, 64);
        
        /// <summary>
        /// Control the size of the filter used for ray traced shadows
        /// </summary>
        [Tooltip("Control the size of the filter used for ray traced shadows")]
        public ClampedIntParameter filterSizeTraced = new(16, 1, 32);
    }
}
