using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityRhi
{
    public enum RhiShaderKeywordMode
    {
        ShaderFeature,
        MultiCompile,
    }

    [Serializable]
    public sealed class RhiShaderKeywordOption
    {
        public string value = "0";
        public string[] defines = Array.Empty<string>();
    }

    [Serializable]
    public sealed class RhiShaderKeywordDefinition
    {
        public string name = "";
        public RhiShaderKeywordMode mode = RhiShaderKeywordMode.ShaderFeature;
        public string defaultValue = "0";
        public bool allowCustomValue;
        [Tooltip("Used for custom values; {0} is replaced with the selected value.")]
        public string customDefineTemplate = "{0}";
        public RhiShaderKeywordOption[] options = Array.Empty<RhiShaderKeywordOption>();

        public string[] GetDefines(string value)
        {
            RhiShaderKeywordOption option = (options ?? Array.Empty<RhiShaderKeywordOption>())
                .FirstOrDefault(item => item != null && item.value == value);
            if (option != null) return option.defines ?? Array.Empty<string>();
            if (!allowCustomValue)
                throw new InvalidOperationException(
                    $"Keyword '{name}' does not support value '{value}'.");
            return new[] { (customDefineTemplate ?? "{0}").Replace("{0}", value ?? "") };
        }

        public bool Accepts(string value) => allowCustomValue ||
            (options ?? Array.Empty<RhiShaderKeywordOption>())
                .Any(option => option != null && option.value == value);
    }

    [Serializable]
    public struct RhiShaderKeywordValue
    {
        public string name;
        public string value;

        public RhiShaderKeywordValue(string name, string value)
        {
            this.name = name ?? "";
            this.value = value ?? "";
        }
    }

    /// <summary>A concrete keyword selection. Its canonical key, not its display name, identifies a variant.</summary>
    [Serializable]
    public sealed class RhiShaderKeywordSet
    {
        public string name = "";
        public RhiShaderKeywordValue[] keywords = Array.Empty<RhiShaderKeywordValue>();

        public IReadOnlyList<RhiShaderKeywordValue> Keywords =>
            keywords ?? Array.Empty<RhiShaderKeywordValue>();
        public string Key => MakeKey(Keywords);

        public RhiShaderKeywordSet() { }

        public RhiShaderKeywordSet(string name, params RhiShaderKeywordValue[] keywords)
        {
            this.name = name ?? "";
            this.keywords = keywords ?? Array.Empty<RhiShaderKeywordValue>();
        }

        public bool TryGetValue(string keyword, out string value)
        {
            for (int i = 0; i < Keywords.Count; ++i)
                if (Keywords[i].name == keyword)
                {
                    value = Keywords[i].value ?? "";
                    return true;
                }
            value = null;
            return false;
        }

        public static string MakeKey(IEnumerable<RhiShaderKeywordValue> keywords) => string.Join(";",
            (keywords ?? Array.Empty<RhiShaderKeywordValue>())
                .OrderBy(item => item.name, StringComparer.Ordinal)
                .Select(item => $"{item.name}={item.value}"));

        public static RhiShaderKeywordSet ParseKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return new RhiShaderKeywordSet();
            return new RhiShaderKeywordSet("", key.Split(';')
                .Where(part => !string.IsNullOrEmpty(part))
                .Select(part =>
                {
                    int separator = part.IndexOf('=');
                    return separator < 0
                        ? new RhiShaderKeywordValue(part, "")
                        : new RhiShaderKeywordValue(part.Substring(0, separator),
                            part.Substring(separator + 1));
                }).ToArray());
        }
    }

    public sealed class RhiShaderKeywordSpace : ScriptableObject
    {
        [SerializeField] private RhiShaderKeywordDefinition[] _keywords =
            Array.Empty<RhiShaderKeywordDefinition>();

        public IReadOnlyList<RhiShaderKeywordDefinition> Keywords =>
            _keywords ?? Array.Empty<RhiShaderKeywordDefinition>();

        public RhiShaderKeywordSet Normalize(RhiShaderKeywordSet selection)
        {
            var values = new List<RhiShaderKeywordValue>(Keywords.Count);
            foreach (RhiShaderKeywordDefinition definition in Keywords)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.name)) continue;
                string value = selection != null && selection.TryGetValue(definition.name, out string selected)
                    ? selected
                    : definition.defaultValue;
                if (!definition.Accepts(value))
                    throw new InvalidOperationException(
                        $"Keyword '{definition.name}' does not support value '{value}'.");
                values.Add(new RhiShaderKeywordValue(definition.name, value));
            }
            return new RhiShaderKeywordSet(selection?.name ?? "", values.ToArray());
        }

        public string[] GetDefines(RhiShaderKeywordSet selection)
        {
            RhiShaderKeywordSet normalized = Normalize(selection);
            var defines = new List<string>();
            foreach (RhiShaderKeywordDefinition definition in Keywords)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.name)) continue;
                normalized.TryGetValue(definition.name, out string value);
                defines.AddRange(definition.GetDefines(value));
            }
            return defines.Where(define => !string.IsNullOrWhiteSpace(define)).ToArray();
        }

        public RhiShaderKeywordSet[] ExpandMultiCompile(IEnumerable<RhiShaderKeywordSet> seeds)
        {
            var result = (seeds ?? Array.Empty<RhiShaderKeywordSet>())
                .Select(Normalize).ToList();
            if (result.Count == 0) result.Add(Normalize(null));
            foreach (RhiShaderKeywordDefinition definition in Keywords)
            {
                if (definition == null || definition.mode != RhiShaderKeywordMode.MultiCompile) continue;
                if (definition.allowCustomValue)
                    throw new InvalidOperationException(
                        $"multi_compile keyword '{definition.name}' cannot allow arbitrary values.");
                var expanded = new List<RhiShaderKeywordSet>();
                foreach (RhiShaderKeywordSet set in result)
                    foreach (RhiShaderKeywordOption option in definition.options)
                    {
                        if (option == null) continue;
                        var values = set.Keywords.Where(item => item.name != definition.name).ToList();
                        values.Add(new RhiShaderKeywordValue(definition.name, option.value));
                        expanded.Add(Normalize(new RhiShaderKeywordSet(set.name, values.ToArray())));
                    }
                result = expanded;
            }
            return result.GroupBy(set => set.Key, StringComparer.Ordinal)
                .Select(group => group.First()).OrderBy(set => set.Key, StringComparer.Ordinal).ToArray();
        }

#if UNITY_EDITOR
        public void Initialize(RhiShaderKeywordDefinition[] keywords) =>
            _keywords = keywords ?? Array.Empty<RhiShaderKeywordDefinition>();
#endif
    }
}
