using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering
{
    public sealed class PathTracingInlineRegistry : ScriptableObject
    {
        internal const string ResourceName = "PathTracingInlineRegistry";

        public enum TextureDefault : byte
        {
            White,
            Black,
            Gray,
            Bump,
            Red,
            LinearGray
        }

        [Serializable]
        public struct Constant
        {
            public string Name;
            public byte Components;
            public byte Count;
            public byte Kind;
            public ushort Offset;
        }

        [Serializable]
        public struct Texture
        {
            public string Name;
            public TextureDefault Default;
        }

        [Serializable]
        public sealed class Evaluator
        {
            public Shader Shader;
            public string[] Keywords = Array.Empty<string>();
            public Constant[] Constants = Array.Empty<Constant>();
            public Texture[] Textures = Array.Empty<Texture>();
            public int RecordSize;
            public bool HasAnyHit;
        }

        [Serializable]
        public sealed class ShaderKeywords
        {
            public Shader Shader;
            public string[] Keywords = Array.Empty<string>();
        }

        public Evaluator[] Evaluators = Array.Empty<Evaluator>();

        public ShaderKeywords[] PassKeywords = Array.Empty<ShaderKeywords>();

        public string[] Buffers = Array.Empty<string>();

        public bool[] RawBuffers = Array.Empty<bool>();

        public PathTracing.PathTracingInlineProgram Performance;
        public PathTracing.PathTracingInlineProgram Quality;
        public PathTracing.PathTracingInlineProgram CacheUpdate;
        public PathTracing.PathTracingInlineProgram Reference;
        public PathTracing.PathTracingInlineProgram Build;
        public PathTracing.PathTracingInlineProgram Fill;

        public ComputeShader RayTracedGIResampling;

        public string Hash;
        [HideInInspector] public string ModulesHash;

        [NonSerialized] private Dictionary<(int, string), int> _lookup;
        [NonSerialized] private Dictionary<int, HashSet<string>> _passKeywords;

        public const int NoEvaluator = -1;

        public int FindEvaluator(Material material)
        {
            if (!material || !material.shader)
                return NoEvaluator;
            EnsureLookup();
            int shaderId = material.shader.GetInstanceID();
            if (!_passKeywords.TryGetValue(shaderId, out HashSet<string> passKeywords))
                passKeywords = null;
            var enabled = new List<string>();
            if (passKeywords != null)
            {
                foreach (string keyword in material.shaderKeywords)
                    if (passKeywords.Contains(keyword))
                        enabled.Add(keyword);
                enabled.Sort(StringComparer.Ordinal);
            }
            return _lookup.TryGetValue((shaderId, string.Join(" ", enabled)), out int index) ? index : NoEvaluator;
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
                return;
            _lookup = new Dictionary<(int, string), int>();
            _passKeywords = new Dictionary<int, HashSet<string>>();
            for (int i = 0; i < Evaluators.Length; i++)
            {
                Evaluator evaluator = Evaluators[i];
                if (evaluator.Shader)
                    _lookup[(evaluator.Shader.GetInstanceID(), string.Join(" ", evaluator.Keywords))] = i;
            }
            foreach (ShaderKeywords entry in PassKeywords)
                if (entry.Shader)
                    _passKeywords[entry.Shader.GetInstanceID()] = new HashSet<string>(entry.Keywords);
        }

        private void OnValidate()
        {
            _lookup = null;
            _passKeywords = null;
        }

        private static PathTracingInlineRegistry s_Instance;

        internal static PathTracingInlineRegistry Instance
        {
            get
            {
                if (!s_Instance)
                    s_Instance = Resources.Load<PathTracingInlineRegistry>(ResourceName);
                return s_Instance;
            }
        }

        internal static void Reload()
        {
            s_Instance = null;
        }
    }
}
