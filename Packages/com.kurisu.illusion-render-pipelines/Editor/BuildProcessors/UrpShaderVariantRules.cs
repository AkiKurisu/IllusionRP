using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    internal readonly struct UrpKeywordState
    {
        internal UrpKeywordState(ShEvalMode shMode, bool reflectionProbeAtlas)
        {
            ShMode = shMode;
            ReflectionProbeAtlas = reflectionProbeAtlas;
        }

        internal ShEvalMode ShMode { get; }

        internal bool ReflectionProbeAtlas { get; }
    }

    internal static class UrpShaderVariantRules
    {
        internal static void Strip(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> variants,
            IReadOnlyList<UrpKeywordState> rendererStates)
        {
            LocalKeyword mixed = shader.keywordSpace.FindKeyword(ShaderKeywordStrings.EVALUATE_SH_MIXED);
            LocalKeyword vertex = shader.keywordSpace.FindKeyword(ShaderKeywordStrings.EVALUATE_SH_VERTEX);
            LocalKeyword atlas = shader.keywordSpace.FindKeyword(ShaderKeywordStrings.ReflectionProbeAtlas);

            for (int i = variants.Count - 1; i >= 0; i--)
            {
                ShaderCompilerData variant = variants[i];
                bool declaresSh = HasKeyword(shader, snippet, variant, mixed)
                    || HasKeyword(shader, snippet, variant, vertex);
                bool declaresAtlas = HasKeyword(shader, snippet, variant, atlas);
                if (!declaresSh && !declaresAtlas)
                    continue;

                ShEvalMode shMode = mixed.isValid && variant.shaderKeywordSet.IsEnabled(mixed)
                    ? ShEvalMode.Mixed
                    : vertex.isValid && variant.shaderKeywordSet.IsEnabled(vertex) ? ShEvalMode.PerVertex : ShEvalMode.PerPixel;

                if (CanRemove(shMode, declaresAtlas && variant.shaderKeywordSet.IsEnabled(atlas), declaresSh, declaresAtlas, rendererStates))
                    variants.RemoveAt(i);
            }
        }

        internal static bool CanRemove(
            ShEvalMode shMode,
            bool atlas,
            bool declaresSh,
            bool declaresAtlas,
            IReadOnlyList<UrpKeywordState> rendererStates)
        {
            if (!declaresSh && !declaresAtlas)
                return false;

            foreach (UrpKeywordState renderer in rendererStates)
            {
                if ((!declaresSh || renderer.ShMode == shMode)
                    && (!declaresAtlas || renderer.ReflectionProbeAtlas == atlas))
                    return false;
            }

            return true;
        }

        private static bool HasKeyword(
            Shader shader,
            ShaderSnippetData snippet,
            ShaderCompilerData variant,
            LocalKeyword keyword)
        {
            return keyword.isValid && ShaderUtil.PassHasKeyword(
                shader, snippet.pass, keyword, snippet.shaderType, variant.shaderCompilerPlatform);
        }
    }
}
