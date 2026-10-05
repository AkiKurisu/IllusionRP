using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using ShaderKeywordStrings = UnityEngine.Rendering.Universal.ShaderKeywordStrings;

namespace Illusion.Rendering.Editor
{
    internal struct ShaderStrippingData
    {
        internal ShaderFeatures ShaderFeatures { get; set; }

        internal ShaderSnippetData PassData { get; set; }

        internal ShaderCompilerData VariantData { get; set; }

        internal bool StripUnusedVariants { get; set; }

        internal Shader Shader { get; set; }

        internal bool IsKeywordEnabled(LocalKeyword keyword)
        {
            return VariantData.shaderKeywordSet.IsEnabled(keyword);
        }

        internal bool IsShaderFeatureEnabled(ShaderFeatures feature)
        {
            return (ShaderFeatures & feature) != 0;
        }

        internal bool PassHasKeyword(LocalKeyword keyword)
        {
            return ShaderUtil.PassHasKeyword(
                Shader,
                PassData.pass,
                keyword,
                PassData.shaderType,
                VariantData.shaderCompilerPlatform);
        }
    }

    /// <summary>
    /// Preserves the established IllusionRP keyword stripping rules for every target renderer.
    /// </summary>
    internal sealed class ShaderVariantStripper : IShaderVariantStripper, IShaderVariantStripperScope
    {
        private LocalKeyword _mainLightShadowsScreen;
        private LocalKeyword _surfaceTypeTransparent;
        private LocalKeyword _screenSpaceReflection;
        private LocalKeyword _screenSpaceOcclusion;
        private LocalKeyword _screenSpaceGlobalIllumination;
        private LocalKeyword _precomputedRadianceTransferGI;
        private LocalKeyword _transparentPerObjectShadow;
        private LocalKeyword _fragmentShadowBias;
        private LocalKeyword _areaShadowMedium;
        private LocalKeyword _areaShadowHigh;

        public ShaderVariantStripper()
        {
        }

        public bool active
        {
            get
            {
                IllusionShaderBuildData buildData = ShaderBuildPreprocessor.CurrentData;
                return buildData != null
                    && buildData.IsValid
                    && buildData.StripUnusedVariants;
            }
        }

        public bool CanRemoveVariant(
            Shader shader,
            ShaderSnippetData passData,
            ShaderCompilerData variantData)
        {
            IllusionShaderBuildData buildData = ShaderBuildPreprocessor.CurrentData;
            if (buildData == null || !buildData.IsValid || !buildData.StripUnusedVariants)
                return true;

            ShaderStrippingData strippingData = new()
            {
                Shader = shader,
                PassData = passData,
                VariantData = variantData,
                StripUnusedVariants = buildData.StripUnusedVariants,
            };

            IReadOnlyList<ShaderFeatures> rendererFeatures = buildData.RendererFeatures;
            for (int i = 0; i < rendererFeatures.Count; i++)
            {
                strippingData.ShaderFeatures = rendererFeatures[i];
                if (StripUnusedFeatures(ref strippingData))
                    continue;
                return false;
            }

            return true;
        }

        private bool StripUnusedFeatures(ref ShaderStrippingData strippingData)
        {
            ShaderStripTool<ShaderFeatures> stripTool = new(
                strippingData.ShaderFeatures,
                ref strippingData);

            if (stripTool.StripMultiCompile(_screenSpaceReflection, ShaderFeatures.ScreenSpaceReflection))
                return true;
            if (stripTool.StripMultiCompile(
                    _screenSpaceGlobalIllumination,
                    ShaderFeatures.ScreenSpaceGlobalIllumination))
                return true;
            if (!strippingData.IsShaderFeatureEnabled(ShaderFeatures.UnmanagedScreenSpaceOcclusion)
                && stripTool.StripMultiCompile(_screenSpaceOcclusion, ShaderFeatures.ScreenSpaceOcclusion))
                return true;
            if (StripMainLightShadowsScreen(ref strippingData, ref stripTool))
                return true;
            if (stripTool.StripMultiCompile(
                    _precomputedRadianceTransferGI,
                    ShaderFeatures.PrecomputedRadianceTransferGI))
                return true;
            if (stripTool.StripMultiCompile(
                    _transparentPerObjectShadow,
                    ShaderFeatures.TransparentPerObjectShadow))
                return true;
            if (stripTool.StripMultiCompile(
                    _fragmentShadowBias,
                    ShaderFeatures.FragmentShadowBias))
                return true;
            return StripAreaShadow(ref strippingData);
        }

        // One multi_compile set: a renderer with area lights keeps exactly its tier (the volume / runtime switch
        // only zeroes the light count), a renderer without area lights keeps only the off variant.
        private bool StripAreaShadow(ref ShaderStrippingData strippingData)
        {
            if (!strippingData.StripUnusedVariants)
                return false;

            bool medium = strippingData.IsKeywordEnabled(_areaShadowMedium);
            bool high = strippingData.IsKeywordEnabled(_areaShadowHigh);
            if (medium)
                return !strippingData.IsShaderFeatureEnabled(ShaderFeatures.AreaShadowMedium);
            if (high)
                return !strippingData.IsShaderFeatureEnabled(ShaderFeatures.AreaShadowHigh);

            // Off variant: only passes that declare the axis are eligible.
            bool declaresAxis = strippingData.PassHasKeyword(_areaShadowMedium)
                                || strippingData.PassHasKeyword(_areaShadowHigh);
            return declaresAxis && strippingData.IsShaderFeatureEnabled(ShaderFeatures.AreaLights);
        }

        private bool StripMainLightShadowsScreen(
            ref ShaderStrippingData strippingData,
            ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            if (strippingData.IsShaderFeatureEnabled(ShaderFeatures.MainLightShadowsScreen))
            {
                if (strippingData.IsKeywordEnabled(_surfaceTypeTransparent)
                    && strippingData.IsKeywordEnabled(_mainLightShadowsScreen))
                {
                    return true;
                }

                return stripTool.StripMultiCompileKeepOffVariant(
                    _mainLightShadowsScreen,
                    ShaderFeatures.MainLightShadowsScreen);
            }

            return stripTool.StripMultiCompile(
                _mainLightShadowsScreen,
                ShaderFeatures.MainLightShadowsScreen);
        }

        public void BeforeShaderStripping(Shader shader)
        {
            _surfaceTypeTransparent = shader.keywordSpace.FindKeyword(
                ShaderKeywordStrings._SURFACE_TYPE_TRANSPARENT);
            _mainLightShadowsScreen = shader.keywordSpace.FindKeyword(
                ShaderKeywordStrings.MainLightShadowScreen);
            _screenSpaceOcclusion = shader.keywordSpace.FindKeyword(
                ShaderKeywordStrings.ScreenSpaceOcclusion);
            _screenSpaceReflection = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords._SCREEN_SPACE_REFLECTION);
            _screenSpaceGlobalIllumination = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION);
            _precomputedRadianceTransferGI = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION);
            _transparentPerObjectShadow = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS);
            _fragmentShadowBias = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT);
            _areaShadowMedium = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords.AREA_SHADOW_MEDIUM);
            _areaShadowHigh = shader.keywordSpace.FindKeyword(
                IllusionShaderKeywords.AREA_SHADOW_HIGH);
        }

        public void AfterShaderStripping(Shader shader)
        {
        }
    }

}
