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
    /// <summary>
    /// Protects shared URP keyword variants required by IllusionRP renderer features.
    /// </summary>
    internal sealed class ShaderVariantStripper : IShaderVariantStripper, IShaderVariantStripperScope
    {
        private LocalKeyword _mainLightShadowsScreen;
        private LocalKeyword _screenSpaceOcclusion;

        public ShaderVariantStripper()
        {
        }

        public bool active => true;

        public bool CanRemoveVariant(
            Shader shader,
            ShaderSnippetData passData,
            ShaderCompilerData variantData)
        {
            IllusionShaderBuildData buildData = ShaderBuildPreprocessor.CurrentData;
            if (buildData == null || !buildData.IsValid)
                return false;

            // URP does not discover IllusionRP renderer features, so it would otherwise strip their shared ON variants.
            if (buildData.AnyRendererSupports(ShaderFeatures.ScreenSpaceOcclusion)
                && _screenSpaceOcclusion.isValid
                && variantData.shaderKeywordSet.IsEnabled(_screenSpaceOcclusion))
            {
                return false;
            }

            if (buildData.AnyRendererSupports(ShaderFeatures.MainLightShadowsScreen)
                && _mainLightShadowsScreen.isValid
                && variantData.shaderKeywordSet.IsEnabled(_mainLightShadowsScreen))
            {
                return false;
            }

            return true;
        }

        public void BeforeShaderStripping(Shader shader)
        {
            _mainLightShadowsScreen = shader.keywordSpace.FindKeyword(
                ShaderKeywordStrings.MainLightShadowScreen);
            _screenSpaceOcclusion = shader.keywordSpace.FindKeyword(
                ShaderKeywordStrings.ScreenSpaceOcclusion);
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
