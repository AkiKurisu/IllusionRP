using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    /// <summary>
    /// Makes the toolchain itself an import dependency: rebuilding the native
    /// plugin or swapping the bundled DXC invalidates every compiled program,
    /// the same way editing a source file does. This replaces the compiler
    /// fingerprint that used to be folded into a hand-rolled cache key.
    /// </summary>
    internal static class RhiShaderCompilerDependency
    {
        internal const string Name = "UnityRHI/ShaderCompiler";
        private const string SessionKey = "UnityRHI.ShaderCompilerDependency";
        private const string PluginRoot = "Packages/top.kuanmi.unityrhi.native/Plugins/x86_64";
        private static readonly string[] Binaries =
            { "UnityRHI.dll", "dxcompiler.dll", "dxil.dll" };

        [InitializeOnLoadMethod]
        private static void Register()
        {
            // Import workers must not write to the database; the main editor
            // process owns this registration and the workers just read it.
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;

            string stamp = ComputeStamp();
            if (SessionState.GetString(SessionKey, "") == stamp) return;
            SessionState.SetString(SessionKey, stamp);
            AssetDatabase.RegisterCustomDependency(Name, Hash128.Compute(stamp));
        }

        /// <summary>
        /// Size and write time rather than a content hash: the binaries run to
        /// tens of megabytes and this is evaluated on every domain reload. A
        /// rebuild always moves the timestamp, and the worst case for a false
        /// positive is a recompile that lands back in the artifact cache.
        /// </summary>
        internal static string ComputeStamp()
        {
            var stamp = new System.Text.StringBuilder(Application.unityVersion);
            string root = RhiShaderPaths.ToAbsolutePath(PluginRoot);
            foreach (string binary in Binaries)
            {
                var file = new FileInfo(Path.Combine(root, binary));
                stamp.Append('|').Append(binary).Append(':');
                stamp.Append(file.Exists
                    ? file.Length + "@" + file.LastWriteTimeUtc.Ticks.ToString()
                    : "missing");
            }
            return stamp.ToString();
        }
    }
}
