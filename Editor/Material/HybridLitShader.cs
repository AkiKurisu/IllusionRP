using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    /// <summary>
    /// Class for shader GUI for Hybrid Lit shader in IllusionRP.
    /// </summary>
    public class HybridLitShader : BaseShaderGUI
    {
        private struct LitHybridProperties
        {
            public readonly MaterialProperty OrderIndependentProp;

            public readonly MaterialProperty ScreenSpaceReflectionsProp;

            public readonly MaterialProperty ScreenSpaceAmbientOcclusionProp;

            public LitHybridProperties(MaterialProperty[] properties)
            {
                OrderIndependentProp = FindProperty("_OrderIndependent", properties, false);

                ScreenSpaceReflectionsProp = FindProperty("_ScreenSpaceReflections", properties, false);

                ScreenSpaceAmbientOcclusionProp = FindProperty("_ScreenSpaceAmbientOcclusion", properties, false);
            }
        }

        private new static class Styles
        {
            public static readonly GUIContent OrderIndependentText =
                EditorGUIUtility.TrTextContent("Order Independent Transparency",
                    "When enabled, the transparent Material will be drawn after accumulation and no need to set render queue order.");

            public static readonly GUIContent ScreenSpaceReflectionsText =
                EditorGUIUtility.TrTextContent("Screen Space Reflections",
                    "When enabled, the Material will sample screen space reflections.");

            public static readonly GUIContent ScreenSpaceAmbientOcclusionText =
                EditorGUIUtility.TrTextContent("Screen Space Ambient Occlusion",
                    "When enabled, the Material will receive screen space ambient occlusion.");
        }

        private static readonly string[] WorkflowModeNames = Enum.GetNames(typeof(LitGUI.WorkflowMode));

        private LitGUI.LitProperties _litProperties;

        private LitDetailGUI.LitProperties _litDetailProperties;

        private LitHybridProperties _litHybridProperties;

        public override void FillAdditionalFoldouts(MaterialHeaderScopeList materialScopesList)
        {
            materialScopesList.RegisterHeaderScope(LitDetailGUI.Styles.detailInputs, Expandable.Details, _ => LitDetailGUI.DoDetailArea(_litDetailProperties, materialEditor));
        }

        // collect properties from the material properties
        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            _litProperties = new LitGUI.LitProperties(properties);
            _litDetailProperties = new LitDetailGUI.LitProperties(properties);
            _litHybridProperties = new LitHybridProperties(properties);
        }

        // material changed check
        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material, SetMaterialKeywords, LitDetailGUI.SetMaterialKeywords);
        }

        private void SetMaterialKeywords(Material material)
        {
            LitGUI.SetMaterialKeywords(material);

            if (surfaceTypeProp != null)
            {
                if ((SurfaceType)surfaceTypeProp.floatValue == SurfaceType.Transparent)
                {
                    if (material.HasProperty(IllusionShaderProperties.OrderIndependent))
                    {
                        var hasOrderIndependent =
                            Mathf.Approximately(material.GetFloat(IllusionShaderProperties.OrderIndependent), 1.0f);
                        material.SetShaderPassEnabled("UniversalForward", !hasOrderIndependent);
                        material.SetShaderPassEnabled("OIT", hasOrderIndependent);
                    }
                }
                else
                {
                    material.SetShaderPassEnabled("OIT", false);
                }
            }


            if (material.HasProperty(IllusionShaderProperties.CastPerObjectShadow))
            {
                bool hasPerObjectShadow = Mathf.Approximately(material.GetFloat(IllusionShaderProperties.CastPerObjectShadow), 1.0f);
                material.SetShaderPassEnabled("PerObjectShadowCaster", hasPerObjectShadow);
                // Disable main shadow caster to prevent shadow overdraw
                material.SetShaderPassEnabled("ShadowCaster", !hasPerObjectShadow);
            }

            int stencilRefDepth = 0;
            int stencilWriteMaskDepth = 0;
            int stencilRefGBuffer = 0;
            int stencilWriteMaskGBuffer = 0;
            stencilWriteMaskDepth |= (int)IllusionStencilUsage.NotReceiveAmbientOcclusion;
            stencilWriteMaskGBuffer |= (int)IllusionStencilUsage.TraceReflectionRay;

            if (material.HasProperty(IllusionShaderProperties.ScreenSpaceReflections))
            {
                var receiveSSR = Mathf.Approximately(material.GetFloat(IllusionShaderProperties.ScreenSpaceReflections), 1.0f);
                if (receiveSSR)
                {
                    stencilRefGBuffer |= (int)IllusionStencilUsage.TraceReflectionRay;
                }
            }

            if (material.HasProperty(IllusionShaderProperties.ScreenSpaceAmbientOcclusion))
            {
                bool receiveSSAO = Mathf.Approximately(material.GetFloat(IllusionShaderProperties.ScreenSpaceAmbientOcclusion), 1.0f);
                if (!receiveSSAO)
                {
                    stencilRefDepth |= (int)IllusionStencilUsage.NotReceiveAmbientOcclusion;
                }
            }

            if (material.HasProperty(IllusionShaderProperties.StencilRefDepth) && material.HasProperty(IllusionShaderProperties.StencilWriteMaskDepth))
            {
                material.SetInt(IllusionShaderProperties.StencilRefDepth, stencilRefDepth);
                material.SetInt(IllusionShaderProperties.StencilWriteMaskDepth, stencilWriteMaskDepth);
            }

            if (material.HasProperty(IllusionShaderProperties.StencilRefGBuffer) && material.HasProperty(IllusionShaderProperties.StencilWriteMaskGBuffer))
            {
                material.SetInt(IllusionShaderProperties.StencilRefGBuffer, stencilRefGBuffer);
                material.SetInt(IllusionShaderProperties.StencilWriteMaskGBuffer, stencilWriteMaskGBuffer);
            }
        }

        // material main surface options
        public override void DrawSurfaceOptions(Material material)
        {
            // Use default labelWidth
            EditorGUIUtility.labelWidth = 0f;

            if (_litProperties.workflowMode != null)
                DoPopup(LitGUI.Styles.workflowModeText, _litProperties.workflowMode, WorkflowModeNames);

            base.DrawSurfaceOptions(material);
        }

        // material main surface inputs
        public override void DrawSurfaceInputs(Material material)
        {
            base.DrawSurfaceInputs(material);
            LitGUI.Inputs(_litProperties, materialEditor, material);
            DrawEmissionProperties(material, true);
            DrawTileOffset(materialEditor, baseMapProp);
        }

        // material main advanced options
        public override void DrawAdvancedOptions(Material material)
        {
            if (_litProperties.highlights != null)
            {
                materialEditor.ShaderProperty(_litProperties.highlights, LitGUI.Styles.highlightsText);
            }

            if (_litProperties.reflections != null)
            {
                materialEditor.ShaderProperty(_litProperties.reflections, LitGUI.Styles.reflectionsText);
            }

            bool showQueueControl = true;
            if (surfaceTypeProp != null && (SurfaceType)surfaceTypeProp.floatValue == SurfaceType.Transparent)
            {
                if (_litHybridProperties.OrderIndependentProp != null)
                {
                    materialEditor.ShaderProperty(_litHybridProperties.OrderIndependentProp, Styles.OrderIndependentText);
                    if (Mathf.Approximately(_litHybridProperties.OrderIndependentProp.floatValue, 1))
                    {
                        showQueueControl = false;
                    }
                }
            }

            DrawFloatToggleProperty(Styles.ScreenSpaceReflectionsText, _litHybridProperties.ScreenSpaceReflectionsProp);

            DrawFloatToggleProperty(Styles.ScreenSpaceAmbientOcclusionText, _litHybridProperties.ScreenSpaceAmbientOcclusionProp);

            if (showQueueControl)
            {
                // Only draw the sorting priority field if queue control is set to "auto"
                bool autoQueueControl = GetAutomaticQueueControlSetting(material);
                if (autoQueueControl)
                    DrawQueueOffsetField();
            }
            materialEditor.EnableInstancingField();
        }
    }
}
