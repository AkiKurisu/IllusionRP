using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
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

            if (StripScreenSpaceReflection(ref strippingData, ref stripTool))
                return true;
            if (stripTool.StripMultiCompile(
                    _screenSpaceGlobalIllumination,
                    ShaderFeatures.ScreenSpaceGlobalIllumination))
                return true;
            if (StripScreenSpaceOcclusion(ref strippingData, ref stripTool))
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
            return stripTool.StripMultiCompile(
                _fragmentShadowBias,
                ShaderFeatures.FragmentShadowBias);
        }

        private bool StripScreenSpaceReflection(
            ref ShaderStrippingData strippingData,
            ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            if (strippingData.IsShaderFeatureEnabled(ShaderFeatures.ScreenSpaceReflection))
            {
                if (strippingData.IsKeywordEnabled(_surfaceTypeTransparent)
                    && strippingData.IsKeywordEnabled(_screenSpaceReflection))
                {
                    return true;
                }

                return stripTool.StripMultiCompileKeepOffVariant(
                    _screenSpaceReflection,
                    ShaderFeatures.ScreenSpaceReflection);
            }

            return stripTool.StripMultiCompile(
                _screenSpaceReflection,
                ShaderFeatures.ScreenSpaceReflection);
        }

        private bool StripScreenSpaceOcclusion(
            ref ShaderStrippingData strippingData,
            ref ShaderStripTool<ShaderFeatures> stripTool)
        {
            if (strippingData.IsShaderFeatureEnabled(ShaderFeatures.ScreenSpaceOcclusion))
            {
                if (strippingData.IsKeywordEnabled(_surfaceTypeTransparent)
                    && strippingData.IsKeywordEnabled(_screenSpaceOcclusion))
                {
                    return true;
                }

                return stripTool.StripMultiCompileKeepOffVariant(
                    _screenSpaceOcclusion,
                    ShaderFeatures.ScreenSpaceOcclusion);
            }

            return stripTool.StripMultiCompile(
                _screenSpaceOcclusion,
                ShaderFeatures.ScreenSpaceOcclusion);
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
        }

        public void AfterShaderStripping(Shader shader)
        {
        }
    }

    /// <summary>
    /// Removes IllusionRP-only passes after SRP Core and URP have processed regular shader variants.
    /// </summary>
    internal sealed class IllusionShaderVariantPreprocessor : IPreprocessShaders, IOrderedCallback
    {
        private readonly struct PassContract
        {
            internal PassContract(
                string passName,
                string lightMode,
                ShaderFeatures required,
                bool requireAll = false)
            {
                PassName = passName;
                LightMode = lightMode;
                Required = required;
                RequireAll = requireAll;
            }

            internal string PassName { get; }

            internal string LightMode { get; }

            internal ShaderFeatures Required { get; }

            internal bool RequireAll { get; }
        }

        private static readonly ShaderTagId RenderPipelineTag = new("RenderPipeline");
        private static readonly ShaderTagId LightModeTag = new("LightMode");

        private static readonly PassContract[] PassContracts =
        {
            new(
                IllusionShaderPasses.OITPassName,
                IllusionShaderPasses.OIT,
                ShaderFeatures.OrderIndependentTransparency),
            new(
                IllusionShaderPasses.SubsurfaceDiffuse,
                IllusionShaderPasses.SubsurfaceDiffuse,
                ShaderFeatures.ScreenSpaceSubsurfaceScattering),
            new(
                IllusionShaderPasses.WaterSSRData,
                IllusionShaderPasses.WaterSSRData,
                ShaderFeatures.ScreenSpaceReflection
                    | ShaderFeatures.TransparentScreenSpaceReflection,
                requireAll: true),
            new(
                IllusionShaderPasses.PostDepthOnly,
                IllusionShaderPasses.PostDepthOnly,
                ShaderFeatures.TransparentDepthPostPass
                    | ShaderFeatures.TransparentOverdraw),
        };

        public int callbackOrder => 100;

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> compilerDataList)
        {
            if (!shader || compilerDataList == null || compilerDataList.Count == 0)
                return;

            IllusionShaderBuildData buildData = ShaderBuildPreprocessor.CurrentData;
            if (buildData == null || !buildData.IsValid || !buildData.StripUnusedVariants)
                return;

            if (!TryGetPassContract(snippet.passName, out PassContract contract))
                return;

            if (!TryGetPassMetadata(shader, snippet, out string renderPipeline, out string lightMode))
                return;

            if (!string.Equals(renderPipeline, "UniversalPipeline", StringComparison.Ordinal)
                || !string.Equals(lightMode, contract.LightMode, StringComparison.Ordinal))
                return;

            if (IsPassReachable(buildData, contract))
                return;

            compilerDataList.Clear();
        }

        private static bool TryGetPassContract(string passName, out PassContract contract)
        {
            for (int i = 0; i < PassContracts.Length; i++)
            {
                if (!string.Equals(PassContracts[i].PassName, passName, StringComparison.Ordinal))
                    continue;
                contract = PassContracts[i];
                return true;
            }

            contract = default;
            return false;
        }

        private static bool IsPassReachable(
            IllusionShaderBuildData buildData,
            PassContract contract)
        {
            if (contract.PassName == IllusionShaderPasses.PostDepthOnly)
            {
                return buildData.AnyRendererSupports(ShaderFeatures.TransparentDepthPostPass)
                    || buildData.AnyRendererSupports(
                        ShaderFeatures.OrderIndependentTransparency
                            | ShaderFeatures.TransparentOverdraw,
                        requireAll: true);
            }

            return buildData.AnyRendererSupports(contract.Required, contract.RequireAll);
        }

        private static bool TryGetPassMetadata(
            Shader shader,
            ShaderSnippetData snippet,
            out string renderPipeline,
            out string lightMode)
        {
            renderPipeline = null;
            lightMode = null;

            try
            {
                ShaderData shaderData = ShaderUtil.GetShaderData(shader);
                if (shaderData == null)
                    return false;

                int subshaderIndex = (int)snippet.pass.SubshaderIndex;
                if (subshaderIndex < 0 || subshaderIndex >= shader.subshaderCount)
                    return false;

                ShaderData.Subshader subshader = shaderData.GetSerializedSubshader(subshaderIndex);
                if (subshader == null)
                    return false;

                ShaderTagId pipelineTag = subshader.FindTagValue(RenderPipelineTag);
                if (string.IsNullOrEmpty(pipelineTag.name))
                    return false;

                int passIndex = (int)snippet.pass.PassIndex;
                if (passIndex < 0 || passIndex >= subshader.PassCount)
                    return false;

                ShaderData.Pass pass = subshader.GetPass(passIndex);
                if (pass == null)
                    return false;

                ShaderTagId passLightMode = pass.FindTagValue(LightModeTag);
                if (string.IsNullOrEmpty(passLightMode.name))
                    return false;

                renderPipeline = pipelineTag.name;
                lightMode = passLightMode.name;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
