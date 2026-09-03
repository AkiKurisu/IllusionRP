using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(DiffusionProfileAsset))]
    internal class DiffusionProfileAssetEditor : PropertyFetchEditor<DiffusionProfileAsset>
    {
        private sealed class Profile
        {
            internal DiffusionProfile objReference;

            internal SerializedProperty scatteringDistance;
            
            internal SerializedProperty scatteringDistanceMultiplier;
            
            internal SerializedProperty transmissionTint;
            
            // internal SerializedProperty texturingMode;
            
            // internal SerializedProperty transmissionMode;
            internal SerializedProperty thicknessRemap;
            
            internal SerializedProperty worldScale;
            
            internal SerializedProperty ior;

            // Render preview
            internal readonly RenderTexture profileRT;
            
            internal readonly RenderTexture transmittanceRT;

            internal Profile()
            {
                profileRT = new RenderTexture(256, 256, 0, GraphicsFormat.R16G16B16A16_SFloat);
                transmittanceRT = new RenderTexture(16, 256, 0, GraphicsFormat.R16G16B16A16_SFloat);
            }

            internal void Release()
            {
                CoreUtils.Destroy(profileRT);
                CoreUtils.Destroy(transmittanceRT);
            }
        }

        private Profile _profile;

        private Material _profileMaterial;

        private Material _transmittanceMaterial;

        private static Styles _styles;
                
        private static readonly int ShapeParam = Shader.PropertyToID("_ShapeParam");
        
        private static readonly int MaxRadius = Shader.PropertyToID("_MaxRadius");
        
        private static readonly int ThicknessRemap = Shader.PropertyToID("_ThicknessRemap");
        
        private static readonly int TransmissionTint = Shader.PropertyToID("_TransmissionTint");

        protected override void OnEnable()
        {
            base.OnEnable();

            // These shaders don't need to be reference by RenderPipelineResource as they are not use at runtime
            _profileMaterial = CoreUtils.CreateEngineMaterial("Hidden/DrawDiffusionProfile");
            _transmittanceMaterial = CoreUtils.CreateEngineMaterial("Hidden/DrawTransmittanceGraph");

            var serializedProfile = Properties.Find(x => x.profile);

            var rp = new RelativePropertyFetcher<DiffusionProfile>(serializedProfile);

            _profile = new Profile
            {
                objReference = Target.profile,
                scatteringDistance = rp.Find(x => x.scatteringDistance),
                scatteringDistanceMultiplier = rp.Find(x => x.scatteringDistanceMultiplier),
                transmissionTint = rp.Find(x => x.transmissionTint),
                // texturingMode = rp.Find(x => x.texturingMode),
                // transmissionMode = rp.Find(x => x.transmissionMode),
                thicknessRemap = rp.Find(x => x.thicknessRemap),
                worldScale = rp.Find(x => x.worldScale),
                ior = rp.Find(x => x.ior)
            };

            Undo.undoRedoPerformed += UpdateProfile;
        }

        private void OnDisable()
        {
            CoreUtils.Destroy(_profileMaterial);
            CoreUtils.Destroy(_transmittanceMaterial);

            _profile.Release();

            _profile = null;

            Undo.undoRedoPerformed -= UpdateProfile;
        }

        public override void OnInspectorGUI()
        {
            CheckStyles();

            serializedObject.Update();

            EditorGUILayout.Space();

            var profile = _profile;

            EditorGUI.indentLevel++;

            using (new EditorGUI.ChangeCheckScope())
            {
                EditorGUI.BeginChangeCheck();
                // For some reason the HDR picker is in gamma space, so convert to maintain same visual
                var color = EditorGUILayout.ColorField(_styles.ProfileScatteringColor, profile.scatteringDistance.colorValue.gamma, true, false, false);
                if (EditorGUI.EndChangeCheck())
                    profile.scatteringDistance.colorValue = color.linear;

                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.PropertyField(profile.scatteringDistanceMultiplier, _styles.ProfileScatteringDistanceMultiplier);

                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.FloatField(_styles.ProfileMaxRadius, profile.objReference.filterRadius);

                EditorGUILayout.Space();

                EditorGUILayout.Slider(profile.ior, 1.0f, 2.0f, _styles.ProfileIor);
                EditorGUILayout.PropertyField(profile.worldScale, _styles.ProfileWorldScale);

                // EditorGUILayout.Space();
                // EditorGUILayout.LabelField(s_Styles.SubsurfaceScatteringLabel, EditorStyles.boldLabel);
                //
                // profile.texturingMode.intValue = EditorGUILayout.Popup(s_Styles.texturingMode, profile.texturingMode.intValue, s_Styles.texturingModeOptions);
                //
                // EditorGUILayout.Space();
                // EditorGUILayout.LabelField(s_Styles.TransmissionLabel, EditorStyles.boldLabel);
                //
                // profile.transmissionMode.intValue = EditorGUILayout.Popup(s_Styles.profileTransmissionMode, profile.transmissionMode.intValue, s_Styles.transmissionModeOptions);

                EditorGUILayout.PropertyField(profile.transmissionTint, _styles.ProfileTransmissionTint);
                EditorGUILayout.PropertyField(profile.thicknessRemap, _styles.ProfileMinMaxThickness);
                var thicknessRemap = profile.thicknessRemap.vector2Value;
                EditorGUILayout.MinMaxSlider(_styles.ProfileThicknessRemap, ref thicknessRemap.x, ref thicknessRemap.y, 0f, 50f);
                profile.thicknessRemap.vector2Value = thicknessRemap;

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(_styles.ProfilePreview0, _styles.CenteredMiniBoldLabel);
                EditorGUILayout.LabelField(_styles.ProfilePreview1, EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField(_styles.ProfilePreview2, EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField(_styles.ProfilePreview3, EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.Space();

                serializedObject.ApplyModifiedProperties();

                // NOTE: We cannot change only upon scope changed since there is no callback when Reset is triggered for Editor and the scope is not changed when Reset is called.
                // The following operations are not super cheap, but are not overly expensive, so we instead trigger the change every time inspector is drawn.
                //    if (scope.changed)
                {
                    // Validate and update the cache for this profile only
                    profile.objReference.Validate();
                    Target.UpdateCache();
                }
            }

            RenderPreview(profile);

            EditorGUILayout.Space();
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }

        private void RenderPreview(Profile profile)
        {
            var obj = profile.objReference;
            float r = obj.filterRadius;
            var S = obj.shapeParam;

            _profileMaterial.SetFloat(MaxRadius, r);
            _profileMaterial.SetVector(ShapeParam, S);

            // Draw the profile.
            EditorGUI.DrawPreviewTexture(GUILayoutUtility.GetRect(256f, 256f), profile.profileRT, _profileMaterial, ScaleMode.ScaleToFit, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_styles.TransmittancePreview0, _styles.CenteredMiniBoldLabel);
            EditorGUILayout.LabelField(_styles.TransmittancePreview1, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField(_styles.TransmittancePreview2, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space();

            _transmittanceMaterial.SetVector(ShapeParam, S);
            _transmittanceMaterial.SetVector(TransmissionTint, obj.transmissionTint);
            _transmittanceMaterial.SetVector(ThicknessRemap, obj.thicknessRemap);

            // Draw the transmittance graph.
            EditorGUI.DrawPreviewTexture(GUILayoutUtility.GetRect(16f, 16f), profile.transmittanceRT, _transmittanceMaterial, ScaleMode.ScaleToFit, 16f);
        }

        private void UpdateProfile()
        {
            Target.profile.Validate();
            Target.UpdateCache();
        }
        
        // Can't use a static initializer in case we need to create GUIStyle in the Styles class as
        // these can only be created with an active GUI rendering context
        private static void CheckStyles()
        {
            _styles ??= new Styles();
        }

        private sealed class Styles
        {
            public readonly GUIContent ProfilePreview0 = new("Diffusion Profile Preview");
            
            public readonly GUIContent ProfilePreview1 = new("Shows the fraction of light scattered from the source (center).");
            
            public readonly GUIContent ProfilePreview2 = new("The distance to the boundary of the image corresponds to the Max Radius.");
            
            public readonly GUIContent ProfilePreview3 = new("Note that the intensity of pixels around the center may be clipped.");
            
            public readonly GUIContent TransmittancePreview0 = new("Transmittance Preview");
            
            public readonly GUIContent TransmittancePreview1 = new("Shows the fraction of light passing through the object for thickness values from the remap.");
            
            public readonly GUIContent TransmittancePreview2 = new("Can be viewed as a cross section of a slab of material illuminated by white light from the left.");
            
            public readonly GUIContent ProfileScatteringColor = new("Scattering Color", "Determines the shape of the profile. It should be similar to the diffuse color of the material.");
            
            public readonly GUIContent ProfileScatteringDistanceMultiplier = new("Multiplier", "Multiplier applied to the Scattering Color. Determines the effective radius of the filter.");
            
            public readonly GUIContent ProfileTransmissionTint = new("Transmission tint", "Color which tints transmitted light. Alpha is ignored.");
            
            public readonly GUIContent ProfileMaxRadius = new("Max Radius", "The maximum radius of the effect you define in Scattering Color and Multiplier.\nWhen the world scale is 1, this value is in millimeters.");
            
            // public readonly GUIContent texturingMode = new("Texturing Mode", "Specifies when the diffuse texture should be applied.");
            //
            // public readonly GUIContent[] texturingModeOptions = {
            //     new("Pre- and post-scatter", "HDRP performs texturing during both the lighting and the subsurface scattering passes. This blurs the diffuse texture. Choose this mode if your diffuse texture contains little to no subsurface scattering lighting."),
            //     new("Post-scatter",          "HDRP performs texturing only during the subsurface scattering pass. Preserves the sharpness of the diffuse texture. Choose this mode if your diffuse texture contains subsurface scattering lighting (for example a photo of skin).")
            // };
            //
            // public readonly GUIContent profileTransmissionMode = new("Transmission Mode", "Configures the simulation of light passing through thin objects. Depends on the thickness value (which HDRP applies in the normal direction).");
            //
            // public readonly GUIContent[] transmissionModeOptions = {
            //     new("Thick Object",      "Choose this mode for thick objects. For performance reasons, transmitted light ignores occlusion (shadows)."),
            //     new("Thin Object",       "Choose this mode for thin objects, such as paper or leaves. Transmitted light reuses the shadowing state of the surface.")
            // };
            
            public readonly GUIContent ProfileMinMaxThickness = new("Thickness Remap Values (Min-Max)", "Shows the values of the thickness remap below (in millimeters).");
            
            public readonly GUIContent ProfileThicknessRemap = new("Thickness Remap (Min-Max)", "Remaps the thickness parameter from [0, 1] to the desired range (in millimeters).");
            
            public readonly GUIContent ProfileWorldScale = new("World Scale", "Size of the world unit in meters.");
            
            public readonly GUIContent ProfileIor = new("Index of Refraction", "Select the index of refraction for this Diffusion Profile. For reference, skin is 1.4 and most materials are between 1.3 and 1.5.");
            
            public readonly GUIStyle CenteredMiniBoldLabel = new(GUI.skin.label);

            // public readonly GUIContent SubsurfaceScatteringLabel = new("Subsurface Scattering only");
            //
            // public readonly GUIContent TransmissionLabel = new("Transmission only");
            
            public Styles()
            {
                CenteredMiniBoldLabel.alignment = TextAnchor.MiddleCenter;
                CenteredMiniBoldLabel.fontSize = 10;
                CenteredMiniBoldLabel.fontStyle = FontStyle.Bold;
            }
        }
    }
}