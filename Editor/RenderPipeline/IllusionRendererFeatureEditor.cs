using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(IllusionRendererFeature))]
    internal class IllusionRendererFeatureEditor : PropertyFetchEditor<IllusionRendererFeature>
    {
        // General Settings
        private SerializedProperty _requireHistoryColor;
        private SerializedProperty _requireEarlyMotionVector;
        private SerializedProperty _preferComputeShader;

        // Transparency Settings
        private SerializedProperty _orderIndependentTransparency;
        private SerializedProperty _oitFilterLayer;
        private SerializedProperty _oitOverrideStencil;
        private SerializedProperty _transparentDepthPostPass;
        private SerializedProperty _oitTransparentOverdrawPass;

        // Character Rendering Settings
        private SerializedProperty _subsurfaceScattering;

        // Shadow Settings
        private SerializedProperty _perObjectShadowRenderingLayer;
        private SerializedProperty _transparentReceivePerObjectShadows;
        private SerializedProperty _contactShadows;
        private SerializedProperty _pcssShadows;
        private SerializedProperty _fragmentShadowBias;

        // Ambient Occlusion Settings
        private SerializedProperty _groundTruthAO;

        // Global Illumination Settings
        private SerializedProperty _screenSpaceReflection;
        private SerializedProperty _screenSpaceGlobalIllumination;
        private SerializedProperty _precomputedRadianceTransferGI;
        private SerializedProperty _enableIndirectDiffuseRenderingLayers;

        // Post Processing Settings
        private SerializedProperty _convolutionBloom;
        private SerializedProperty _volumetricFog;

        protected override void OnEnable()
        {
            base.OnEnable();

            // General Settings
            _requireHistoryColor = Properties.Find(feature => feature.requireHistoryColor);
            _requireEarlyMotionVector = Properties.Find(feature => feature.requireEarlyMotionVector);
            _preferComputeShader = Properties.Find(feature => feature.preferComputeShader);

            // Transparency Settings
            _orderIndependentTransparency = Properties.Find(feature => feature.orderIndependentTransparency);
            _oitFilterLayer = Properties.Find(feature => feature.oitFilterLayer);
            _oitOverrideStencil = Properties.Find(feature => feature.oitOverrideStencil);
            _transparentDepthPostPass = Properties.Find(feature => feature.transparentDepthPostPass);
            _oitTransparentOverdrawPass = Properties.Find(feature => feature.oitTransparentOverdrawPass);

            // Character Rendering Settings
            _subsurfaceScattering = Properties.Find(feature => feature.subsurfaceScattering);

            // Shadow Settings
            _perObjectShadowRenderingLayer = Properties.Find(feature => feature.perObjectShadowRenderingLayer);
            _transparentReceivePerObjectShadows = Properties.Find(feature => feature.transparentReceivePerObjectShadows);
            _contactShadows = Properties.Find(feature => feature.contactShadows);
            _pcssShadows = Properties.Find(feature => feature.pcssShadows);
            _fragmentShadowBias = Properties.Find(feature => feature.fragmentShadowBias);

            // Ambient Occlusion Settings
            _groundTruthAO = Properties.Find(feature => feature.groundTruthAO);

            // Global Illumination Settings
            _screenSpaceReflection = Properties.Find(feature => feature.screenSpaceReflection);
            _screenSpaceGlobalIllumination = Properties.Find(feature => feature.screenSpaceGlobalIllumination);
            _precomputedRadianceTransferGI = Properties.Find(feature => feature.precomputedRadianceTransferGI);
            _enableIndirectDiffuseRenderingLayers = Properties.Find(feature => feature.enableIndirectDiffuseRenderingLayers);

            // Post Processing Settings
            _convolutionBloom = Properties.Find(feature => feature.convolutionBloom);
            _volumetricFog = Properties.Find(feature => feature.volumetricFog);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            
            // General Settings
            DrawGeneralSettings();

            // Transparency Settings
            DrawTransparencySettings();

            // Character Rendering Settings
            DrawCharacterRenderingSettings();

            // Shadow Settings
            DrawShadowSettings();

            // Ambient Occlusion Settings
            DrawAmbientOcclusionSettings();

            // Global Illumination Settings
            DrawGlobalIlluminationSettings();

            // Post Processing Settings
            DrawPostProcessingSettings();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneralSettings()
        {
            if (Foldout("General", true))
            {
                EditorGUILayout.PropertyField(_requireHistoryColor, Styles.RequireHistoryColorLabel);
                EditorGUILayout.PropertyField(_requireEarlyMotionVector, Styles.RequireEarlyMotionVectorLabel);
                EditorGUILayout.PropertyField(_preferComputeShader, Styles.PreferComputeShaderLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawTransparencySettings()
        {
            if (Foldout("Transparency", true))
            {
                EditorGUILayout.PropertyField(_transparentDepthPostPass, Styles.TransparentDepthPostPassLabel);
                EditorGUILayout.PropertyField(_orderIndependentTransparency, Styles.OrderIndependentTransparencyLabel);
                if (_orderIndependentTransparency.boolValue)
                {
                    EditorGUILayout.PropertyField(_oitFilterLayer, Styles.OitFilterLayerLabel);
                    EditorGUILayout.PropertyField(_oitOverrideStencil, Styles.OitOverrideStencilLabel);
                    EditorGUILayout.PropertyField(_oitTransparentOverdrawPass, Styles.OitTransparentOverdrawPassLabel);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawCharacterRenderingSettings()
        {
            if (Foldout("Lighting", true))
            {
                EditorGUILayout.PropertyField(_subsurfaceScattering, Styles.SubsurfaceScatteringLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawShadowSettings()
        {
            if (Foldout("Shadows", true))
            {
                EditorGUILayout.PropertyField(_perObjectShadowRenderingLayer, Styles.PerObjectShadowRenderingLayerLabel);
                EditorGUILayout.PropertyField(_transparentReceivePerObjectShadows, Styles.TransparentReceivePerObjectShadowsLabel);
                EditorGUILayout.PropertyField(_pcssShadows, Styles.PcssShadowsLabel);
                EditorGUILayout.PropertyField(_contactShadows, Styles.ContactShadowsLabel);
                EditorGUILayout.PropertyField(_fragmentShadowBias, Styles.FragmentShadowBiasLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawAmbientOcclusionSettings()
        {
            if (Foldout("Ambient Occlusion", true))
            {
                EditorGUILayout.PropertyField(_groundTruthAO, Styles.GroundTruthAOLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawGlobalIlluminationSettings()
        {
            if (Foldout("Global Illumination", true))
            {
                EditorGUILayout.PropertyField(_screenSpaceReflection, Styles.ScreenSpaceReflectionLabel);
                EditorGUILayout.PropertyField(_screenSpaceGlobalIllumination, Styles.ScreenSpaceGlobalIlluminationLabel);
                EditorGUILayout.PropertyField(_precomputedRadianceTransferGI, Styles.PrecomputedRadianceTransferGILabel);
                EditorGUILayout.PropertyField(_enableIndirectDiffuseRenderingLayers, Styles.EnableIndirectDiffuseRenderingLayersLabel);
            }

            EditorGUILayout.Space();
        }

        private void DrawPostProcessingSettings()
        {
            if (Foldout("Post Processing", true))
            {
                EditorGUILayout.PropertyField(_convolutionBloom, Styles.ConvolutionBloomLabel);
                EditorGUILayout.PropertyField(_volumetricFog, Styles.VolumetricFogLabel);
            }
        }

        private static class Styles
        {
            // General Settings
            public static readonly GUIContent RequireHistoryColorLabel = new("Require History Color",
                "If this is enabled, the camera copies the last rendered view so it can be accessed at next frame in the pipeline." +
                "When temporal anti-aliasing is on, history color will fetch accumulation buffer directly.");
            public static readonly GUIContent RequireEarlyMotionVectorLabel = new("Require Early Motion Vector",
                "Enable to draw motion vector before rendering objects. Please note that motion vector will be drawn twice when using RenderGraph.");
            public static readonly GUIContent PreferComputeShaderLabel = new("Prefer Compute Shader",
                "Whether prefer to calculating effects in compute shader if possible.");

            // Transparency Settings
            public static readonly GUIContent OrderIndependentTransparencyLabel = new("Order Independent Transparency",
                "Enable Weighted Blended Order-Independent Transparency which will solve transparent objects rendering order problem.");
            public static readonly GUIContent OitFilterLayerLabel = new("OIT Filter Layer",
                "Configure the rendering objects LayerMask for the oit passes.");
            public static readonly GUIContent OitOverrideStencilLabel = new("OIT Override Stencil",
                "Override the stencil state for the transparent overdraw pass.");
            public static readonly GUIContent TransparentDepthPostPassLabel = new("Transparent Depth Post Pass",
                "Enable to write transparent depth after depth prepass.");
            public static readonly GUIContent OitTransparentOverdrawPassLabel = new("OIT Transparent Overdraw Pass",
                "Enable to overdraw universal transparent objects after rendering OIT objects.");

            // Lighting Settings
            public static readonly GUIContent SubsurfaceScatteringLabel = new("Subsurface Scattering",
                "Enable Screen Space Subsurface Scattering.");

            // Shadow Settings
            public static readonly GUIContent PerObjectShadowRenderingLayerLabel = new("Per Object Shadow Rendering Layer",
                "Rendering layer for per object shadow to prevent shadow overdraw in main light caster.");
            public static readonly GUIContent TransparentReceivePerObjectShadowsLabel = new("Transparent Receive Per Object Shadows",
                "When enabled, transparent objects will sample per object shadow.");
            public static readonly GUIContent ContactShadowsLabel = new("Contact Shadows",
                "Enable contact shadows feature.");
            public static readonly GUIContent PcssShadowsLabel = new("PCSS Shadows",
                "Enable Percentage Closer Soft Shadows.");
            public static readonly GUIContent FragmentShadowBiasLabel = new("Fragment Shadow Bias",
                "Enable Fragment Shadow Bias in receiver pass instead of caster pass, notice this will let IllusionRP shaders not compatible to URP original shaders.");

            // Ambient Occlusion Settings
            public static readonly GUIContent GroundTruthAOLabel = new("Ground Truth AO",
                "Enable ground truth ambient occlusion.");

            // Global Illumination Settings
            public static readonly GUIContent ScreenSpaceReflectionLabel = new("Screen Space Reflection",
                "Enable screen space reflection.");
            public static readonly GUIContent ScreenSpaceGlobalIlluminationLabel = new("Screen Space GI",
                "Enable screen space global illumination.");
            public static readonly GUIContent PrecomputedRadianceTransferGILabel = new("Precomputed Radiance Transfer GI",
                "Enable precomputed radiance transfer global illumination.");
            public static readonly GUIContent EnableIndirectDiffuseRenderingLayersLabel = new("Enable Indirect Diffuse Rendering Layers",
                "Enable using main light rendering layers to control indirect diffuse intensity.");

            // Post Processing Settings
            public static readonly GUIContent ConvolutionBloomLabel = new("Convolution Bloom",
                "Enable high-quality bloom effect using Fast Fourier Transform convolution.");
            public static readonly GUIContent VolumetricFogLabel = new("Volumetric Fog",
                "Enable volumetric fog effect.");
        }
    }
}


