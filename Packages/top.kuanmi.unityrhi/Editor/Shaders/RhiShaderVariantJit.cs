using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    /// <summary>
    /// Compiles shaders on demand when the editor first requests them.
    ///
    /// Unlike the compiler this replaces, it produces nothing but bytecode: no
    /// asset is created or dirtied, which makes it safe to hit during Play Mode.
    /// Player builds use <see cref="RhiShaderBuildValidation"/> to collect,
    /// expand and temporarily bake the combinations reported by build variant
    /// providers.
    /// </summary>
    internal static class RhiShaderVariantJit
    {
        private const string CacheFormatVersion = "UnityRHI variant JIT v4";
        private static readonly Dictionary<string, byte[]> Memory =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        private static string CacheRoot => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../Library/UnityRhi/VariantJit"));

        [InitializeOnLoadMethod]
        private static void Register() => RhiShader.EditorVariantCompiler = Compile;

        private static byte[] Compile(RhiShader shader, RhiShaderKeywordSet keywords)
        {
            try
            {
                return CompileOrThrow(shader, keywords);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UnityRHI.Shaders] On-demand compile of '{shader?.name}' " +
                    $"variant '{keywords?.Key}' failed: {exception.Message}");
                return null;
            }
        }

        private static byte[] CompileOrThrow(RhiShader shader,
            RhiShaderKeywordSet keywords)
        {
            if (shader == null) return null;

            // Revision already fingerprints the compiler, dependency contents and
            // import settings. Keep this lookup ahead of all importer/source access.
            string key = ComputeKey(shader, keywords);
            if (Memory.TryGetValue(key, out byte[] cached)) return cached;

            string cachePath = Path.Combine(CacheRoot, key + ".dxil");
            if (File.Exists(cachePath))
            {
                cached = File.ReadAllBytes(cachePath);
                Memory[key] = cached;
                return cached;
            }

            string manifestPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(manifestPath) ||
                !manifestPath.EndsWith("." + RhiShaderImporter.Extension,
                    StringComparison.OrdinalIgnoreCase)) return null;
            var importer = AssetImporter.GetAtPath(manifestPath) as RhiShaderImporter;
            return importer != null
                ? CompileProgram(importer, shader.KeywordSpace,
                    keywords, key, cachePath)
                : null;
        }

        internal static byte[] CompileForBuild(RhiShader shader,
            RhiShaderKeywordSet keywords) => CompileOrThrow(shader, keywords);

        private static byte[] CompileProgram(RhiShaderImporter importer,
            RhiShaderKeywordSpace keywordSpace, RhiShaderKeywordSet keywords,
            string key, string cachePath)
        {
            string sourcePath = importer.SourcePath();
            var includes = new List<string> { Path.GetDirectoryName(sourcePath) ?? "" };
            includes.AddRange(importer.AbsoluteIncludeDirectories());

            string[] keywordDefines = keywordSpace != null
                ? keywordSpace.GetDefines(keywords)
                : Array.Empty<string>();
            string[] defines = OverrideDefinesByMacroName(importer.defines
                .Concat(keywordDefines)
                .Concat(importer.programDefines)
                .Where(define => !string.IsNullOrWhiteSpace(define)));

            var scanner = new RhiShaderIncludeScanner();
            ShaderCompiler.Result result = ShaderCompiler.Compile(
                scanner.ReadAllText(sourcePath), Path.GetFileName(sourcePath),
                importer.entryPoint ?? "", importer.targetProfile, defines,
                includes.ToArray(), importer.flags);
            if (!result.Succeeded)
                throw new InvalidOperationException(result.Diagnostics);

            Directory.CreateDirectory(CacheRoot);
            WriteAtomically(cachePath, result.Bytecode);
            Memory[key] = result.Bytecode;
            Debug.Log($"[UnityRHI.Shaders] Compiled '{importer.assetPath}' variant " +
                $"'{keywords?.Key ?? ""}' on demand.");
            return result.Bytecode;
        }

        private static void WriteAtomically(string cachePath, byte[] bytecode)
        {
            string temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytecode);
                try
                {
                    File.Move(temporary, cachePath);
                }
                catch (IOException) when (File.Exists(cachePath))
                {
                    // Another process produced the same content-addressed entry.
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string ComputeKey(RhiShader shader, RhiShaderKeywordSet keywords)
        {
            using var sha = SHA256.Create();
            string assetPath = AssetDatabase.GetAssetPath(shader);
            var data = new StringBuilder(CacheFormatVersion).Append('\n')
                .Append("Revision:").Append(shader.Revision).Append('\n')
                .Append("Asset:").Append(
                    AssetDatabase.AssetPathToGUID(assetPath)).Append('\n')
                .Append("Keywords:").Append(keywords?.Key ?? "").Append('\n');
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())))
                .Replace("-", "").ToLowerInvariant();
        }

        private static string[] OverrideDefinesByMacroName(IEnumerable<string> defines)
        {
            var order = new List<string>();
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string define in defines)
            {
                int separator = define.IndexOf('=');
                string name = (separator < 0 ? define : define.Substring(0, separator)).Trim();
                if (indexByName.TryGetValue(name, out int existing)) order[existing] = define;
                else
                {
                    indexByName[name] = order.Count;
                    order.Add(define);
                }
            }
            return order.ToArray();
        }
    }
}
