using UnityEngine;

namespace Illusion.Rendering.AreaLights
{
    /// <summary>
    /// Area light settings attached next to a Unity rectangle <see cref="Light"/>.
    /// The Light component stays the authority for color, intensity, shape size, range and shadow on/off.
    /// </summary>
    // Reference: UnityEngine.Rendering.HighDefinition.HDAdditionalLightData (rectangle area light fields)
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("Rendering/Illusion Additional Light Data")]
    public class IllusionAdditionalLightData : MonoBehaviour
    {
        internal const float k_MinEvsmExponent = 5.0f;
        internal const float k_MaxEvsmExponent = 42.0f;
        internal const float k_MinEvsmLightLeakBias = 0.0f;
        internal const float k_MaxEvsmLightLeakBias = 1.0f;
        internal const float k_MinEvsmVarianceBias = 0.0f;
        internal const float k_MaxEvsmVarianceBias = 0.001f;
        internal const int k_MinEvsmBlurPasses = 0;
        internal const int k_MaxEvsmBlurPasses = 8;

        internal const float k_MinAreaLightShadowCone = 10.0f;
        internal const float k_MaxAreaLightShadowCone = 179.0f;

        internal const int k_MinShadowResolution = 64;
        internal const int k_MaxShadowResolution = 4096;

        [SerializeField, Range(0.0f, 16.0f)]
        float m_LightDimmer = 1.0f;

        /// <summary>
        /// Get/Set the light dimmer / multiplier, between 0 and 16.
        /// </summary>
        public float lightDimmer
        {
            get => m_LightDimmer;
            set => m_LightDimmer = Mathf.Clamp(value, 0.0f, 16.0f);
        }

        [SerializeField]
        bool m_AffectDiffuse = true;

        /// <summary>
        /// Controls whether the light affects the diffuse or not
        /// </summary>
        public bool affectDiffuse
        {
            get => m_AffectDiffuse;
            set => m_AffectDiffuse = value;
        }

        [SerializeField]
        bool m_AffectSpecular = true;

        /// <summary>
        /// Controls whether the light affects the specular or not
        /// </summary>
        public bool affectSpecular
        {
            get => m_AffectSpecular;
            set => m_AffectSpecular = value;
        }

        [SerializeField]
        bool m_ApplyRangeAttenuation = true;

        /// <summary>
        /// If enabled, the light will smoothly fade to zero at its range, else it keeps its full inverse squared falloff.
        /// </summary>
        public bool applyRangeAttenuation
        {
            get => m_ApplyRangeAttenuation;
            set => m_ApplyRangeAttenuation = value;
        }

        [SerializeField]
        float m_FadeDistance = 10000.0f;

        /// <summary>
        /// Get/Set fade distance.
        /// </summary>
        public float fadeDistance
        {
            get => m_FadeDistance;
            set => m_FadeDistance = Mathf.Clamp(value, 0, float.MaxValue);
        }

        [SerializeField, Min(0.0f)]
        float m_ShapeRadius = 0.025f;

        /// <summary>
        /// Get/Set the radius of a light, drives the shadow softness.
        /// </summary>
        public float shapeRadius
        {
            get => m_ShapeRadius;
            set => m_ShapeRadius = Mathf.Max(value, 0.0f);
        }

        [SerializeField, Range(0.0f, 2.0f)]
        float m_SoftnessScale = 1.0f;

        /// <summary>
        /// Get/Set the softness scale for area lights shadows.
        /// </summary>
        public float softnessScale
        {
            get => m_SoftnessScale;
            set => m_SoftnessScale = value;
        }

        [SerializeField, Range(0.0f, 180.0f)]
        float m_BarnDoorAngle = 90.0f;

        /// <summary>
        /// Get/Set the barn door angle for rectangle area lights.
        /// </summary>
        public float barnDoorAngle
        {
            get => m_BarnDoorAngle;
            set => m_BarnDoorAngle = Mathf.Clamp(value, 0.0f, 180.0f);
        }

        [SerializeField, Min(0.0f)]
        float m_BarnDoorLength = 0.05f;

        /// <summary>
        /// Get/Set the barn door length for rectangle area lights.
        /// </summary>
        public float barnDoorLength
        {
            get => m_BarnDoorLength;
            set => m_BarnDoorLength = Mathf.Max(value, 0.0f);
        }

        // Optional cookie for rectangular area lights
        [SerializeField]
        Texture m_AreaLightCookie = null;

        /// <summary>
        /// Get/Set cookie texture for area lights.
        /// </summary>
        public Texture areaLightCookie
        {
            get => m_AreaLightCookie;
            set => m_AreaLightCookie = value;
        }

        [SerializeField, Range(k_MinShadowResolution, k_MaxShadowResolution)]
        int m_ShadowResolution = 512;

        /// <summary>
        /// Requested shadow map resolution, clamped by the Area Lighting volume maximum.
        /// </summary>
        public int shadowResolution
        {
            get => m_ShadowResolution;
            set => m_ShadowResolution = Mathf.Clamp(value, k_MinShadowResolution, k_MaxShadowResolution);
        }

        [SerializeField, Range(k_MinAreaLightShadowCone, k_MaxAreaLightShadowCone)]
        float m_AreaLightShadowCone = 120.0f;

        /// <summary>
        /// Angular size of the cone used to approximate the area light shadows.
        /// </summary>
        public float areaLightShadowCone
        {
            get => m_AreaLightShadowCone;
            set => m_AreaLightShadowCone = Mathf.Clamp(value, k_MinAreaLightShadowCone, k_MaxAreaLightShadowCone);
        }

        [SerializeField, Range(k_MinEvsmExponent, k_MaxEvsmExponent)]
        float m_EvsmExponent = 15.0f;

        /// <summary>
        /// Controls the exponent used for EVSM shadows.
        /// </summary>
        public float evsmExponent
        {
            get => m_EvsmExponent;
            set => m_EvsmExponent = Mathf.Clamp(value, k_MinEvsmExponent, k_MaxEvsmExponent);
        }

        [SerializeField, Range(k_MinEvsmLightLeakBias, k_MaxEvsmLightLeakBias)]
        float m_EvsmLightLeakBias = 0.0f;

        /// <summary>
        /// Controls the light leak bias value for EVSM shadows.
        /// </summary>
        public float evsmLightLeakBias
        {
            get => m_EvsmLightLeakBias;
            set => m_EvsmLightLeakBias = Mathf.Clamp(value, k_MinEvsmLightLeakBias, k_MaxEvsmLightLeakBias);
        }

        [SerializeField, Range(k_MinEvsmVarianceBias, k_MaxEvsmVarianceBias)]
        float m_EvsmVarianceBias = 1e-5f;

        /// <summary>
        /// Controls the variance bias used for EVSM shadows.
        /// </summary>
        public float evsmVarianceBias
        {
            get => m_EvsmVarianceBias;
            set => m_EvsmVarianceBias = Mathf.Clamp(value, k_MinEvsmVarianceBias, k_MaxEvsmVarianceBias);
        }

        [SerializeField, Range(k_MinEvsmBlurPasses, k_MaxEvsmBlurPasses)]
        int m_EvsmBlurPasses = 0;

        /// <summary>
        /// Controls the number of blur passes used for EVSM shadows.
        /// </summary>
        public int evsmBlurPasses
        {
            get => m_EvsmBlurPasses;
            set => m_EvsmBlurPasses = Mathf.Clamp(value, k_MinEvsmBlurPasses, k_MaxEvsmBlurPasses);
        }

        [SerializeField, Range(HDShadowUtils.k_MinShadowNearPlane, HDShadowUtils.k_MaxShadowNearPlane)]
        float m_ShadowNearPlane = 0.1f;

        /// <summary>
        /// Controls the near plane distance of the shadows.
        /// </summary>
        public float shadowNearPlane
        {
            get => m_ShadowNearPlane;
            set => m_ShadowNearPlane = Mathf.Clamp(value, HDShadowUtils.k_MinShadowNearPlane, HDShadowUtils.k_MaxShadowNearPlane);
        }

        [SerializeField, Range(1, 64)]
        int m_BlockerSampleCount = 24;

        /// <summary>
        /// Controls the number of samples used to detect blockers for PCSS shadows.
        /// </summary>
        public int blockerSampleCount
        {
            get => m_BlockerSampleCount;
            set => m_BlockerSampleCount = Mathf.Clamp(value, 1, 64);
        }

        [SerializeField, Range(1, 64)]
        int m_FilterSampleCount = 16;

        /// <summary>
        /// Controls the number of samples used to filter for PCSS shadows.
        /// </summary>
        public int filterSampleCount
        {
            get => m_FilterSampleCount;
            set => m_FilterSampleCount = Mathf.Clamp(value, 1, 64);
        }

        [SerializeField, Range(0.0f, 1.0f)]
        float m_MinFilterSize = 0.1f;

        /// <summary>
        /// Controls the minimum filter size of PCSS shadows.
        /// </summary>
        public float minFilterSize
        {
            get => m_MinFilterSize;
            set => m_MinFilterSize = Mathf.Clamp(value, 0.0f, 1.0f);
        }

        [SerializeField, Range(0.0f, 1.0f)]
        float m_ShadowDimmer = 1.0f;

        /// <summary>
        /// Get/Set the shadow dimmer.
        /// </summary>
        public float shadowDimmer
        {
            get => m_ShadowDimmer;
            set => m_ShadowDimmer = Mathf.Clamp01(value);
        }

        [SerializeField, Min(0.0f)]
        float m_ShadowFadeDistance = 10000.0f;

        /// <summary>
        /// Shadow fade distance.
        /// </summary>
        public float shadowFadeDistance
        {
            get => m_ShadowFadeDistance;
            set => m_ShadowFadeDistance = Mathf.Max(value, 0.0f);
        }

        [SerializeField]
        Color m_ShadowTint = Color.black;

        /// <summary>
        /// Shadow tint.
        /// </summary>
        public Color shadowTint
        {
            get => m_ShadowTint;
            set => m_ShadowTint = value;
        }

        [SerializeField]
        bool m_PenumbraTint = false;

        /// <summary>
        /// Whether the shadow tint only affects the penumbra.
        /// </summary>
        public bool penumbraTint
        {
            get => m_PenumbraTint;
            set => m_PenumbraTint = value;
        }

        [SerializeField, Range(0.0f, 5.0f)]
        float m_NormalBias = 0.75f;

        /// <summary>
        /// Get/Set the normal bias of the shadow maps.
        /// </summary>
        public float normalBias
        {
            get => m_NormalBias;
            set => m_NormalBias = value;
        }

        [SerializeField, Range(0.0f, 1.0f)]
        float m_SlopeBias = 0.5f;

        /// <summary>
        /// Get/Set the slope bias of the shadow maps.
        /// </summary>
        public float slopeBias
        {
            get => m_SlopeBias;
            set => m_SlopeBias = value;
        }

        // This offset shift the position of the spotlight used to approximate the area light shadows. The offset is the minimum such that the full
        // area light shape is included in the cone spanned by the spot light.
        internal static float GetAreaLightOffsetForShadows(Vector2 shapeSize, float coneAngle)
        {
            float halfMinSize = Mathf.Min(shapeSize.x, shapeSize.y) * 0.5f;
            float halfAngle = coneAngle * 0.5f;
            float cotanHalfAngle = 1.0f / Mathf.Tan(halfAngle * Mathf.Deg2Rad);
            float offset = halfMinSize * cotanHalfAngle;

            return -offset;
        }

        internal Light attachedLight
        {
            get
            {
                if (!_light) _light = GetComponent<Light>();
                return _light;
            }
        }

        private Light _light;
    }
}
