using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using Illusion.Rendering.PathTracing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    internal sealed class IllusionShaderVariantPreprocessor : IPreprocessShaders, IPreprocessComputeShaders, IOrderedCallback
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
            new(
                PathTracingPass.MaterialPassName,
                PathTracingPass.MaterialPassName,
                ShaderFeatures.PathTracing),
        };

        public int callbackOrder => 100;

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> compilerDataList)
        {
            if (!shader || compilerDataList == null || compilerDataList.Count == 0)
                return;

            LocalKeyword capture = shader.keywordSpace.FindKeyword("_PRT_CAPTURE");
            if (capture.isValid)
                for (int i = compilerDataList.Count - 1; i >= 0; i--)
                    if (compilerDataList[i].shaderKeywordSet.IsEnabled(capture))
                        compilerDataList.RemoveAt(i);
            if (compilerDataList.Count == 0)
                return;

            IllusionShaderBuildData buildData = ShaderBuildPreprocessor.CurrentData;
            if (buildData == null || !buildData.IsValid || !buildData.StripUnusedVariants)
                return;

            if (IsUnusedPathTracingShader(buildData, shader))
            {
                compilerDataList.Clear();
                return;
            }

            if (!TryGetPassMetadata(shader, snippet, out string renderPipeline, out string lightMode))
                return;

            if (!string.Equals(renderPipeline, "UniversalPipeline", StringComparison.Ordinal))
                return;

            if (buildData.StripUrpVariants)
            {
                if ((lightMode == "UniversalGBuffer" && !buildData.AnyRendererSupports(ShaderFeatures.DeferredRendering))
                    || (lightMode == "Universal2D" && !buildData.AnyRendererSupports(ShaderFeatures.Renderer2D)))
                {
                    compilerDataList.Clear();
                    return;
                }
                UrpShaderVariantRules.Strip(shader, snippet, compilerDataList, buildData.UrpKeywordStates);
            }

            if (!TryGetPassContract(snippet.passName, out PassContract contract)
                || !string.Equals(lightMode, contract.LightMode, StringComparison.Ordinal))
                return;

            if (!IsPassReachable(buildData, contract)
                || (contract.LightMode == IllusionShaderPasses.OIT && IllusionShaderBuildScope.IsOitUnused(shader)))
                compilerDataList.Clear();
        }

        public void OnProcessComputeShader(
            ComputeShader shader,
            string kernelName,
            IList<ShaderCompilerData> compilerDataList)
        {
            if (!shader || compilerDataList == null || compilerDataList.Count == 0)
                return;

            IllusionShaderBuildData buildData = ShaderBuildPreprocessor.CurrentData;
            if (buildData == null || !buildData.IsValid || !buildData.StripUnusedVariants)
                return;

            if (IsUnusedPathTracingShader(buildData, shader))
                compilerDataList.Clear();
        }

        private static bool IsUnusedPathTracingShader(IllusionShaderBuildData buildData, UnityEngine.Object shader)
        {
            return buildData.IsPathTracingShader(shader) && !buildData.AnyRendererSupports(ShaderFeatures.PathTracing);
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
