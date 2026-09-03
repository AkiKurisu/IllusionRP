using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UnityRhi
{
    /// <summary>
    /// Independent mutable keyword state for an immutable <see cref="RhiShader"/>
    /// asset. Instances can select different variants of the same shader safely.
    /// </summary>
    public sealed class RhiShaderVariant
    {
        private readonly RhiShader _shader;
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private RhiShaderKeywordSet _normalized;

        internal RhiShaderVariant(RhiShader shader) =>
            _shader = shader != null
                ? shader
                : throw new ArgumentNullException(nameof(shader));

        public RhiShader Shader => _shader;
        public string Key => Keywords.Key;

        public RhiShaderKeywordSet Keywords
        {
            get
            {
                if (_normalized == null)
                    _normalized = _shader.Normalize(new RhiShaderKeywordSet("Runtime",
                        _values.Select(pair =>
                            new RhiShaderKeywordValue(pair.Key, pair.Value)).ToArray()));
                return _normalized;
            }
        }

        public void EnableKeyword(string name) => SetKeyword(name, true);
        public void DisableKeyword(string name) => SetKeyword(name, false);

        public void SetKeyword(string name, bool value) =>
            SetKeyword(name, value ? "1" : "0");

        public void SetKeyword(string name, int value) =>
            SetKeyword(name, value.ToString(CultureInfo.InvariantCulture));

        public void SetKeyword(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A keyword name is required.", nameof(name));
            RhiShaderKeywordDefinition definition = FindDefinition(name);
            string selected = value ?? "";
            if (!definition.Accepts(selected))
                throw new InvalidOperationException(
                    $"Shader '{_shader.name}' keyword '{name}' does not support " +
                    $"value '{selected}'.");
            _values[name] = selected;
            _normalized = null;
        }

        public byte[] GetBytecode() => _shader.GetBytecode(Keywords);

        private RhiShaderKeywordDefinition FindDefinition(string name)
        {
            RhiShaderKeywordSpace space = _shader.KeywordSpace;
            if (space != null)
                foreach (RhiShaderKeywordDefinition definition in space.Keywords)
                    if (definition != null && definition.name == name)
                        return definition;
            throw new InvalidOperationException(
                $"Shader '{_shader.name}' does not declare keyword '{name}'.");
        }
    }
}
