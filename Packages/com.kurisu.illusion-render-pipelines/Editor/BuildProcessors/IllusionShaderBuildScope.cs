using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    public readonly struct ShaderPassUsage
    {
        public ShaderPassUsage(Shader shader, string lightMode, bool used)
        {
            Shader = shader;
            LightMode = lightMode;
            Used = used;
        }

        public Shader Shader { get; }
        public string LightMode { get; }
        public bool Used { get; }
    }

    public static class IllusionShaderBuildScope
    {
        public const int PolicyVersion = 2;

        private static PassUsageScope s_Current;

        public static IDisposable BeginPassUsage(IEnumerable<ShaderPassUsage> usage)
        {
            if (usage == null)
                throw new ArgumentNullException(nameof(usage));

            var oitUsage = new Dictionary<int, bool>();
            foreach (ShaderPassUsage entry in usage)
            {
                if (!entry.Shader || string.IsNullOrEmpty(entry.LightMode))
                    throw new ArgumentException("Pass usage requires a shader and LightMode.", nameof(usage));
                if (!string.Equals(entry.LightMode, IllusionShaderPasses.OIT, StringComparison.Ordinal))
                    continue;
                int id = entry.Shader.GetInstanceID();
                oitUsage.TryGetValue(id, out bool used);
                oitUsage[id] = used || entry.Used;
            }

            var scope = new PassUsageScope(s_Current, oitUsage);
            s_Current = scope;
            return scope;
        }

        internal static bool IsOitUnused(Shader shader)
        {
            return s_Current != null
                && s_Current.Usage.TryGetValue(shader.GetInstanceID(), out bool used)
                && !used;
        }

        private sealed class PassUsageScope : IDisposable
        {
            private readonly PassUsageScope _previous;
            private bool _disposed;

            internal PassUsageScope(PassUsageScope previous, Dictionary<int, bool> usage)
            {
                _previous = previous;
                Usage = usage;
            }

            internal Dictionary<int, bool> Usage { get; }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                s_Current = _previous;
            }
        }
    }
}
