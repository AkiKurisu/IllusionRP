using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering.Universal;
using PrefilterMode = Illusion.Rendering.IllusionRendererFeature.PrefilterMode;

namespace Illusion.Rendering.Editor
{
    [Flags]
    internal enum ShaderFeatures : long
    {
        None = 0,
        ScreenSpaceReflection = (1L << 0),
        ScreenSpaceGlobalIllumination = (1L << 1),
        ScreenSpaceOcclusion = (1L << 2),
        MainLightShadowsScreen = (1L << 3),
        PrecomputedRadianceTransferGI = (1L << 4),
        ScreenSpaceSubsurfaceScattering = (1L << 5),
        OrderIndependentTransparency = (1L << 6),
        TransparentPerObjectShadow = (1L << 7),
        FragmentShadowBias = (1L << 8),
        // Unused = (1L << 9),
        // Unused = (1L << 10),
        // Unused = (1L << 11),
        // Unused = (1L << 12),
        All = ~0
    }

    /// <summary>
    /// This class is used solely to make sure Shader Prefiltering data inside the
    /// URP Assets get updated before anything (Like Asset Bundles) are built.
    /// </summary>
    internal class UpdateShaderPrefilteringDataBeforeBuild : IPreprocessShaders
    {
        public int callbackOrder => -99; // After URP

        public UpdateShaderPrefilteringDataBeforeBuild()
        {
            ShaderBuildPreprocessor.GatherShaderFeatures();
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippetData, IList<ShaderCompilerData> compilerDataList){}
    }
    
    public class ShaderBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private static readonly List<ShaderFeatures> m_SupportedFeaturesList = new();

        internal static List<ShaderFeatures> SupportedFeaturesList
        {
            get
            {
                if (m_SupportedFeaturesList.Count == 0)
                {
                    GatherShaderFeatures();
                }

                return m_SupportedFeaturesList;
            }
        }
        
        public void OnPreprocessBuild(BuildReport report)
        {
            GatherShaderFeatures();
        }

