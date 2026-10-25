using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Illusion.ShaderTools;
using Illusion.ShaderTools.Syntax.ShaderLab;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Illusion.Rendering.Editor
{
    internal static class PathTracingInlineGenerator
    {
        internal const string OutputFolder = "Assets/IllusionRP.Generated/PathTracingInline";
        internal const string EvaluatorsPath = OutputFolder + "/PathTracingInlineEvaluators.hlsl";
        internal const string AnyHitEvaluatorsPath = OutputFolder + "/PathTracingInlineAnyHitEvaluators.hlsl";
        internal const string RegistryPath = OutputFolder + "/Resources/" + PathTracingInlineRegistry.ResourceName + ".asset";
        internal const string PreludeInclude = "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/Inline/PathTracingInline.hlsl";
        private const string UnitMarker = "#pragma PT_INLINE_UNIT_MARKER";
        private const string StandardCoverageProbe = "PT_INLINE_STANDARD_COVERAGE_PROBE";
        private const string StandardCoverageInclude = "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/Inline/PathTracingInlineStandardCoverage.hlsl";
        private const int EnumerateAllVariantsUpTo = 8;

        private static readonly Regex s_PathTracingTag = new(@"""LightMode""\s*=\s*""PathTracing""", RegexOptions.Compiled);
        private static readonly Regex s_KeywordPragma = new(@"^\s*#pragma\s+(shader_feature|multi_compile)(_local)?(_[a-z]+)?\s+(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex s_LineDirectiveOrBlank = new(@"^[ \t]*(?:#line[^\n]*)?\n", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex s_StandardCoverage = new(StandardCoverageProbe + @"\s+PathTracingStandardCoverage\s*\(([^()]*)\)", RegexOptions.Compiled);
        private static readonly Regex s_Identifier = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);
        private static readonly Regex s_MaterialShader = new(@"m_Shader:\s*\{[^}]*guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);

        private sealed class PassSource
        {
            public Shader Shader;
            public string Path;
            public string Source;
            public List<string[]> KeywordSets = new();
            public HashSet<string> Keywords = new();
        }

        private sealed class GeneratedUnit
        {
            public string Key;
            public string[] Files;
            public PathTracingInlineUnitTransform.Unit Transformed;
            public PassSource Pass;
            public string[] Keywords;
            public PathTracingInlineUnitTransform.Unit Unit;
            public string Namespace;
            public string Error;
        }

        [MenuItem("Tools/IllusionRP/Generate Inline Evaluators")]
        private static void GenerateChanged() => Generate(false);

        [MenuItem("Tools/IllusionRP/Regenerate Inline Evaluators")]
        private static void Regenerate() => Generate(true);

        private static bool? s_RayTracingEnabled;

        internal static bool IsEnabled => PathTracingInlineBackend.IsCompiled && (s_RayTracingEnabled ??= RayTracingEnabled());

        internal static void InvalidateEnabled() => s_RayTracingEnabled = null;

        private static bool RayTracingEnabled() => AssetDatabase.FindAssets("t:ScriptableRendererData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(data => data)
            .SelectMany(data => data.rendererFeatures)
            .OfType<IllusionRendererFeature>()
            .Any(feature => feature.isActive && (feature.pathTracing || feature.rayTracedGlobalIllumination));

        public static void Generate(bool force)
        {
            if (!IsEnabled)
                return;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Progress("Collecting PathTracing passes", 0f);
                List<PassSource> passes = CollectPasses();
                Dictionary<string, HashSet<string>> usedVariants = CollectMaterialVariants();

                string vfs = EnsureVirtualFileSystem();
                var includeArgs = new List<string> { "-HV", "2018", "-I", vfs, "-I", Path.Combine(EditorApplication.applicationContentsPath, "Resources", "CGIncludes") };
                var stageDefines = new[] { "SHADER_API_D3D11=1", "SHADER_API_DESKTOP=1", "UNITY_COMPILER_DXC=1", "SHADER_TARGET=50", "UNITY_VERSION=600030" };
                var preprocessArgs = new List<string>(includeArgs);
                foreach (string define in stageDefines)
                {
                    preprocessArgs.Add("-D");
                    preprocessArgs.Add(define);
                }
                var compileArgs = new List<string>(preprocessArgs) { "-D", "SHADER_STAGE_COMPUTE=1" };

                IllusionLog.Verbose("Inline Ray Tracing: Toolchain " + DxcCompiler.ToolchainVersion);
                GeneratorCache cache = GeneratorCache.Load();
                var units = new List<GeneratedUnit>();
                var pending = new List<GeneratedUnit>();
                foreach (PassSource pass in passes)
                {
                    foreach (string[] keywords in Variants(pass, usedVariants))
                    {
                        var unit = new GeneratedUnit { Pass = pass, Keywords = keywords, Namespace = $"PathTracingInlineUnit{units.Count}" };
                        units.Add(unit);
                        unit.Key = Hash(cache.Version + "\n" + pass.Path + "\n" + string.Join(" ", keywords) + "\n" + pass.Source);
                        if (cache.TryGet(unit.Key, out GeneratorCache.Entry cached))
                        {
                            unit.Unit = cached.ToUnit(unit.Namespace);
                            unit.Error = string.IsNullOrEmpty(cached.Error) ? null : cached.Error;
                        }
                        else
                        {
                            pending.Add(unit);
                        }
                    }
                }
                int reused = units.Count - pending.Count;

                var phase = Stopwatch.StartNew();
                RunParallel("Preparing inline evaluators", pending, (dxc, unit) => Preprocess(dxc, unit, preprocessArgs));
                long transformMs = phase.ElapsedMilliseconds;
                foreach (GeneratedUnit unit in pending)
                {
                    cache.Put(unit.Key, unit.Transformed, unit.Error, unit.Files ?? Array.Empty<string>());
                    if (unit.Transformed != null && cache.TryGet(unit.Key, out GeneratorCache.Entry entry))
                    {
                        unit.Unit = entry.ToUnit(unit.Namespace);
                    }
                }

                var hoisted = new Dictionary<string, string>();
                var hoistedOrder = new List<string>();
                var fingerprints = new Dictionary<string, string>();
                string Fingerprint(string text)
                {
                    if (!fingerprints.TryGetValue(text, out string value))
                        fingerprints.Add(text, value = Normalize(text));
                    return value;
                }
                foreach (GeneratedUnit unit in units.Where(u => u.Unit != null))
                {
                    foreach ((string key, string text) in unit.Unit.Hoisted)
                    {
                        if (!hoisted.TryGetValue(key, out string existing))
                        {
                            hoisted.Add(key, text);
                            hoistedOrder.Add(key);
                        }
                        else if (existing != text && Fingerprint(existing) != Fingerprint(text))
                        {
                            unit.Error = $"conflicting global declaration '{key}'";
                        }
                    }
                }

                Progress("Writing inline evaluators and kernels", 1f);
                if (units.Any(u => u.Error != null))
                {
                    new GeneratorReport
                    {
                        Failures = units.Where(u => u.Error != null).Select(u => new GeneratorReport.Failure
                        {
                            Shader = u.Pass.Path, Keywords = string.Join(" ", u.Keywords), Error = u.Error
                        }).ToList()
                    }.Save();
                    throw new InvalidOperationException(string.Join("\n", units.Where(u => u.Error != null).Select(u => u.Pass.Path + ": " + u.Error)));
                }
                List<GeneratedUnit> accepted = units.Where(u => u.Unit != null && u.Error == null).ToList();
                PathTracingInlineRegistry registry = Write(ComposeEvaluators(accepted), accepted, passes, force);
                string shared = ComposeHoisted(accepted, hoisted, hoistedOrder);
                try
                {
                    using DxcCompiler dxc = DxcCompiler.Create();
                    DxcCompiler.Result prelude = dxc.Preprocess("#include \"" + PreludeInclude + "\"\n", PreludeInclude, compileArgs);
                    HlslDocument declaredBefore = ShaderSource.ParseHlsl((prelude.Succeeded ? prelude.Output : string.Empty) + shared);
                    string PreprocessConsumer(string consumer, string prefix)
                    {
                        DxcCompiler.Result source = dxc.Preprocess(prefix + "#include \"" + PreludeInclude + "\"\n#include \"" + consumer + "\"\n", consumer, compileArgs);
                        if (source.Succeeded)
                            return PathTracingInlineUnitTransform.RemoveDeclared(source.Output, declaredBefore);
                        IllusionLog.Error($"Inline Ray Tracing: Failed to preprocess '{consumer}' for its inline kernel.\n{FirstErrors(source.Errors)}");
                        return null;
                    }
                    PathTracingInlineModules.Write(registry, shared, accepted.Select(u => (u.Unit.Body, u.Unit.AnyHitBody, u.Namespace)).ToArray(), PreprocessConsumer, dxc, compileArgs, cache.Version);
                    var existing = AssetDatabase.LoadAssetAtPath<PathTracingInlineRegistry>(RegistryPath);
                    if (existing)
                    {
                        EditorUtility.CopySerialized(registry, existing);
                        EditorUtility.SetDirty(existing);
                        AssetDatabase.SaveAssetIfDirty(existing);
                    }
                    else AssetDatabase.CreateAsset(registry, RegistryPath);
                    PathTracingInlineRegistry.Reload();
                    RemoveObsoleteOutputs(AssetDatabase.LoadAssetAtPath<PathTracingInlineRegistry>(RegistryPath));
                }
                finally { if (!AssetDatabase.Contains(registry)) UnityEngine.Object.DestroyImmediate(registry); }
                cache.Retain(units.Select(u => u.Key));
                cache.Save();

                new GeneratorReport().Save();
                IllusionLog.Info($"Inline Ray Tracing: Generated {units.Count} path tracing inline evaluators from {passes.Count} shaders ({reused} unchanged, {accepted.Count(u => StandardCoverage(u.Unit) != null)} with standard coverage) in {stopwatch.ElapsedMilliseconds} ms (preprocess/transform {transformMs} ms).");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private const string ProgressTitle = "Path Tracing Inline Evaluators";

        private static void Progress(string info, float progress) => EditorUtility.DisplayProgressBar(ProgressTitle, info, progress);

        private static void RunParallel(string info, List<GeneratedUnit> units, Action<DxcCompiler, GeneratedUnit> work, int concurrency = 2)
        {
            if (units.Count == 0)
                return;
            int done = 0;
            var options = new ParallelOptions { MaxDegreeOfParallelism = concurrency };
            Task task = Task.Run(() => Parallel.ForEach(units, options, DxcCompiler.Create, (unit, _, dxc) =>
            {
                try
                {
                    work(dxc, unit);
                }
                catch (Exception e)
                {
                    unit.Error = e.Message;
                }
                Interlocked.Increment(ref done);
                return dxc;
            }, dxc => dxc.Dispose()));
            while (!task.Wait(100))
                Progress($"{info} ({Volatile.Read(ref done)}/{units.Count})", Volatile.Read(ref done) / (float)units.Count);
            task.GetAwaiter().GetResult();
        }

        private static void Preprocess(DxcCompiler dxc, GeneratedUnit unit, List<string> args)
        {
            var source = new StringBuilder();
            source.Append("#include \"").Append(PreludeInclude).Append("\"\n").Append(UnitMarker).Append('\n');
            foreach (string keyword in unit.Keywords)
                source.Append("#define ").Append(keyword).Append(" 1\n");
            source.Append(unit.Pass.Source).Append('\n').Append(StandardCoverageProbe).Append(" PATH_TRACING_STANDARD_COVERAGE\n");

            DxcCompiler.Result result = dxc.Preprocess(source.ToString(), unit.Pass.Path, args);
            unit.Files = GeneratorCache.Dependencies(result.Output, unit.Pass.Path);
            if (!result.Succeeded || string.IsNullOrEmpty(result.Output))
            {
                unit.Error = result.Errors;
                return;
            }
            int marker = result.Output.IndexOf(UnitMarker, StringComparison.Ordinal);
            if (marker < 0)
            {
                unit.Error = "unit marker missing from preprocessed output";
                return;
            }
            string output = result.Output;
            int probe = output.LastIndexOf(StandardCoverageProbe, StringComparison.Ordinal);
            string standardCoverage = null;
            if (probe > marker)
            {
                Match declaration = s_StandardCoverage.Match(output, probe);
                if (declaration.Success && declaration.Index == probe)
                    standardCoverage = declaration.Groups[1].Value;
                output = output.Substring(0, probe);
            }
            unit.Transformed = PathTracingInlineUnitTransform.Transform(output, GeneratorCache.NamespacePlaceholder, marker + UnitMarker.Length);
            unit.Transformed.StandardCoverage = standardCoverage;
        }

        // Expanded declaration: texture, its scale-offset property or 0, texture coordinate set, color, cutoff, then the alpha test,
        // blend and full coverage keywords. A keyword argument that still reads as its own name is not defined.
        private static uint[] StandardCoverage(PathTracingInlineUnitTransform.Unit unit)
        {
            if (unit.StandardCoverage == null)
                return null;
            string[] args = unit.StandardCoverage.Split(',').Select(a => a.Trim()).ToArray();
            if (args.Length != 8 || !uint.TryParse(args[2], out uint uvSet) || uvSet > 3)
                return null;
            static bool On(string keyword) => keyword != "0" && !s_Identifier.IsMatch(keyword);
            bool test = On(args[5]), blend = On(args[6]), full = blend && On(args[7]);
            uint flags = 0x80000000u | (test ? 1u : 0u) | (blend ? 2u : 0u) | (full ? 4u : 0u) | uvSet << 4;
            if (!test && !blend)
                return new[] { flags, 0u, 0u, 0u };
            int Offset(string name, int components)
            {
                int index = unit.Constants.FindIndex(c => c.Name == name);
                if (index < 0)
                    return -1;
                PathTracingInlineUnitTransform.Constant c = unit.Constants[index];
                return c.Components == components && c.Count <= 1 && ConstantKind(c.Type) == 0 && c.Offset < 0xFFFF ? c.Offset : -1;
            }
            int slot = unit.Textures.IndexOf(args[0]);
            int scaleOffset = args[1] == "0" ? 0xFFFF : Offset(args[1], 4), color = Offset(args[3], 4), cutoff = test ? Offset(args[4], 1) : 0;
            if (slot < 0 || scaleOffset < 0 || color < 0 || cutoff < 0)
                return null;
            return new[] { flags, (uint)slot, (uint)color | (uint)scaleOffset << 16, (uint)cutoff };
        }

        private static string Normalize(string text) =>
            ShaderSource.Fingerprint(text);

        private static string FirstErrors(string errors) =>
            string.Join("\n", (errors ?? string.Empty).Split('\n').Where(l => l.Contains("error")).Take(5));

        private static string ComposeHoisted(List<GeneratedUnit> units, Dictionary<string, string> hoisted, List<string> hoistedOrder)
        {
            var keys = new HashSet<string>();
            foreach (GeneratedUnit unit in units)
                foreach (var entry in unit.Unit.Hoisted)
                    keys.Add(entry.Key);
            var sb = new StringBuilder();
            foreach (string key in hoistedOrder)
            {
                if (!keys.Contains(key))
                    continue;
                sb.Append(s_LineDirectiveOrBlank.Replace(hoisted[key], string.Empty));
            }
            return sb.ToString();
        }

        private static string ComposeEvaluators(List<GeneratedUnit> units, bool anyHitOnly = false)
        {
            GeneratedUnit hitContext = null;
            if (!anyHitOnly)
            {
                var contexts = units.Select(u => (Unit: u, Signature: u.Unit.SharedHitContext)).ToList();
                if (contexts.All(c => c.Signature != null) && contexts.Where(c => c.Signature.Length > 0).Select(c => c.Signature).Distinct().Count() == 1)
                    hitContext = contexts.First(c => c.Signature.Length > 0).Unit;
            }
            var sb = new StringBuilder();
            sb.Append("// Generated by IllusionRP from the PathTracing passes of the project's shaders. Do not edit.\n");
            sb.Append("#ifndef ILLUSION_PATH_TRACING_INLINE_EVALUATORS_INCLUDED\n#define ILLUSION_PATH_TRACING_INLINE_EVALUATORS_INCLUDED\n");
            sb.Append("#include \"").Append(PreludeInclude).Append("\"\n");
            uint[][] standard = units.Select(u => StandardCoverage(u.Unit)).ToArray();
            for (int i = 0; i < units.Count; i++)
                if (!anyHitOnly || standard[i] == null)
                    sb.Append(s_LineDirectiveOrBlank.Replace(anyHitOnly ? units[i].Unit.AnyHitBody : units[i].Unit.Body, string.Empty));

            sb.Append("#define PT_INLINE_EVALUATOR_COUNT ").Append(units.Count).Append("u\n");
            bool anyStandard = standard.Any(s => s != null);
            if (anyStandard)
            {
                sb.Append("#include \"").Append(StandardCoverageInclude).Append("\"\n");
                sb.Append("static const uint4 g_PathTracingInlineStandardCoverage[PT_INLINE_EVALUATOR_COUNT] =\n{\n");
                foreach (uint[] s in standard)
                    sb.Append(s == null ? "    uint4(0u, 0u, 0u, 0u),\n" : $"    uint4(0x{s[0]:X}u, {s[1]}u, 0x{s[2]:X}u, {s[3]}u),\n");
                sb.Append("};\n");
            }
            sb.Append("void PathTracingInlineEvaluateClosestHit(uint evaluator, inout IllusionPathPayload payload, AttributeData attributes)\n{\n");
            if (hitContext != null)
                sb.Append($"    g_PathTracingInlineCachedHit = {hitContext.Namespace}::PathTracingGetHitContext(attributes, payload);\n    g_PathTracingInlineHitContextReady = true;\n");
            sb.Append("    switch (evaluator)\n    {\n");
            for (int i = 0; i < units.Count; i++)
                if (!anyHitOnly || standard[i] == null)
                    sb.Append($"        case {i}u: {units[i].Namespace}::PathTracingInlineClosestHit(payload, attributes); break;\n");
            sb.Append("        default: break;\n    }\n");
            if (hitContext != null)
                sb.Append("    g_PathTracingInlineHitContextReady = false;\n");
            sb.Append("}\n");
            sb.Append("bool PathTracingInlineEvaluateAnyHit(uint evaluator, inout IllusionPathPayload payload, AttributeData attributes)\n{\n");
            if (anyStandard)
                sb.Append("    uint4 standard = g_PathTracingInlineStandardCoverage[min(evaluator, PT_INLINE_EVALUATOR_COUNT - 1u)];\n")
                    .Append("    if (evaluator < PT_INLINE_EVALUATOR_COUNT && standard.x != 0u)\n        return PathTracingInlineStandardCoverage(standard, payload, attributes);\n");
            sb.Append("    switch (evaluator)\n    {\n");
            for (int i = 0; i < units.Count; i++)
                if (units[i].Unit.HasAnyHit && standard[i] == null)
                    sb.Append($"        case {i}u: return {units[i].Namespace}::PathTracingInlineAnyHit(payload, attributes);\n");
            sb.Append("        default: return true;\n    }\n}\n#endif\n");
            return sb.ToString();
        }

        private static PathTracingInlineRegistry Write(string evaluators, List<GeneratedUnit> units, List<PassSource> passes, bool force)
        {
            Directory.CreateDirectory(OutputFolder + "/Resources");
            bool changed = WriteIfChanged(EvaluatorsPath, evaluators);
            if (WriteIfChanged(AnyHitEvaluatorsPath, ComposeEvaluators(units, true)))
                AssetDatabase.ImportAsset(AnyHitEvaluatorsPath);

            string hash = Hash(evaluators);
            var existing = AssetDatabase.LoadAssetAtPath<PathTracingInlineRegistry>(RegistryPath);
            bool created = !existing;
            var registry = existing ? UnityEngine.Object.Instantiate(existing) : ScriptableObject.CreateInstance<PathTracingInlineRegistry>();
            registry.name = PathTracingInlineRegistry.ResourceName;
            if (created || force || registry.Hash != hash || registry.Evaluators.Length != units.Count)
            {
                registry.Hash = hash;
                registry.Evaluators = units.Select(u => new PathTracingInlineRegistry.Evaluator
                {
                    Shader = u.Pass.Shader,
                    Keywords = u.Keywords,
                    HasAnyHit = u.Unit.HasAnyHit,
                    RecordSize = u.Unit.RecordSize,
                    StandardCoverage = StandardCoverage(u.Unit) ?? Array.Empty<uint>(),
                    Constants = u.Unit.Constants.Select(c => new PathTracingInlineRegistry.Constant
                    {
                        Name = c.Name,
                        Components = (byte)c.Components,
                        Count = (byte)c.Count,
                        Kind = ConstantKind(c.Type),
                        Offset = (ushort)c.Offset
                    }).ToArray(),
                    Textures = u.Unit.Textures.Select(t => new PathTracingInlineRegistry.Texture
                    {
                        Name = t,
                        Default = TextureDefault(u.Pass.Shader, t)
                    }).ToArray()
                }).ToArray();
                var buffers = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (GeneratedUnit unit in units)
                    foreach ((string name, string type) in unit.Unit.HoistedResources)
                        buffers[name] = type;
                registry.Buffers = buffers.Keys.ToArray();
                registry.RawBuffers = buffers.Values.Select(t => t.Contains("ByteAddressBuffer")).ToArray();
                registry.PassKeywords = passes.Select(p => new PathTracingInlineRegistry.ShaderKeywords
                {
                    Shader = p.Shader,
                    Keywords = p.Keywords.OrderBy(k => k, StringComparer.Ordinal).ToArray()
                }).ToArray();
                changed = true;
            }
            if (changed)
                AssetDatabase.ImportAsset(EvaluatorsPath);
            return registry;
        }

        private static byte ConstantKind(string type)
        {
            return PathTracingInlineUnitTransform.BaseType(type) switch
            {
                "int" or "min16int" or "min12int" => 1,
                "uint" or "min16uint" => 2,
                "bool" => 3,
                _ => 0
            };
        }

        private static PathTracingInlineRegistry.TextureDefault TextureDefault(Shader shader, string texture)
        {
            int index = shader.FindPropertyIndex(texture);
            if (index < 0)
                return PathTracingInlineRegistry.TextureDefault.White;
            return shader.GetPropertyTextureDefaultName(index) switch
            {
                "black" => PathTracingInlineRegistry.TextureDefault.Black,
                "gray" or "grey" => PathTracingInlineRegistry.TextureDefault.Gray,
                "bump" => PathTracingInlineRegistry.TextureDefault.Bump,
                "red" => PathTracingInlineRegistry.TextureDefault.Red,
                "linearGray" or "linearGrey" => PathTracingInlineRegistry.TextureDefault.LinearGray,
                _ => PathTracingInlineRegistry.TextureDefault.White
            };
        }

        // Outputs that the registry no longer references would still import and compile in the background, and unloading them
        // waits for that compile.
        private static void RemoveObsoleteOutputs(PathTracingInlineRegistry registry)
        {
            if (!registry)
                return;
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RegistryPath, EvaluatorsPath, AnyHitEvaluatorsPath };
            SerializedProperty property = new SerializedObject(registry).GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue)
                    live.Add(AssetDatabase.GetAssetPath(property.objectReferenceValue));
            string[] obsolete = Directory.GetFiles(OutputFolder).Select(path => path.Replace('\\', '/'))
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !live.Contains(path)).ToArray();
            if (obsolete.Length == 0)
                return;
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(obsolete, failed);
            IllusionLog.Verbose($"Inline Ray Tracing: Moved {obsolete.Length - failed.Count} obsolete inline outputs to the trash: {string.Join(", ", obsolete.Except(failed).Select(Path.GetFileName))}");
        }

        internal static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content)
                return false;
            File.WriteAllText(path, content);
            return true;
        }

        private static string Hash(string text)
        {
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16);
        }

        private static IEnumerable<string[]> Variants(PassSource pass, Dictionary<string, HashSet<string>> usedVariants)
        {
            var variants = new HashSet<string> { string.Empty };
            if (usedVariants.TryGetValue(AssetDatabase.AssetPathToGUID(pass.Path), out HashSet<string> used))
                variants.UnionWith(used.Select(v => string.Join(" ", v.Split(' ').Where(pass.Keywords.Contains).OrderBy(k => k, StringComparer.Ordinal))));

            long total = 1;
            foreach (string[] set in pass.KeywordSets)
                total *= set.Length;
            if (total <= EnumerateAllVariantsUpTo)
            {
                IEnumerable<IEnumerable<string>> combos = new[] { Enumerable.Empty<string>() };
                foreach (string[] set in pass.KeywordSets)
                {
                    string[] options = set;
                    combos = combos.SelectMany(c => options.Select(o => o == "_" ? c : c.Append(o)));
                }
                foreach (IEnumerable<string> combo in combos)
                    variants.Add(string.Join(" ", combo.OrderBy(k => k, StringComparer.Ordinal)));
            }
            return variants.OrderBy(v => v, StringComparer.Ordinal).Select(v => v.Length == 0 ? Array.Empty<string>() : v.Split(' '));
        }

        private static List<PassSource> CollectPasses()
        {
            var passes = new List<PassSource>();
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    continue;
                string physical = FileUtil.GetPhysicalPath(path);
                if (!File.Exists(physical))
                    continue;
                string text = File.ReadAllText(physical);
                if (!s_PathTracingTag.IsMatch(text))
                    continue;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (!shader)
                    continue;
                PassSource pass = ExtractPass(text);
                if (pass == null)
                    continue;
                pass.Shader = shader;
                pass.Path = path;
                passes.Add(pass);
            }
            return passes.OrderBy(p => p.Path, StringComparer.Ordinal).ToList();
        }

        private static PassSource ExtractPass(string text)
        {
            var shader = ShaderSource.ParseShaderLab(text);
            var selected = shader.SubShaders.SelectMany(s => s.Passes).OfType<ShaderCodePassNode>()
                .FirstOrDefault(p => p.Commands.OfType<ShaderLabCommandTagsNode>()
                    .Any(t => t.Tags.TryGetValue("LightMode", out string mode) && mode == "PathTracing"));
            if (selected?.ProgramBlock == null) return null;
            var pass = new PassSource { Source = selected.ProgramBlock.Value.FullCode.Replace("#include_with_pragmas", "#include") };
            foreach (Match pragma in s_KeywordPragma.Matches(pass.Source))
            {
                string stage = pragma.Groups[3].Value;
                if (stage.Length > 0 && stage != "_raytracing")
                    continue;
                string[] options = pragma.Groups[4].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim('_').Length == 0 ? "_" : o).ToArray();
                if (options.Length == 0)
                    continue;
                if (pragma.Groups[1].Value == "shader_feature" && options.Length == 1)
                    options = new[] { "_", options[0] };
                pass.KeywordSets.Add(options.Distinct().ToArray());
                foreach (string option in options)
                    if (option != "_")
                        pass.Keywords.Add(option);
            }
            return pass;
        }

        private static Dictionary<string, HashSet<string>> CollectMaterialVariants()
        {
            var variants = new Dictionary<string, HashSet<string>>();
            MaterialScanCache scanCache = MaterialScanCache.Load();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    CollectEmbeddedVariants(path, scanCache, variants);
                    continue;
                }
                string physical = FileUtil.GetPhysicalPath(path);
                if (!File.Exists(physical))
                    continue;
                long stamp = GeneratorCache.Stamp(physical);
                if (scanCache.TryGet(path, stamp, out string cachedShader, out string cachedKeywords))
                {
                    if (cachedShader.Length > 0)
                        Add(variants, cachedShader, cachedKeywords);
                    continue;
                }
                string text = File.ReadAllText(physical);
                Match shader = s_MaterialShader.Match(text);
                if (!shader.Success)
                {
                    scanCache.Put(path, stamp, string.Empty, string.Empty);
                    continue;
                }
                var keywords = new List<string>();
                int valid = text.IndexOf("m_ValidKeywords:", StringComparison.Ordinal);
                if (valid >= 0)
                {
                    foreach (string line in text.Substring(valid + "m_ValidKeywords:".Length).Split('\n').Skip(1))
                    {
                        string trimmed = line.Trim();
                        if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                            break;
                        keywords.Add(trimmed.Substring(2).Trim());
                    }
                }
                else
                {
                    Match legacy = Regex.Match(text, @"m_ShaderKeywords:\s*(.*)");
                    if (legacy.Success)
                        keywords.AddRange(legacy.Groups[1].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                }
                scanCache.Put(path, stamp, shader.Groups[1].Value, string.Join(" ", keywords));
                Add(variants, shader.Groups[1].Value, string.Join(" ", keywords));
            }
            scanCache.Save();
            return variants;
        }

        // Models and SpeedTree assets carry their materials as sub-assets, which also depend on the importer settings.
        private static void CollectEmbeddedVariants(string path, MaterialScanCache scanCache, Dictionary<string, HashSet<string>> variants)
        {
            string physical = FileUtil.GetPhysicalPath(path);
            if (!File.Exists(physical))
                return;
            long stamp = GeneratorCache.Stamp(physical) * 31 + GeneratorCache.Stamp(physical + ".meta");
            if (!scanCache.TryGet(path, stamp, out string shaders, out string keywords))
            {
                Material[] materials = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Where(m => m.shader).ToArray();
                shaders = string.Join("|", materials.Select(m => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m.shader))));
                keywords = string.Join("|", materials.Select(m => string.Join(" ", m.shaderKeywords)));
                scanCache.Put(path, stamp, shaders, keywords);
            }
            if (shaders.Length == 0)
                return;
            string[] shaderGuids = shaders.Split('|');
            string[] keywordSets = keywords.Split('|');
            for (int i = 0; i < shaderGuids.Length; i++)
                if (shaderGuids[i].Length > 0)
                    Add(variants, shaderGuids[i], keywordSets[i]);
        }

        private static void Add(Dictionary<string, HashSet<string>> variants, string shader, string keywords)
        {
            if (!variants.TryGetValue(shader, out HashSet<string> set))
                variants.Add(shader, set = new HashSet<string>());
            set.Add(keywords);
        }

        // DXC resolves Unity's virtual package paths through junctions to each package's resolved folder.
        private static string EnsureVirtualFileSystem()
        {
            string root = Path.GetFullPath(Path.Combine("Library", "IllusionRP", "PathTracingInlineVfs"));
            string packages = Path.Combine(root, "Packages");
            Directory.CreateDirectory(packages);
            string manifestPath = Path.Combine(root, "links.txt");
            var links = new Dictionary<string, string>();
            if (File.Exists(manifestPath))
                foreach (string line in File.ReadAllLines(manifestPath))
                {
                    int separator = line.IndexOf('|');
                    if (separator > 0)
                        links[line.Substring(0, separator)] = line.Substring(separator + 1);
                }

            var wanted = new Dictionary<string, string> { [Path.Combine(root, "Assets")] = Path.GetFullPath("Assets") };
            foreach (UnityEditor.PackageManager.PackageInfo info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                wanted[Path.Combine(packages, info.name)] = Path.GetFullPath(info.resolvedPath);

            foreach ((string link, string target) in wanted)
            {
                if (links.TryGetValue(link, out string existing) && existing == target && Directory.Exists(link))
                    continue;
                if (Directory.Exists(link))
                    Directory.Delete(link, false);
                var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                process?.WaitForExit();
            }
            File.WriteAllLines(manifestPath, wanted.Select(kv => kv.Key + "|" + kv.Value));
            return root;
        }
    }
}
