using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    internal struct ShaderStrippingData
    {
        public ShaderFeatures ShaderFeatures { get; set; }
        
        public ShaderSnippetData PassData { get; set; }
            
        public ShaderCompilerData VariantData { get; set; }
            
        public bool StripUnusedVariants { get; set; }

        public Shader Shader { get; set; }
            
        public ShaderType ShaderType => PassData.shaderType;

        public ShaderCompilerPlatform ShaderCompilerPlatform => VariantData.shaderCompilerPlatform;

        public string PassName => PassData.passName;

        public PassType PassType => PassData.passType;

        public PassIdentifier PassIdentifier => PassData.pass;
        
        public bool IsKeywordEnabled(LocalKeyword keyword)
        {
            return VariantData.shaderKeywordSet.IsEnabled(keyword);
        }

        public bool IsShaderFeatureEnabled(ShaderFeatures feature)
        {
            return (ShaderFeatures & feature) != 0;
        }

        public bool PassHasKeyword(LocalKeyword keyword)
        {
            return ShaderUtil.PassHasKeyword(Shader, PassData.pass, keyword, PassData.shaderType, ShaderCompilerPlatform);
        }
    }
    
    internal class ShaderVariantStripper : IShaderVariantStripper, IShaderVariantStripperScope
    {
        private LocalKeyword _mainLightShadowsScreen;

        private LocalKeyword _surfaceTypeTransparent;

        private LocalKeyword _screenSpaceReflection;
        
        private LocalKeyword _screenSpaceOcclusion;
        
        private LocalKeyword _screenSpaceGlobalIllumination;

        private LocalKeyword _precomputedRadianceTransferGI;

        private LocalKeyword _transparentPerObjectShadow;
        
        private LocalKeyword _fragmentShadowBias;
        
        public bool active
        {
            get
            {
                var asset = UniversalRenderPipeline.asset;
                if (!asset)
                {
                    return false;
                }

                if (asset.m_RendererDataList == null)
                {
                    return false;
                }

                foreach (ScriptableRendererData rendererData in asset.m_RendererDataList)
                {
                    if (UniversalRenderingUtility.TryGetRendererFeature<IllusionRendererFeature>(rendererData, out var rendererFeature)
                        && rendererFeature.isActive)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool CanRemoveVariant(Shader shader, ShaderSnippetData passData, ShaderCompilerData variantData)
        {
            var strippingData = new ShaderStrippingData
            {
                StripUnusedVariants = IllusionRenderPipelineSettings.instance,
                Shader = shader,
                PassData = passData,
                VariantData = variantData
            };

            // All feature sets need to have this variant unused to be stripped out.
            bool removeInput = strippingData.StripUnusedVariants;
            var features = ShaderBuildPreprocessor.SupportedFeaturesList;
            for (int i = 0; i < features.Count; i++)
            {
                strippingData.ShaderFeatures = features[i];
                
                if (StripUnusedPasses(ref strippingData))
                    continue;
                
                if (StripUnusedFeatures(ref strippingData))
                    continue;
                
                removeInput = false;
                break;
            }

            return removeInput;
        }

        private static bool StripUnusedPasses(ref ShaderStrippingData strippingData)
        {
            if (StripUnusedFeatures_SubsurfaceDiffuse(ref strippingData))
            {
                return true;
            }
            
            if (StripUnusedFeatures_OIT(ref strippingData))
            {
                return true;
            }

            return false;
        }

        private static bool StripUnusedFeatures_SubsurfaceDiffuse(ref ShaderStrippingData strippingData)
        {
            if (!strippingData.IsShaderFeatureEnabled(ShaderFeatures.ScreenSpaceSubsurfaceScattering))
            {
                if (strippingData.PassName == IllusionShaderPasses.SubsurfaceDiffuse)
                {
                    return true;
                }
            }

            return false;
        }
        
        private static bool StripUnusedFeatures_OIT(ref ShaderStrippingData strippingData)
        {
            if (!strippingData.IsShaderFeatureEnabled(ShaderFeatures.OrderIndependentTransparency))
            {
                if (strippingData.PassName == IllusionShaderPasses.OIT)
                {
                    return true;
                }
            }

            return false;
        }

        private bool StripUnusedFeatures(ref ShaderStrippingData strippingData)
        {
            ShaderStripTool<ShaderFeatures> stripTool = new ShaderStripTool<ShaderFeatures>(strippingData.ShaderFeatures, ref strippingData);
            
            if (StripUnusedFeatures_ScreenSpaceReflection(ref strippingData, ref stripTool))
            {
                return true;
            }
            
            if (StripUnusedFeatures_ScreenSpaceGlobalIllumination(ref strippingData, ref stripTool))
            {
                return true;
            }
            
            if (StripUnusedFeatures_ScreenSpaceOcclusion(ref strippingData, ref stripTool))
            {
                return true;
            }
            
            if (StripUnusedFeatures_MainLightShadowsScreen(ref strippingData, ref stripTool))
            {
                return true;
            }
                        
            if (StripUnusedFeatures_PRGGlobalIllumination(ref strippingData, ref stripTool))
            {
                return true;
            }
            
            if (StripUnusedFeatures_TransparentPerObjectShadow(ref strippingData, ref stripTool))
            {
                return true;
            }
            
            if (StripUnusedFeatures_FragmentShadowBias(ref strippingData, ref stripTool))
            {
                return true;
            }

            return false;
        }
        
        private bool StripUnusedFeatures_ScreenSpaceReflection(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            if (strippingData.IsShaderFeatureEnabled(ShaderFeatures.ScreenSpaceReflection))
            {
                // Transparent strip ssr
                if (strippingData.IsKeywordEnabled(_surfaceTypeTransparent) && strippingData.IsKeywordEnabled(_screenSpaceReflection))
                    return true;
                
                if (stripTool.StripMultiCompileKeepOffVariant(_screenSpaceReflection, ShaderFeatures.ScreenSpaceReflection))
                    return true;
            }
            else
            {
                if (stripTool.StripMultiCompile(_screenSpaceReflection, ShaderFeatures.ScreenSpaceReflection))
                    return true;
            }
            return false;
        }
        
        private bool StripUnusedFeatures_ScreenSpaceGlobalIllumination(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            // Can strip off keyword, since we use global variables to control effect
            return stripTool.StripMultiCompile(_screenSpaceGlobalIllumination, ShaderFeatures.ScreenSpaceGlobalIllumination);
        }
                
        private bool StripUnusedFeatures_ScreenSpaceOcclusion(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            if (strippingData.IsShaderFeatureEnabled(ShaderFeatures.ScreenSpaceOcclusion))
            {
                // Transparent strip ssao
                if (strippingData.IsKeywordEnabled(_surfaceTypeTransparent) && strippingData.IsKeywordEnabled(_screenSpaceOcclusion))
                    return true;
                
                if (stripTool.StripMultiCompileKeepOffVariant(_screenSpaceOcclusion, ShaderFeatures.ScreenSpaceOcclusion))
                    return true;
            }
            else
            {
                if (stripTool.StripMultiCompile(_screenSpaceOcclusion, ShaderFeatures.ScreenSpaceOcclusion))
                    return true;
            }
            
            return false;
        }
        
        private bool StripUnusedFeatures_MainLightShadowsScreen(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            if (strippingData.IsShaderFeatureEnabled(ShaderFeatures.MainLightShadowsScreen))
            {
                // Transparent strip sample ss shadow
                if (strippingData.IsKeywordEnabled(_surfaceTypeTransparent) && strippingData.IsKeywordEnabled(_mainLightShadowsScreen))
                    return true;
                
                if (stripTool.StripMultiCompileKeepOffVariant(_mainLightShadowsScreen, ShaderFeatures.MainLightShadowsScreen))
                    return true;
            }
            else
            {
                if (stripTool.StripMultiCompile(_mainLightShadowsScreen, ShaderFeatures.MainLightShadowsScreen))
                    return true;
            }
            return false;
        }
        
        private bool StripUnusedFeatures_PRGGlobalIllumination(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            // Can strip off keyword, since we use global variables to control effect
            return stripTool.StripMultiCompile(_precomputedRadianceTransferGI, ShaderFeatures.PrecomputedRadianceTransferGI);
        }
                
        private bool StripUnusedFeatures_TransparentPerObjectShadow(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            return stripTool.StripMultiCompile(_transparentPerObjectShadow, ShaderFeatures.TransparentPerObjectShadow);
        }
                        
        private bool StripUnusedFeatures_FragmentShadowBias(ref ShaderStrippingData strippingData, ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            return stripTool.StripMultiCompile(_fragmentShadowBias, ShaderFeatures.FragmentShadowBias);
        }
        
        private static LocalKeyword TryGetLocalKeyword(Shader shader, string name)
        {
            return shader.keywordSpace.FindKeyword(name);
        }

        public void BeforeShaderStripping(Shader shader)
        {
            _surfaceTypeTransparent = TryGetLocalKeyword(shader, ShaderKeywordStrings._SURFACE_TYPE_TRANSPARENT);
            _screenSpaceOcclusion = TryGetLocalKeyword(shader, ShaderKeywordStrings.ScreenSpaceOcclusion);
            _screenSpaceReflection = TryGetLocalKeyword(shader, IllusionShaderKeywords._SCREEN_SPACE_REFLECTION);
            _screenSpaceGlobalIllumination = TryGetLocalKeyword(shader, IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION);
            _mainLightShadowsScreen = TryGetLocalKeyword(shader, ShaderKeywordStrings.MainLightShadowScreen);
            _precomputedRadianceTransferGI = TryGetLocalKeyword(shader, IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION);
            _transparentPerObjectShadow = TryGetLocalKeyword(shader, IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS);
            _fragmentShadowBias = TryGetLocalKeyword(shader, IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT);
        }

        public void AfterShaderStripping(Shader shader) { }
    }
}