        /// <summary>
        /// Gathers all the shader features and updates the prefilter settings
        /// </summary>
        internal static void GatherShaderFeatures()
        {
            m_SupportedFeaturesList.Clear();
            
            // Global Settings
            var settings = IllusionRenderPipelineSettings.instance;
            if (!settings.stripUnusedVariants)
            {
                m_SupportedFeaturesList.Add(ShaderFeatures.All);
                return;
            }
            
            // SSS
            bool isScreenSpaceSubsurfaceScatteringEnabled = false;
            bool everyRendererHasScreenSpaceSubsurfaceScattering = true;
            
            // Contact Shadow
            bool isContactShadowEnabled = false;
            bool everyRendererHasContactShadow = true;
            
            // PCSS
            bool isPCSSEnabled = false;
            bool everyRendererHasPCSS = true;
            
            // PRTGI
            bool isPRTGIEnabled = false;
            bool everyRendererHasPRTGI = true;

            // Calculate transparent shadow
            bool isCalculateTransparentShadowEnabled = false;
            bool everyRendererHasCalculateTransparentShadow = true;
            
            // SSR
            bool isSSREnabled = false;
            
            // SSGI
            bool isSSGIEnabled = false;
            bool everyRendererHasSSGI = true;
            
            // Fragment shadow bias
            bool isFragmentShadowBiasEnabled = false;
            bool everyRendererHasFragmentShadowBias = true;

            List<IllusionRendererFeature> features = new();

            using (ListPool<UniversalRenderPipelineAsset>.Get(out var urpAssets))
            {
                bool success = EditorUserBuildSettings.activeBuildTarget.TryGetRenderPipelineAssets(urpAssets);
                if (!success)
                {
                    Debug.LogError("Unable to get UniversalRenderPipelineAssets from EditorUserBuildSettings.activeBuildTarget.");
                    return;
                }
                
                foreach (var asset in urpAssets)
                {
                    foreach (ScriptableRendererData rendererData in asset.m_RendererDataList)
                    {
                        ShaderFeatures shaderFeatures = ShaderFeatures.None;
                        if (!UniversalRenderingUtility.TryGetRendererFeature<IllusionRendererFeature>(rendererData,
                                out var rendererFeature)) continue;

                        features.Add(rendererFeature);
                        // Always included features
                        shaderFeatures |= ShaderFeatures.MainLightShadowsScreen;
                        
                        if (rendererFeature.subsurfaceScattering)
                        {
                            shaderFeatures |= ShaderFeatures.ScreenSpaceSubsurfaceScattering;
                            isScreenSpaceSubsurfaceScatteringEnabled = true;
                        }
                        else
                        {
                            everyRendererHasScreenSpaceSubsurfaceScattering = false;
                        }
                        
                        if (rendererFeature.contactShadows)
                        {
                            isContactShadowEnabled = true;
                        }
                        else
                        {
                            everyRendererHasContactShadow = false;
                        }
                        
                        if (rendererFeature.pcssShadows)
                        {
                            isPCSSEnabled = true;
                        }
                        else
                        {
                            everyRendererHasPCSS = false;
                        }
                        
                        if (rendererFeature.precomputedRadianceTransferGI)
                        {
                            shaderFeatures |= ShaderFeatures.PrecomputedRadianceTransferGI;
                            isPRTGIEnabled = true;
                        }
                        else
                        {
                            everyRendererHasPRTGI = false;
                        }

                        if (rendererFeature.orderIndependentTransparency)
                        {
                            shaderFeatures |= ShaderFeatures.OrderIndependentTransparency;
                        }

                        if (rendererFeature.groundTruthAO)
                        {
                            shaderFeatures |= ShaderFeatures.ScreenSpaceOcclusion;
                        }

                        if (rendererFeature.transparentReceivePerObjectShadows)
                        {
                            shaderFeatures |= ShaderFeatures.TransparentPerObjectShadow;
                            isCalculateTransparentShadowEnabled = true;
                        }
                        else
                        {
                            everyRendererHasCalculateTransparentShadow = false;
                        }
                        
                        if (rendererFeature.screenSpaceReflection)
                        {
                            shaderFeatures |= ShaderFeatures.ScreenSpaceReflection;
                            isSSREnabled = true;
                        }

                        if (rendererFeature.screenSpaceGlobalIllumination)
                        {
                            shaderFeatures |= ShaderFeatures.ScreenSpaceGlobalIllumination;
                            isSSGIEnabled = true;
                        }
                        else
                        {
                            everyRendererHasSSGI = false;
                        }

                        if (rendererFeature.fragmentShadowBias)
                        {
                            shaderFeatures |= ShaderFeatures.FragmentShadowBias;
                            isFragmentShadowBiasEnabled = true;
                        }
                        else
                        {
                            everyRendererHasFragmentShadowBias = false;
                        }
                        
                        m_SupportedFeaturesList.Add(shaderFeatures);
                    }
                }
            }

            var screenSpaceSubsurfaceScatteringPrefilterMode = PrefilterMode.Remove;
            if (isScreenSpaceSubsurfaceScatteringEnabled)
            {
                if (everyRendererHasScreenSpaceSubsurfaceScattering)
                {
                    screenSpaceSubsurfaceScatteringPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    screenSpaceSubsurfaceScatteringPrefilterMode = PrefilterMode.Select;
                }
            }

            var contactShadowPrefilterMode = PrefilterMode.Remove;
            if (isContactShadowEnabled)
            {
                if (everyRendererHasContactShadow)
                {
                    contactShadowPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    contactShadowPrefilterMode = PrefilterMode.Select;
                }
            }
            
            var percentageCloserSoftShadowsPrefilterMode = PrefilterMode.Remove;
            if (isPCSSEnabled)
            {
                if (everyRendererHasPCSS)
                {
                    percentageCloserSoftShadowsPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    percentageCloserSoftShadowsPrefilterMode = PrefilterMode.Select;
                }
            }
            
            var prtgiPrefilterMode = PrefilterMode.Remove;
            if (isPRTGIEnabled)
            {
                if (everyRendererHasPRTGI)
                {
                    prtgiPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    prtgiPrefilterMode = PrefilterMode.Select;
                }
            }

            var calculateTransparentShadowPrefilterMode = PrefilterMode.Remove;
            if (isCalculateTransparentShadowEnabled)
            {
                if (everyRendererHasCalculateTransparentShadow)
                {
                    calculateTransparentShadowPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    calculateTransparentShadowPrefilterMode = PrefilterMode.Select;
                }
            }

            var ssrPrefilterMode = PrefilterMode.Remove;
            if (isSSREnabled)
            {
                ssrPrefilterMode = PrefilterMode.Select;
            }
            
            var ssgiPrefilterMode = PrefilterMode.Remove;
            if (isSSGIEnabled)
            {
                if (everyRendererHasSSGI)
                {
                    ssgiPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    ssgiPrefilterMode = PrefilterMode.Select;
                }
            }
            
            var fragmentShadowBiasPrefilterMode = PrefilterMode.Remove;
            if (isFragmentShadowBiasEnabled)
            {
                if (everyRendererHasFragmentShadowBias)
                {
                    fragmentShadowBiasPrefilterMode = PrefilterMode.SelectOnly;
                }
                else
                {
                    fragmentShadowBiasPrefilterMode = PrefilterMode.Select;
                }
            }

            foreach (var feature in features)
            {
                feature.screenSpaceSubsurfaceScatteringPrefilterMode = screenSpaceSubsurfaceScatteringPrefilterMode;
                feature.contactShadowPrefilterMode = contactShadowPrefilterMode;
                feature.percentageCloserSoftShadowsPrefilterMode = percentageCloserSoftShadowsPrefilterMode;
                feature.precomputedRadianceTransferGIPrefilterMode = prtgiPrefilterMode;
                feature.transparentPerObjectShadowsPrefilterMode = calculateTransparentShadowPrefilterMode;
                feature.screenSpaceReflectionPrefilterMode = ssrPrefilterMode;
                feature.screenSpaceGlobalIlluminationPrefilterMode = ssgiPrefilterMode;
                feature.fragmentShadowBiasPrefilterMode = fragmentShadowBiasPrefilterMode;
            }
        }
    }
}
