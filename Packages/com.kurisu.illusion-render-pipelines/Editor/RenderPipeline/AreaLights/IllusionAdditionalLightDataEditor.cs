using Illusion.Rendering.AreaLights;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    // @IllusionRP: the shadow tier settings shown depend on the renderer feature's area shadow filtering quality.
    // The URP light inspector hides realtime shadow settings for rectangle lights, so shadow on/off lives here as well.
    [CustomEditor(typeof(IllusionAdditionalLightData))]
    [CanEditMultipleObjects]
    internal class IllusionAdditionalLightDataEditor : PropertyFetchEditor<IllusionAdditionalLightData>
    {
        private SerializedProperty _lightDimmer;
        private SerializedProperty _affectDiffuse;
        private SerializedProperty _affectSpecular;
        private SerializedProperty _applyRangeAttenuation;
        private SerializedProperty _fadeDistance;
        private SerializedProperty _barnDoorAngle;
        private SerializedProperty _barnDoorLength;
        private SerializedProperty _areaLightCookie;

        private SerializedProperty _shadowResolution;
        private SerializedProperty _areaLightShadowCone;
        private SerializedProperty _shadowNearPlane;
        private SerializedProperty _shadowDimmer;
        private SerializedProperty _shadowFadeDistance;
        private SerializedProperty _shadowTint;
        private SerializedProperty _penumbraTint;

        private SerializedProperty _slopeBias;
        private SerializedProperty _normalBias;
        private SerializedProperty _blockerSampleCount;
        private SerializedProperty _filterSampleCount;
        private SerializedProperty _minFilterSize;
        private SerializedProperty _shapeRadius;
        private SerializedProperty _softnessScale;

        private SerializedProperty _evsmExponent;
        private SerializedProperty _evsmLightLeakBias;
        private SerializedProperty _evsmVarianceBias;
        private SerializedProperty _evsmBlurPasses;

        private SerializedObject _lightObject;
        private SerializedProperty _lightShadows;

        protected override void OnEnable()
        {
            base.OnEnable();
            _lightDimmer = serializedObject.FindProperty("m_LightDimmer");
            _affectDiffuse = serializedObject.FindProperty("m_AffectDiffuse");
            _affectSpecular = serializedObject.FindProperty("m_AffectSpecular");
            _applyRangeAttenuation = serializedObject.FindProperty("m_ApplyRangeAttenuation");
            _fadeDistance = serializedObject.FindProperty("m_FadeDistance");
            _barnDoorAngle = serializedObject.FindProperty("m_BarnDoorAngle");
            _barnDoorLength = serializedObject.FindProperty("m_BarnDoorLength");
            _areaLightCookie = serializedObject.FindProperty("m_AreaLightCookie");

            _shadowResolution = serializedObject.FindProperty("m_ShadowResolution");
            _areaLightShadowCone = serializedObject.FindProperty("m_AreaLightShadowCone");
            _shadowNearPlane = serializedObject.FindProperty("m_ShadowNearPlane");
            _shadowDimmer = serializedObject.FindProperty("m_ShadowDimmer");
            _shadowFadeDistance = serializedObject.FindProperty("m_ShadowFadeDistance");
            _shadowTint = serializedObject.FindProperty("m_ShadowTint");
            _penumbraTint = serializedObject.FindProperty("m_PenumbraTint");

            _slopeBias = serializedObject.FindProperty("m_SlopeBias");
            _normalBias = serializedObject.FindProperty("m_NormalBias");
            _blockerSampleCount = serializedObject.FindProperty("m_BlockerSampleCount");
            _filterSampleCount = serializedObject.FindProperty("m_FilterSampleCount");
            _minFilterSize = serializedObject.FindProperty("m_MinFilterSize");
            _shapeRadius = serializedObject.FindProperty("m_ShapeRadius");
            _softnessScale = serializedObject.FindProperty("m_SoftnessScale");

            _evsmExponent = serializedObject.FindProperty("m_EvsmExponent");
            _evsmLightLeakBias = serializedObject.FindProperty("m_EvsmLightLeakBias");
            _evsmVarianceBias = serializedObject.FindProperty("m_EvsmVarianceBias");
            _evsmBlurPasses = serializedObject.FindProperty("m_EvsmBlurPasses");

            var lights = new Object[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                lights[i] = ((IllusionAdditionalLightData)targets[i]).GetComponent<Light>();
            _lightObject = new SerializedObject(lights);
            _lightShadows = _lightObject.FindProperty("m_Shadows.m_Type");
        }

        private void OnDisable()
        {
            _lightObject?.Dispose();
            _lightObject = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _lightObject.Update();

            Light light = Target.GetComponent<Light>();
            if (light && light.type != LightType.Rectangle)
            {
                EditorGUILayout.HelpBox("IllusionRP area lighting only evaluates Rectangle lights.", MessageType.Warning);
            }

            if (Foldout("Light", true))
            {
                DrawRenderingLayers();
                EditorGUILayout.PropertyField(_lightDimmer, Styles.LightDimmer);
                EditorGUILayout.PropertyField(_affectDiffuse, Styles.AffectDiffuse);
                EditorGUILayout.PropertyField(_affectSpecular, Styles.AffectSpecular);
                EditorGUILayout.PropertyField(_applyRangeAttenuation, Styles.ApplyRangeAttenuation);
                EditorGUILayout.PropertyField(_fadeDistance, Styles.FadeDistance);
                EditorGUILayout.PropertyField(_barnDoorAngle, Styles.BarnDoorAngle);
                EditorGUILayout.PropertyField(_barnDoorLength, Styles.BarnDoorLength);
                EditorGUILayout.PropertyField(_areaLightCookie, Styles.AreaLightCookie);
                ShowCookieTextureWarnings(_areaLightCookie.objectReferenceValue as Texture);
            }

            EditorGUILayout.Space();

            if (Foldout("Shadows", true))
            {
                DrawShadowSettings();
            }

            _lightObject.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
        }

        // The URP light inspector hides Rendering Layers for area lights, but the area light loop applies them
        // like every other URP light, so they are edited here through UniversalAdditionalLightData.
        private void DrawRenderingLayers()
        {
            var first = GetUrpLightData(targets[0]);
            if (first == null)
                return;

            bool mixed = false;
            for (int i = 1; i < targets.Length; i++)
            {
                var other = GetUrpLightData(targets[i]);
                mixed |= other == null || other.renderingLayers != first.renderingLayers;
            }

            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            RenderingLayerMask mask = EditorGUILayout.RenderingLayerMaskField(Styles.RenderingLayers, first.renderingLayers);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (!changed)
                return;

            foreach (var target in targets)
            {
                var urpData = GetUrpLightData(target);
                if (urpData == null)
                    continue;
                Undo.RecordObject(urpData, "Change Rendering Layers");
                Undo.RecordObject(urpData.GetComponent<Light>(), "Change Rendering Layers");
                urpData.renderingLayers = mask;
                EditorUtility.SetDirty(urpData);
            }
        }

        private static UniversalAdditionalLightData GetUrpLightData(Object target)
        {
            var light = ((IllusionAdditionalLightData)target).GetComponent<Light>();
            return light ? light.GetUniversalAdditionalLightData() : null;
        }

        private void DrawShadowSettings()
        {
            bool shadowsEnabled = _lightShadows.intValue != (int)LightShadows.None;
            EditorGUI.showMixedValue = _lightShadows.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            shadowsEnabled = EditorGUILayout.Toggle(Styles.EnableShadows, shadowsEnabled);
            if (EditorGUI.EndChangeCheck())
                _lightShadows.intValue = (int)(shadowsEnabled ? LightShadows.Soft : LightShadows.None);
            EditorGUI.showMixedValue = false;

            bool hasFeature = IllusionRendererFeatureUtility.TryGetDefault(out IllusionRendererFeature feature);
            HDAreaShadowFilteringQuality quality = hasFeature
                ? feature.areaShadowFilteringQuality
                : HDAreaShadowFilteringQuality.Medium;

            using (new EditorGUI.DisabledScope(!shadowsEnabled))
            {
                EditorGUILayout.PropertyField(_shadowResolution, Styles.ShadowResolution);
                EditorGUILayout.PropertyField(_areaLightShadowCone, Styles.AreaLightShadowCone);
                EditorGUILayout.PropertyField(_shadowNearPlane, Styles.ShadowNearPlane);
                EditorGUILayout.PropertyField(_shadowDimmer, Styles.ShadowDimmer);
                EditorGUILayout.PropertyField(_shadowFadeDistance, Styles.ShadowFadeDistance);
                EditorGUILayout.PropertyField(_shadowTint, Styles.ShadowTint);
                EditorGUILayout.PropertyField(_penumbraTint, Styles.PenumbraTint);

                if (!hasFeature)
                {
                    EditorGUILayout.HelpBox("No Illusion Renderer Feature on the default renderer, showing both area shadow filtering tiers.", MessageType.Info);
                }

                if (!hasFeature || quality == HDAreaShadowFilteringQuality.High)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(Styles.PcssHeader, EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_slopeBias, Styles.SlopeBias);
                    EditorGUILayout.PropertyField(_normalBias, Styles.NormalBias);
                    EditorGUILayout.PropertyField(_blockerSampleCount, Styles.BlockerSampleCount);
                    EditorGUILayout.PropertyField(_filterSampleCount, Styles.FilterSampleCount);
                    EditorGUILayout.PropertyField(_minFilterSize, Styles.MinFilterSize);
                    EditorGUILayout.PropertyField(_shapeRadius, Styles.ShapeRadius);
                    EditorGUILayout.PropertyField(_softnessScale, Styles.SoftnessScale);
                }

                if (!hasFeature || quality == HDAreaShadowFilteringQuality.Medium)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(Styles.EvsmHeader, EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_evsmExponent, Styles.EvsmExponent);
                    EditorGUILayout.PropertyField(_evsmLightLeakBias, Styles.EvsmLightLeakBias);
                    EditorGUILayout.PropertyField(_evsmVarianceBias, Styles.EvsmVarianceBias);
                    EditorGUILayout.PropertyField(_evsmBlurPasses, Styles.EvsmBlurPasses);
                }
            }
        }

        static void ShowCookieTextureWarnings(Texture cookie)
        {
            if (cookie == null)
                return;

            if (cookie.dimension != TextureDimension.Tex2D)
            {
                EditorGUILayout.HelpBox(Styles.CookieNot2D, MessageType.Error);
                return;
            }

            // The texture type is stored in the texture importer so we need to get it:
            TextureImporter texImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(cookie)) as TextureImporter;

            if (texImporter != null && texImporter.textureType == TextureImporterType.Cookie)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int indentSpace = (int)EditorGUI.IndentedRect(new Rect()).x;
                    GUILayout.Space(indentSpace);
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        int oldIndentLevel = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        GUIStyle wordWrap = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                        EditorGUILayout.LabelField(Styles.CookieTextureTypeError, wordWrap);
                        if (GUILayout.Button("Fix", GUILayout.ExpandHeight(true)))
                        {
                            texImporter.textureType = TextureImporterType.Default;
                            texImporter.SaveAndReimport();
                        }
                        EditorGUI.indentLevel = oldIndentLevel;
                    }
                }
            }

            if (cookie.width != cookie.height)
                EditorGUILayout.HelpBox(Styles.CookieNonPOT, MessageType.Warning);
            if (cookie.width < AreaLightCookieManager.k_MinCookieSize || cookie.height < AreaLightCookieManager.k_MinCookieSize)
                EditorGUILayout.HelpBox(Styles.CookieTooSmall, MessageType.Warning);
        }

        private static class Styles
        {
            public static readonly GUIContent RenderingLayers = new("Rendering Layers", "Renderers on these layers receive this area light (UniversalAdditionalLightData.renderingLayers).");
            public static readonly GUIContent LightDimmer = new("Intensity Multiplier", "Multiplies the light intensity.");
            public static readonly GUIContent AffectDiffuse = new("Affect Diffuse", "When enabled, the light affects diffuse lighting.");
            public static readonly GUIContent AffectSpecular = new("Affect Specular", "When enabled, the light affects specular lighting.");
            public static readonly GUIContent ApplyRangeAttenuation = new("Range Attenuation", "Smoothly fades the light to zero at its range.");
            public static readonly GUIContent FadeDistance = new("Fade Distance", "Distance from the camera at which the light is faded out.");
            public static readonly GUIContent BarnDoorAngle = new("Barn Door Angle", "Angle of the barn doors, 90 disables them.");
            public static readonly GUIContent BarnDoorLength = new("Barn Door Length", "Length of the barn doors.");
            public static readonly GUIContent AreaLightCookie = new("Cookie", "Cookie mask currently assigned to the area light.");
            public static readonly GUIContent CookieTextureTypeError = new("IllusionRP does not support the Cookie Texture type, only Default is supported.", EditorGUIUtility.IconContent("console.warnicon").image);
            public static readonly string CookieNot2D = "Area light cookies must be 2D textures.";
            public static readonly string CookieNonPOT = "IllusionRP does not support non power of two cookie textures.";
            public static readonly string CookieTooSmall = "Min texture size for cookies is 2x2 pixels.";

            public static readonly GUIContent EnableShadows = new("Enable Shadows", "Renders an area shadow map for this light (Light.shadows).");
            public static readonly GUIContent ShadowResolution = new("Resolution", "Requested shadow map resolution, clamped by the Area Lighting volume.");
            public static readonly GUIContent AreaLightShadowCone = new("Shadow Cone", "Angular size of the cone used to approximate the area light shadow.");
            public static readonly GUIContent ShadowNearPlane = new("Near Plane", "Near plane distance of the shadow projection.");
            public static readonly GUIContent ShadowDimmer = new("Shadow Dimmer", "Dims the shadow.");
            public static readonly GUIContent ShadowFadeDistance = new("Shadow Fade Distance", "Distance from the camera at which the shadow is faded out.");
            public static readonly GUIContent ShadowTint = new("Shadow Tint", "Tint applied to the shadow.");
            public static readonly GUIContent PenumbraTint = new("Penumbra Tint", "Applies the tint to the penumbra only.");

            public static readonly GUIContent PcssHeader = new("PCSS");
            public static readonly GUIContent SlopeBias = new("Slope Bias", "Slope scale depth bias applied while rendering the shadow map.");
            public static readonly GUIContent NormalBias = new("Normal Bias", "Receiver normal bias.");
            public static readonly GUIContent BlockerSampleCount = new("Blocker Sample Count", "Samples used to search blockers.");
            public static readonly GUIContent FilterSampleCount = new("Filter Sample Count", "Samples used to filter the shadow.");
            public static readonly GUIContent MinFilterSize = new("Min Filter Size", "Minimum penumbra size.");
            public static readonly GUIContent ShapeRadius = new("Softness Radius", "Emissive radius used to derive the PCSS softness.");
            public static readonly GUIContent SoftnessScale = new("Softness Scale", "Scales the PCSS softness.");

            public static readonly GUIContent EvsmHeader = new("EVSM");
            public static readonly GUIContent EvsmExponent = new("Exponent", "EVSM warp exponent.");
            public static readonly GUIContent EvsmLightLeakBias = new("Light Leak Bias", "Reduces light leaking.");
            public static readonly GUIContent EvsmVarianceBias = new("Variance Bias", "Minimum variance.");
            public static readonly GUIContent EvsmBlurPasses = new("Blur Passes", "Number of blur passes applied to the moments.");
        }
    }
}
