using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    /// <summary>A concrete variant required from one directly referenced shader.</summary>
    public readonly struct RhiShaderBuildVariant
    {
        public readonly RhiShader Shader;
        public readonly RhiShaderKeywordSet Keywords;

        public RhiShaderBuildVariant(RhiShader shader, RhiShaderKeywordSet keywords)
        {
            Shader = shader;
            Keywords = keywords;
        }
    }

    public interface IRhiShaderBuildVariantProvider
    {
        IEnumerable<RhiShaderBuildVariant> CollectVariants();
    }

    /// <summary>
    /// Collects shader references and variants, compiles DXIL into Library, and
    /// adds it to the Player as opaque StreamingAssets. Keeping bytecode outside
    /// imported RhiShader artifacts avoids mutating AssetDatabase during a build.
    /// </summary>
    public sealed class RhiShaderBuildValidation :
        IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        [Serializable]
        private sealed class ShaderEntry
        {
            public string assetPath;
            public string[] variantKeys = Array.Empty<string>();
        }

        [Serializable]
        private sealed class BuildManifest
        {
            public bool lightweightRestore;
            public ShaderEntry[] shaders = Array.Empty<ShaderEntry>();
        }

        private static string ManifestPath => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../Library/UnityRhi/BuildVariants.json"));
        internal static string StagingPath => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../Library/UnityRhi/PlayerShaders"));

        public int callbackOrder => -1000;
        public static bool IsArtifactSwapActive => File.Exists(ManifestPath);

        [InitializeOnLoadMethod]
        private static void RegisterBuildPreparation()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildPlayerFromWindow);
        }

        private static void BuildPlayerFromWindow(BuildPlayerOptions options)
        {
            PrepareBuildArtifacts();
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }

        internal static RhiShaderKeywordSet[] GetBuildVariants(
            string shaderAssetPath, RhiShaderKeywordSpace keywordSpace)
        {
            return Array.Empty<RhiShaderKeywordSet>();
        }

        public void OnPreprocessBuild(BuildReport report) => PrepareForBuild();
        public void OnPostprocessBuild(BuildReport report)
        {
            // Intentionally keep build-ready artifacts in Library. They are
            // generated cache data, and retaining them makes consecutive builds
            // deterministic without mutating assets from a post-build callback.
        }

        internal static void PrepareForBuild()
        {
            BuildManifest manifest = CollectManifest();
            if (manifest.shaders.Length == 0)
            {
                Debug.LogWarning("[UnityRHI.Shaders] No RHI shader variants were " +
                    "requested for this player build.");
                return;
            }
            ValidateExternalShaders(manifest);
            Debug.Log("[UnityRHI.Shaders] Reusing staged player shader bytecode.");
        }

        /// <summary>
        /// Prepares external shader bytecode before BuildPipeline starts. Asset
        /// imports from an IPreprocessBuild callback can deadlock Unity 6000.3's
        /// native shader variant compiler.
        /// </summary>
        public static void PrepareBuildArtifacts()
        {
            if (BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException(
                    "UnityRHI shader artifacts must be prepared before the player build starts.");

            // Migrate any build that was interrupted while using the old artifact
            // swap implementation back to lightweight imported objects.
            RestoreLightweightImports();
            BuildManifest manifest = CollectManifest();
            if (manifest.shaders.Length == 0)
            {
                Debug.LogWarning("[UnityRHI.Shaders] No RHI shader variants were " +
                    "requested while preparing player artifacts.");
                return;
            }

            int variantCount = manifest.shaders.Sum(shader =>
                shader.variantKeys?.Length ?? 0);
            Debug.Log($"[UnityRHI.Shaders] Preparing {manifest.shaders.Length} " +
                $"shaders ({variantCount} variants) before the player build.");

            Directory.CreateDirectory(StagingPath);
            foreach (ShaderEntry entry in manifest.shaders)
            {
                RhiShader shader =
                    AssetDatabase.LoadAssetAtPath<RhiShader>(entry.assetPath);
                if (shader == null)
                    throw new BuildFailedException(
                        $"UnityRHI shader '{entry.assetPath}' could not be loaded.");
                foreach (string key in entry.variantKeys)
                {
                    RhiShaderKeywordSet keywords = shader.KeywordSpace != null
                        ? shader.KeywordSpace.Normalize(RhiShaderKeywordSet.ParseKey(key))
                        : null;
                    byte[] bytecode = RhiShaderVariantJit.CompileForBuild(shader, keywords);
                    string path = Path.Combine(StagingPath,
                        RhiShader.ExternalBytecodeFileName(shader.RuntimeId, key));
                    if (!File.Exists(path) ||
                        !File.ReadAllBytes(path).SequenceEqual(bytecode))
                        File.WriteAllBytes(path, bytecode);
                }
            }
            ValidateExternalShaders(manifest);
        }

        private static bool ManifestMatches(BuildManifest left, BuildManifest right)
        {
            if (left == null || right == null || left.lightweightRestore ||
                left.shaders == null || right.shaders == null ||
                left.shaders.Length != right.shaders.Length)
                return false;

            for (int index = 0; index < left.shaders.Length; index++)
            {
                ShaderEntry a = left.shaders[index];
                ShaderEntry b = right.shaders[index];
                if (a == null || b == null || !string.Equals(a.assetPath, b.assetPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !(a.variantKeys ?? Array.Empty<string>()).SequenceEqual(
                        b.variantKeys ?? Array.Empty<string>(), StringComparer.Ordinal))
                    return false;
            }
            return true;
        }

        private static BuildManifest CollectManifest()
        {
            var requests = new Dictionary<string,
                (RhiShader shader, List<RhiShaderKeywordSet> seeds)>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (Type providerType in
                TypeCache.GetTypesDerivedFrom<IRhiShaderBuildVariantProvider>())
            {
                if (providerType.IsAbstract || providerType.IsInterface) continue;
                var provider = (IRhiShaderBuildVariantProvider)Activator.CreateInstance(
                    providerType, nonPublic: true);
                foreach (RhiShaderBuildVariant request in provider.CollectVariants() ??
                    Enumerable.Empty<RhiShaderBuildVariant>())
                {
                    if (request.Shader == null) continue;
                    string path = AssetDatabase.GetAssetPath(request.Shader);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!requests.TryGetValue(path, out var shader))
                        shader = (request.Shader,
                            new List<RhiShaderKeywordSet>());
                    if (request.Keywords != null)
                        shader.seeds.Add(request.Keywords);
                    requests[path] = shader;
                }
            }

            return new BuildManifest
            {
                shaders = requests.OrderBy(pair => pair.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(pair =>
                    {
                        RhiShader shader = pair.Value.shader;
                        // The lightweight imported object can temporarily expose a
                        // null KeywordSpace after a clean import or an artifact swap,
                        // even though the importer definition has a keyword asset.
                        // The importer metadata is the source of truth for builds.
                        var importer = AssetImporter.GetAtPath(pair.Key) as
                            RhiShaderImporter;
                        RhiShaderKeywordSpace keywordSpace =
                            importer?.keywordAsset ?? shader.KeywordSpace;
                        RhiShaderKeywordSet[] variants =
                            keywordSpace != null
                                ? keywordSpace.ExpandMultiCompile(
                                    pair.Value.seeds.Concat(
                                        new RhiShaderKeywordSet[] { null }))
                                : new RhiShaderKeywordSet[] { null };
                        return new ShaderEntry
                        {
                            assetPath = pair.Key,
                            variantKeys = variants.Select(item =>
                                item?.Key ?? "").Distinct(
                                    StringComparer.Ordinal).OrderBy(
                                    key => key, StringComparer.Ordinal).ToArray(),
                        };
                    }).ToArray(),
            };
        }

        private static void ValidateCompiledShaders(BuildManifest manifest)
        {
            var missing = new List<string>();
            foreach (ShaderEntry entry in manifest.shaders)
            {
                RhiShader shader =
                    AssetDatabase.LoadAssetAtPath<RhiShader>(entry.assetPath);
                if (shader == null)
                {
                    missing.Add($"  '{entry.assetPath}' is missing its shader.");
                    continue;
                }
                foreach (string key in entry.variantKeys)
                    if (!shader.TryGetBytecode(key, out _))
                        missing.Add($"  '{entry.assetPath}' is missing variant " +
                            $"'{key}'.");
            }
            if (missing.Count != 0)
                throw new BuildFailedException(
                    "UnityRHI failed to compile shaders required by this build:\n" +
                    string.Join("\n", missing.Distinct(StringComparer.Ordinal)));
        }

        private static void ValidateExternalShaders(BuildManifest manifest)
        {
            var missing = new List<string>();
            foreach (ShaderEntry entry in manifest.shaders)
            {
                RhiShader shader =
                    AssetDatabase.LoadAssetAtPath<RhiShader>(entry.assetPath);
                if (shader == null || string.IsNullOrEmpty(shader.RuntimeId))
                {
                    missing.Add($"  '{entry.assetPath}' has no runtime shader id.");
                    continue;
                }
                foreach (string key in entry.variantKeys)
                {
                    string path = Path.Combine(StagingPath,
                        RhiShader.ExternalBytecodeFileName(shader.RuntimeId, key));
                    if (!File.Exists(path) || new FileInfo(path).Length == 0)
                        missing.Add($"  '{entry.assetPath}' is missing staged variant '{key}'.");
                }
            }
            if (missing.Count != 0)
                throw new BuildFailedException(
                    "UnityRHI failed to stage shaders required by this build:\n" +
                    string.Join("\n", missing.Distinct(StringComparer.Ordinal)));
        }

        private static void Reimport(BuildManifest manifest)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (ShaderEntry shader in manifest.shaders)
                    AssetDatabase.ImportAsset(shader.assetPath,
                        ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        internal static void RestoreLightweightImports()
        {
            BuildManifest manifest = ReadManifest();
            if (manifest == null)
            {
                if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
                return;
            }
            manifest.lightweightRestore = true;
            WriteManifest(manifest);
            try
            {
                Reimport(manifest);
            }
            finally
            {
                if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
            }
        }

        private static BuildManifest ReadManifest()
        {
            if (!File.Exists(ManifestPath)) return null;
            try
            {
                return JsonUtility.FromJson<BuildManifest>(
                    File.ReadAllText(ManifestPath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UnityRHI.Shaders] Ignoring invalid build variant " +
                    $"manifest: {exception.Message}");
                return null;
            }
        }

        private static void WriteManifest(BuildManifest manifest)
        {
            string directory = Path.GetDirectoryName(ManifestPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
        }
    }

    internal sealed class RhiShaderStreamingAssetsProcessor : BuildPlayerProcessor
    {
        public override int callbackOrder => -1100;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            RhiShaderBuildValidation.PrepareForBuild();
            if (!Directory.Exists(RhiShaderBuildValidation.StagingPath)) return;
            buildPlayerContext.AddAdditionalPathToStreamingAssets(
                RhiShaderBuildValidation.StagingPath, "UnityRhi");
        }
    }
}
