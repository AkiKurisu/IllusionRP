using System;
using System.Runtime.CompilerServices;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    /// <summary>
    /// Determines whether multi-compile keyword variants are unused by a renderer feature set.
    /// </summary>
    internal struct ShaderStripTool<T> where T : Enum
    {
        private readonly T _features;
        private readonly ShaderStrippingData _strippingData;

        internal ShaderStripTool(T features, ref ShaderStrippingData strippingData)
        {
            _features = features;
            _strippingData = strippingData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool StripMultiCompileKeepOffVariant(in LocalKeyword keyword, T feature)
        {
            return !_features.HasFlag(feature) && _strippingData.IsKeywordEnabled(keyword);
        }

        internal bool StripMultiCompile(in LocalKeyword keyword, T feature)
        {
            if (!_features.HasFlag(feature))
                return _strippingData.IsKeywordEnabled(keyword);

            return _strippingData.StripUnusedVariants
                && !_strippingData.IsKeywordEnabled(keyword)
                && ContainsKeyword(keyword);
        }

        private bool ContainsKeyword(in LocalKeyword keyword)
        {
            return _strippingData.PassHasKeyword(keyword);
        }
    }
}
