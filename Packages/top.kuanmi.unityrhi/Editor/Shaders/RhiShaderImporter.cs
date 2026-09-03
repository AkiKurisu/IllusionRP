using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    /// <summary>
    /// Compiles a .rhishader marker whose complete definition is serialized in
    /// this importer's .meta settings.
    /// </summary>
        [ScriptedImporter(10, Extension)]
    internal sealed class RhiShaderImporter : ScriptedImporter
    {
        internal const string Extension = "rhishader";
        private const int MaxParallelCompiles = 4;
        private static int s_loggedProcessIdentity;

        [SerializeField] internal DefaultAsset[] includeDirectoryAssets =
            Array.Empty<DefaultAsset>();
        [SerializeField] internal string[] defines = Array.Empty<string>();
        [SerializeField] internal RhiShaderKeywordSpace keywordAsset;
        [SerializeField] internal UnityEngine.Object sourceAsset;
        [SerializeField] internal string entryPoint = "";
        [SerializeField] internal string targetProfile = "cs_6_6";
        [SerializeField] internal ShaderCompileFlags flags;
        [SerializeField] internal string[] programDefines = Array.Empty<string>();

        private sealed class Job
        {
            internal string Variant;
            internal string DisplayName;
            internal string Source;
            internal string SourceName;
            internal string EntryPoint;
            internal string TargetProfile;
            internal string[] Defines;
            internal string[] IncludeDirectories;
            internal ShaderCompileFlags Flags;
            internal ShaderCompiler.Result Result;
            internal Exception Error;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            LogProcessIdentityOnce();
            ctx.DependsOnCustomDependency(RhiShaderCompilerDependency.Name);
            try
            {
                Import(ctx);
            }
            catch (Exception exception)
            {
                ctx.LogImportError($"{ctx.assetPath}: {exception.Message}");
                EmitShader(ctx, null, "");
            }
        }

        private void Import(AssetImportContext ctx)
        {
            ValidateDefinition(ctx.assetPath);

            var scanner = new RhiShaderIncludeScanner();
            string[] absoluteIncludeDirectories = AbsoluteIncludeDirectories();
            foreach (string directory in absoluteIncludeDirectories)
                if (!Directory.Exists(directory))
                    ctx.LogImportWarning(
                        $"{ctx.assetPath}: include directory '{directory}' does not exist.");

            RhiShaderKeywordSpace keywordSpace = null;
            string keywordAssetPath =
                keywordAsset != null ? AssetDatabase.GetAssetPath(keywordAsset) : null;
            if (!string.IsNullOrEmpty(keywordAssetPath))
            {
                ctx.DependsOnArtifact(keywordAssetPath);
                var keywordImporter =
                    AssetImporter.GetAtPath(keywordAssetPath) as RhiShaderKeywordImporter;
                if (keywordImporter == null)
                    throw new InvalidOperationException(
                        $"'{keywordAssetPath}' is not imported as RHI shader keywords.");
                keywordSpace = keywordAsset;
            }

            var jobs = new List<Job>();
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string sourcePath = SourcePath();
            if (!File.Exists(sourcePath))
                throw new InvalidOperationException(
                    $"shader source '{sourceAsset}' was not found.");

            var programIncludes = new List<string>
                { Path.GetDirectoryName(sourcePath) ?? "" };
            programIncludes.AddRange(absoluteIncludeDirectories);
            scanner.CollectDependencies(sourcePath, programIncludes, dependencies);
            string source = scanner.ReadAllText(sourcePath);

            RhiShaderKeywordSet[] buildVariants =
                RhiShaderBuildValidation.GetBuildVariants(
                    ctx.assetPath, keywordSpace);
            foreach (RhiShaderKeywordSet variant in buildVariants)
            {
                string[] jobDefines = OverrideDefinesByMacroName(
                    (defines ?? Array.Empty<string>())
                    .Concat(variant != null
                        ? keywordSpace.GetDefines(variant)
                        : Array.Empty<string>())
                    .Concat(programDefines ?? Array.Empty<string>())
                    .Where(define => !string.IsNullOrWhiteSpace(define)));

                jobs.Add(new Job
                {
                    Variant = variant?.Key ?? "",
                    DisplayName = Path.GetFileNameWithoutExtension(ctx.assetPath) +
                        (variant != null ? " [" + variant.Key + "]" : ""),
                    Source = source,
                    SourceName = Path.GetFileName(sourcePath),
                    EntryPoint = entryPoint ?? "",
                    TargetProfile = targetProfile,
                    Defines = jobDefines,
                    IncludeDirectories = programIncludes.ToArray(),
                    Flags = flags,
                });
            }

            foreach (string dependency in dependencies) DependOn(ctx, dependency);
            string revision = ComputeRevision(
                dependencies, absoluteIncludeDirectories, keywordSpace);
            Run(jobs);

            foreach (Job job in jobs)
            {
                if (job.Error != null)
                {
                    ctx.LogImportError(
                        $"{ctx.assetPath}: {job.DisplayName} threw during compilation: {job.Error}");
                    continue;
                }
                if (job.Result == null || !job.Result.Succeeded)
                {
                    ctx.LogImportError($"{ctx.assetPath}: {job.DisplayName} failed to compile.\n" +
                        job.Result?.Diagnostics);
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(job.Result.Diagnostics))
                    ctx.LogImportWarning(
                        $"{ctx.assetPath}: {job.DisplayName}\n" + job.Result.Diagnostics);

            }
            var variants = jobs
                .Where(job => job.Error == null &&
                    job.Result?.Succeeded == true)
                .ToDictionary(job => job.Variant ?? "",
                    job => job.Result.Bytecode, StringComparer.Ordinal);
            EmitShader(ctx, keywordSpace, revision, variants);
        }

        internal string[] AbsoluteIncludeDirectories() =>
            (includeDirectoryAssets ?? Array.Empty<DefaultAsset>()).Select(directory =>
                {
                    string path = AssetDatabase.GetAssetPath(directory);
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidOperationException(
                            $"{assetPath}: an include directory reference is missing.");
                    return RhiShaderPaths.ToAbsolutePath(path);
                }).ToArray();

        internal string SourcePath()
        {
            string path = AssetDatabase.GetAssetPath(sourceAsset);
            if (!string.IsNullOrEmpty(path)) return RhiShaderPaths.ToAbsolutePath(path);
            throw new InvalidOperationException(
                $"{assetPath}: shader has no source.");
        }

        private void ValidateDefinition(string sourcePath)
        {
            if (sourceAsset == null)
                throw new InvalidOperationException($"{sourcePath}: shader has no source.");
            if (string.IsNullOrWhiteSpace(targetProfile))
                throw new InvalidOperationException(
                    $"{sourcePath}: shader has no target profile.");
        }

        private static void DependOn(AssetImportContext ctx, string absolutePath)
        {
            string dependency = RhiShaderPaths.ToAssetPath(absolutePath);
            if (dependency != null) ctx.DependsOnSourceAsset(dependency);
        }

        private void EmitShader(AssetImportContext ctx,
            RhiShaderKeywordSpace keywordSpace, string revision,
            IReadOnlyDictionary<string, byte[]> variants = null)
        {
            RhiShader shader = RhiShader.Create(
                Path.GetFileNameWithoutExtension(ctx.assetPath),
                AssetDatabase.AssetPathToGUID(ctx.assetPath),
                entryPoint, targetProfile, keywordSpace, revision, variants);
            ctx.AddObjectToAsset("shader", shader);
            ctx.SetMainObject(shader);
        }

        private static void Run(IReadOnlyList<Job> jobs)
        {
            if (jobs.Count == 0) return;
            int workerCount = Math.Min(jobs.Count,
                Math.Min(MaxParallelCompiles, Math.Max(1, Environment.ProcessorCount / 2)));
            int next = -1;
            Task[] workers = Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
            {
                while (true)
                {
                    int index = Interlocked.Increment(ref next);
                    if (index >= jobs.Count) return;
                    Job job = jobs[index];
                    try
                    {
                        job.Result = ShaderCompiler.Compile(job.Source, job.SourceName,
                            job.EntryPoint, job.TargetProfile, job.Defines,
                            job.IncludeDirectories, job.Flags);
                    }
                    catch (Exception exception)
                    {
                        job.Error = exception;
                    }
                }
            })).ToArray();
            Task.WaitAll(workers);
        }

        private string ComputeRevision(IEnumerable<string> dependencies,
            IEnumerable<string> includeDirectories,
            RhiShaderKeywordSpace keywordSpace)
        {
            using var sha = SHA256.Create();
            var data = new StringBuilder("UnityRHI single shader v1\n")
                .Append("Compiler:")
                .Append(RhiShaderCompilerDependency.ComputeStamp())
                .Append('\n');

            foreach (string path in dependencies.OrderBy(
                item => item, StringComparer.OrdinalIgnoreCase))
                data.Append("File:").Append(path).Append('=')
                    .Append(Convert.ToBase64String(
                        sha.ComputeHash(File.ReadAllBytes(path))))
                    .Append('\n');
            foreach (string directory in includeDirectories)
                data.Append("Include:").Append(directory).Append('\n');
            foreach (string define in defines ?? Array.Empty<string>())
                data.Append("Define:").Append(define).Append('\n');
            data.Append("Shader:").Append(entryPoint).Append('|')
                .Append(targetProfile).Append('|')
                .Append((int)flags).Append('\n');
            foreach (string define in programDefines ?? Array.Empty<string>())
                data.Append("ProgramDefine:").Append(define).Append('\n');
            if (keywordSpace != null)
                data.Append("Keywords:")
                    .Append(JsonUtility.ToJson(keywordSpace))
                    .Append('\n');

            return BitConverter.ToString(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())))
                .Replace("-", "").ToLowerInvariant();
        }

        internal static string[] OverrideDefinesByMacroName(IEnumerable<string> sourceDefines)
        {
            var order = new List<string>();
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string define in sourceDefines)
            {
                string name = MacroName(define);
                if (indexByName.TryGetValue(name, out int existing)) order[existing] = define;
                else
                {
                    indexByName[name] = order.Count;
                    order.Add(define);
                }
            }
            return order.ToArray();
        }

        private static string MacroName(string define)
        {
            int separator = define.IndexOf('=');
            return (separator < 0 ? define : define.Substring(0, separator)).Trim();
        }

        private static void LogProcessIdentityOnce()
        {
            if (Interlocked.Exchange(ref s_loggedProcessIdentity, 1) != 0) return;
            Debug.Log("[UnityRHI.Shaders] Importing in " +
                (AssetDatabase.IsAssetImportWorkerProcess() ? "asset import worker" : "main editor") +
                $" process {System.Diagnostics.Process.GetCurrentProcess().Id}.");
        }
    }
}
