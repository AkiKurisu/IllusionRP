using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UnityRhi
{
    [Serializable]
    internal sealed class RhiShaderBytecodeVariant
    {
        [SerializeField] internal string key = "";
        [SerializeField, HideInInspector] internal byte[] bytecode = Array.Empty<byte>();
    }

    /// <summary>
    /// One directly referenceable shader program. The asset is immutable at
    /// runtime; create an <see cref="RhiShaderVariant"/> for independent keyword
    /// state.
    /// </summary>
    public sealed class RhiShader : ScriptableObject
    {
        [SerializeField] private string _entryPoint = "";
        [SerializeField] private string _targetProfile = "";
        [SerializeField] private RhiShaderKeywordSpace _keywordSpace;
        [SerializeField] private string _revision = "";
        [SerializeField, HideInInspector] private string _runtimeId = "";
        [SerializeField, HideInInspector] private RhiShaderBytecodeVariant[] _variants =
            Array.Empty<RhiShaderBytecodeVariant>();
        [NonSerialized] private Dictionary<string, byte[]> _externalVariants;

        public string EntryPoint => _entryPoint ?? "";
        public string TargetProfile => _targetProfile ?? "";
        public RhiShaderKeywordSpace KeywordSpace => _keywordSpace;
        public string Revision => _revision ?? "";
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public string RuntimeId => _runtimeId ?? "";

        public RhiShaderVariant CreateVariant() => new RhiShaderVariant(this);

        public byte[] GetBytecode() => GetBytecode(null);

        public byte[] GetBytecode(RhiShaderKeywordSet keywords)
        {
            RhiShaderKeywordSet normalized = Normalize(keywords);
            string key = _keywordSpace != null ? normalized.Key : "";
            if (TryGetBytecode(key, out byte[] bytecode)) return bytecode;
#if UNITY_EDITOR
            bytecode = EditorVariantCompiler?.Invoke(this, normalized);
            if (bytecode != null) return bytecode;
#endif
            throw new InvalidOperationException(
                $"Shader '{name}' keyword variant '{key}' was not compiled. " +
                "Ensure the shader and variant are declared for the player build.");
        }

        public bool HasVariant(RhiShaderKeywordSet keywords) =>
            TryGetBytecode(_keywordSpace != null ? Normalize(keywords).Key : "", out _);

        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public bool TryGetBytecode(string key, out byte[] bytecode)
        {
            string requested = key ?? "";
            foreach (RhiShaderBytecodeVariant variant in
                _variants ?? Array.Empty<RhiShaderBytecodeVariant>())
            {
                if (variant != null && variant.key == requested)
                {
                    bytecode = variant.bytecode ?? Array.Empty<byte>();
                    return bytecode.Length != 0;
                }
            }
#if !UNITY_EDITOR
            if (!string.IsNullOrEmpty(_runtimeId))
            {
                _externalVariants ??= new Dictionary<string, byte[]>(StringComparer.Ordinal);
                if (_externalVariants.TryGetValue(requested, out bytecode))
                    return bytecode != null && bytecode.Length != 0;
                string path = Path.Combine(Application.streamingAssetsPath, "UnityRhi",
                    ExternalBytecodeFileName(_runtimeId, requested));
                if (File.Exists(path))
                {
                    bytecode = File.ReadAllBytes(path);
                    _externalVariants[requested] = bytecode;
                    return bytecode.Length != 0;
                }
            }
#endif
            bytecode = null;
            return false;
        }

        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static string ExternalBytecodeFileName(string runtimeId, string variantKey)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(variantKey ?? ""));
            string suffix = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return (runtimeId ?? "") + "_" + suffix + ".dxil";
        }

        internal RhiShaderKeywordSet Normalize(RhiShaderKeywordSet keywords) =>
            _keywordSpace != null
                ? _keywordSpace.Normalize(keywords)
                : new RhiShaderKeywordSet();

#if UNITY_EDITOR
        /// <summary>Editor JIT hook. It never mutates this asset.</summary>
        public static Func<RhiShader, RhiShaderKeywordSet, byte[]> EditorVariantCompiler;

        public static RhiShader Create(string assetName, string runtimeId, string entryPoint,
            string targetProfile, RhiShaderKeywordSpace keywordSpace, string revision,
            IReadOnlyDictionary<string, byte[]> variants)
        {
            var shader = CreateInstance<RhiShader>();
            shader.name = assetName ?? "";
            shader._runtimeId = runtimeId ?? "";
            shader._entryPoint = entryPoint ?? "";
            shader._targetProfile = targetProfile ?? "";
            shader._keywordSpace = keywordSpace;
            shader._revision = revision ?? "";
            var emitted = new List<RhiShaderBytecodeVariant>();
            if (variants != null)
                foreach (KeyValuePair<string, byte[]> pair in variants)
                    emitted.Add(new RhiShaderBytecodeVariant
                    {
                        key = pair.Key ?? "",
                        bytecode = pair.Value ?? Array.Empty<byte>(),
                    });
            emitted.Sort((left, right) =>
                string.CompareOrdinal(left.key, right.key));
            shader._variants = emitted.ToArray();
            return shader;
        }
#endif
    }
}
