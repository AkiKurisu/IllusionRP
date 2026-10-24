using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Illusion.Rendering.PathTracing;
using Illusion.ShaderTools;
using Illusion.ShaderTools.Syntax.HLSL;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal static class PathTracingInlineModules
    {
        private const string Library = "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/";
        private const string Shaders = "Packages/com.kurisu.illusion-render-pipelines/Shaders/";
        private const string Version = "material-modules-1";

        private sealed class Job
        {
            public string Name, Source, Key, Path, Closest;
            public bool Native16, Changed;
            public byte[] Code;
            public HlslDocument Document;
            public DxcCompiler.Reflection Reflection;
            public List<string> Arguments;
        }

        public static void Write(PathTracingInlineRegistry registry, string shared, (string Body, string AnyHitBody, string Namespace)[] materials,
            Func<string, string, string> preprocess, DxcCompiler compiler, List<string> arguments, string rulesVersion)
        {
            string toolchain = DxcCompiler.ToolchainVersion;
            string sources = string.Concat(new[] { Library, Shaders }
                .SelectMany(root => Directory.EnumerateFiles(FileUtil.GetPhysicalPath(root), "*.*", SearchOption.AllDirectories))
                .Where(p => p.EndsWith(".hlsl", StringComparison.Ordinal) || p.EndsWith(".hlsli", StringComparison.Ordinal) || p.EndsWith(".h", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
            string key = Hash128.Compute(Version + rulesVersion + toolchain + string.Join(" ", arguments) + shared +
                string.Concat(materials.Select(m => m.Body + m.AnyHitBody)) + sources + File.ReadAllText(PathTracingInlineGenerator.AnyHitEvaluatorsPath)).ToString();
            var cached = new[] { registry.Performance, registry.Quality, registry.CacheUpdate, registry.Reference, registry.Build, registry.Fill };
            if (registry.ModulesHash == key && cached.All(p => p?.Compute && p.RayGenSer && p.Coverage &&
                p.Materials.Length == materials.Length && p.Materials.All(m => m.Code)))
            {
                Debug.Log("[IllusionRP] Reused all inline RayGen, material modules and binding carriers.");
                return;
            }
            var jobs = new List<Job>();
            var programs = new List<(PathTracingInlineProgram Program, Job RayGen, string Path)>();
            var modules = new Dictionary<bool, Job[]>();
            var coverage = new Dictionary<bool, Job>();
            string prelude = "#include \"" + PathTracingInlineGenerator.PreludeInclude + "\"\n" + shared;
            foreach (bool native16 in new[] { false, true })
            {
                modules[native16] = materials.Select(m =>
                {
                    string body = m.Body.Replace(m.Namespace, "PathTracingMaterial");
                    string id = Hash128.Compute(body).ToString();
                    string closest = "PTMaterial_" + id;
                    string source = prelude + body + PathTracingInlineKernels.TableInclude +
                        "#undef InstanceID\n#undef InstanceIndex\n#undef PrimitiveIndex\n#undef GeometryIndex\n" +
                        "#undef ObjectToWorld3x4\n#undef WorldToObject3x4\n#undef WorldRayOrigin\n#undef WorldRayDirection\n#undef RayTCurrent\n" +
                        "void InitializeMaterial() {\n" +
                        "g_PathTracingInline.instanceID = InstanceID(); g_PathTracingInline.instanceIndex = InstanceIndex();\n" +
                        "g_PathTracingInline.primitiveIndex = PrimitiveIndex(); g_PathTracingInline.geometryIndex = GeometryIndex(); g_PathTracingInline.hitKind = HitKind();\n" +
                        "g_PathTracingInline.objectToWorld = ObjectToWorld3x4(); g_PathTracingInline.worldToObject = WorldToObject3x4();\n" +
                        "g_PathTracingInline.rayOrigin = WorldRayOrigin(); g_PathTracingInline.rayDirection = WorldRayDirection(); g_PathTracingInline.t = RayTCurrent();\n" +
                        "uint first = _PathTracingInlineInstanceGeometries[InstanceID()];\n" +
                        "g_PathTracingInlineGeometry = _PathTracingInlineGeometries[first + GeometryIndex()];\n" +
                        "g_PathTracingInlineMaterialBase = g_PathTracingInlineGeometry.materialData; }\n" +
                        $"[shader(\"closesthit\")] void {closest}(inout IllusionPathPayload payload, AttributeData attributes) {{ InitializeMaterial(); PathTracingMaterial::PathTracingInlineClosestHit(payload, attributes); }}\n";
                    var job = Prepare("Material_" + id + (native16 ? "_16" : ""), source, native16);
                    job.Closest = closest;
                    job.Arguments.AddRange(new[] { "-exports", closest });
                    return job;
                }).ToArray();
                coverage[native16] = Prepare("Coverage" + (native16 ? "_16" : ""), prelude +
                    "#include \"" + PathTracingInlineGenerator.AnyHitEvaluatorsPath + "\"\n" + PathTracingInlineKernels.TableInclude +
                    "export bool PathTracingInlineCoverage(uint evaluator, PathTracingInlineHitState state, inout IllusionPathPayload payload, AttributeData attributes) {\n" +
                    "g_PathTracingInline = state;\n" +
                    "uint first = _PathTracingInlineInstanceGeometries[state.instanceID];\n" +
                    "g_PathTracingInlineGeometry = _PathTracingInlineGeometries[first + state.geometryIndex];\n" +
                    "g_PathTracingInlineMaterialBase = g_PathTracingInlineGeometry.materialData;\n" +
                    "return PathTracingInlineEvaluateAnyHit(evaluator, payload, attributes); }\n", native16);
            }
            registry.Performance = Program("InlinePerformance", Library + "RayTracedGlobalIllumination/RayTracedGIInlineInitialSamples.hlsl", "RayTracedGIInitialSamples");
            registry.Quality = Program("InlineQuality", Shaders + "RayTracedGlobalIllumination/RayTracedGIInitialSamplesQuality.hlsl", "RayGen_InitialSamples", true);
            registry.CacheUpdate = Program("InlineCacheUpdate", Shaders + "RayTracedGlobalIllumination/RayTracedGIRadianceCacheUpdate.hlsl", "RayGen_RadianceCacheUpdate", true);
            registry.Reference = Program("InlineReference", Shaders + "PathTracing/PathTracing.hlsl", "RayGen_REF");
            registry.Build = Program("InlineBuild", Shaders + "PathTracing/PathTracingBuild.hlsl", "RayGen_BUILD");
            registry.Fill = Program("InlineFill", Shaders + "PathTracing/PathTracingFill.hlsl", "RayGen_FILL");

            var abi = new Bindings(jobs.Select(j => j.Document));
            foreach (var job in jobs)
            {
                job.Source = abi.Apply(job.Document);
                job.Key = Hash128.Compute(Version + toolchain + string.Join(" ", job.Arguments) + job.Source).ToString();
            }
            var unique = jobs.GroupBy(j => j.Key).Select(g => g.First()).ToArray();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int compiled = 0;
            const string bytecodeCache = "Library/IllusionRP/NativeModules";
            Directory.CreateDirectory(bytecodeCache);
            Parallel.ForEach(unique, new ParallelOptions { MaxDegreeOfParallelism = 2 }, job =>
            {
                using var dxc = DxcCompiler.Create();
                string cachePath = bytecodeCache + "/" + job.Key + ".dxil";
                if (File.Exists(cachePath))
                    job.Code = File.ReadAllBytes(cachePath);
                else
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var result = dxc.Compile(job.Source, "", "lib_6_6", job.Arguments);
                    if (!result.Succeeded) throw new InvalidOperationException($"{job.Name}\n{result.Errors}");
                    job.Code = result.Bytecode;
                    File.WriteAllBytes(cachePath, job.Code);
                    System.Threading.Interlocked.Increment(ref compiled);
                    Debug.Log($"[IllusionRP] Compiled {job.Name} in {watch.ElapsedMilliseconds} ms.");
                }
                job.Changed = !File.Exists(job.Path) || !File.ReadAllBytes(job.Path).SequenceEqual(job.Code);
                job.Reflection = dxc.Reflect(job.Code, true);
            });
            foreach (var job in jobs)
            {
                var result = unique.First(j => j.Key == job.Key);
                job.Code = result.Code;
                job.Changed = job.Path == result.Path ? result.Changed : !File.Exists(job.Path) || !File.ReadAllBytes(job.Path).SequenceEqual(job.Code);
                job.Reflection = result.Reflection;
            }
            var carriers = new Dictionary<string, string>();
            foreach (var entry in programs)
            {
                var all = new[] { entry.RayGen, coverage[entry.RayGen.Native16] }.Concat(modules[entry.RayGen.Native16]).ToArray();
                var reflection = Merge(all.Select(j => j.Reflection));
                var extension = reflection.Resources.FindIndex(r => r.Name == "g_NvidiaExt");
                var binding = reflection.Resources[extension];
                binding.Name = "_PathTracingInlineExtension";
                binding.Type = 6;
                reflection.Resources[extension] = binding;
                string carrierSource = entry.RayGen.Source + "\nRWStructuredBuffer<uint4> _PathTracingInlineExtension : register(u7);\n";
                string carrier = PathTracingInlineBindingCarrier.Generate(carrierSource, reflection);
                var args = new List<string> { "-Zpr", "-HV", "2018" };
                if (entry.RayGen.Native16) { args.Add("-enable-16bit-types"); carrier = "#pragma require Native16Bit\n" + carrier; }
                var check = compiler.Compile(carrier, "InlineMain", "cs_6_6", args);
                if (!check.Succeeded) throw new InvalidOperationException(check.Errors);
                var actual = compiler.Reflect(check.Bytecode, false);
                foreach (var job in all) PathTracingInlineBindingCarrier.Validate(job.Reflection, actual);
                carriers.Add(entry.Path, carrier);
            }
            foreach (var job in jobs)
                if (job.Changed) File.WriteAllBytes(job.Path, job.Code);
            var imported = new Dictionary<string, TextAsset>();
            foreach (var entry in programs)
            {
                string carrier = carriers[entry.Path];
                if (PathTracingInlineGenerator.WriteIfChanged(entry.Path, carrier)) AssetDatabase.ImportAsset(entry.Path);
                entry.Program.Compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(entry.Path);
                entry.Program.RayGenSer = Import(entry.RayGen);
                entry.Program.Coverage = Import(coverage[entry.RayGen.Native16]);
                entry.Program.Materials = modules[entry.RayGen.Native16].Select(m => new PathTracingInlineProgram.MaterialModule
                {
                    Code = Import(m), ClosestHit = m.Closest
                }).ToArray();
            }
            Debug.Log($"[IllusionRP] Material modules: {compiled} compiled, {unique.Length - compiled} reused in {timer.ElapsedMilliseconds} ms.");
            registry.ModulesHash = key;
            EditorUtility.SetDirty(registry);

            TextAsset Import(Job job)
            {
                if (imported.TryGetValue(job.Path, out var asset)) return asset;
                asset = AssetDatabase.LoadAssetAtPath<TextAsset>(job.Path);
                if (job.Changed || !asset)
                {
                    AssetDatabase.ImportAsset(job.Path);
                    asset = AssetDatabase.LoadAssetAtPath<TextAsset>(job.Path);
                }
                imported.Add(job.Path, asset);
                return asset;
            }

            Job Prepare(string name, string source, bool native16)
            {
                var args = new List<string>(arguments) { "-O3", "-Zpr", "-auto-binding-space", "0", "-D", "ILLUSION_INLINE_RAYGEN=1", "-D", "ILLUSION_INLINE_MODULES=1" };
                if (native16) args.Add("-enable-16bit-types");
                var expanded = compiler.Preprocess(source, name, new List<string>(args) { "-T", "lib_6_6" });
                if (!expanded.Succeeded) throw new InvalidOperationException($"{name}\n{expanded.Errors}");
                var job = new Job
                {
                    Name = name, Source = expanded.Output, Document = ShaderSource.ParseHlsl(expanded.Output), Native16 = native16,
                    Arguments = args, Path = PathTracingInlineGenerator.OutputFolder + "/" + name + ".bytes"
                };
                jobs.Add(job);
                return job;
            }

            PathTracingInlineProgram Program(string name, string consumer, string entry, bool native16 = false)
            {
                string body = preprocess(consumer, "#define ILLUSION_INLINE_MODULES 1\n");
                if (body == null) throw new InvalidOperationException($"Could not preprocess {name}.");
                body = string.Join("\n", body.Split('\n').Where(l => !l.TrimStart().StartsWith("#pragma", StringComparison.Ordinal)));
                string source = "#define NV_SHADER_EXTN_SLOT u7\n#define NV_HITOBJECT_USE_MACRO_API 1\n" +
                    "#include \"Packages/com.kurisu.unityrhi/ShaderLibrary/NVAPI/nvHLSLExtns.h\"\n" + prelude +
                    "#include \"" + Library + "PathTracing/Inline/PathTracingInlineTraversal.hlsl\"\n" + body + PathTracingInlineKernels.TableInclude +
                    "[shader(\"raygeneration\")] void InlineMain() { if (_ScreenParams.x < 0) return; uint2 pixel = DispatchRaysIndex().xy; " + entry + "(pixel); }\n";
                var raygen = Prepare(name + ".ser", source, native16);
                raygen.Arguments.AddRange(new[] { "-exports", "InlineMain" });
                var program = new PathTracingInlineProgram();
                programs.Add((program, raygen, PathTracingInlineGenerator.OutputFolder + "/" + name + ".compute"));
                return program;
            }
        }

        private static DxcCompiler.Reflection Merge(IEnumerable<DxcCompiler.Reflection> reflections)
        {
            var result = new DxcCompiler.Reflection();
            foreach (var reflection in reflections)
            {
                foreach (var resource in reflection.Resources)
                {
                    var found = result.Resources.Where(r => r.Name == resource.Name).ToArray();
                    if (found.Length == 0) result.Resources.Add(resource);
                    else if (found[0].Type != resource.Type || found[0].BindPoint != resource.BindPoint || found[0].Space != resource.Space || found[0].BindCount != resource.BindCount)
                        throw new InvalidOperationException($"Conflicting module binding '{resource.Name}'.");
                }
                foreach (var buffer in reflection.ConstantBuffers)
                {
                    var found = result.ConstantBuffers.Find(b => b.Name == buffer.Name);
                    if (found == null) result.ConstantBuffers.Add(buffer);
                    else if (found.Size != buffer.Size || !found.Variables.Select(v => (v.Name, v.Offset, v.Size)).SequenceEqual(buffer.Variables.Select(v => (v.Name, v.Offset, v.Size))))
                        throw new InvalidOperationException($"Conflicting module cbuffer '{buffer.Name}'.");
                }
            }
            return result;
        }

        private sealed class Bindings
        {
            private readonly Dictionary<string, string> declarations = new();
            private readonly Dictionary<string, SortedDictionary<string, string>> globals = new();
            private readonly Dictionary<string, string> slots = new();

            public Bindings(IEnumerable<HlslDocument> documents)
            {
                var counters = new Dictionary<char, int> { ['b'] = 0, ['t'] = 0, ['u'] = 0, ['s'] = 0 };
                foreach (var document in documents)
                    foreach (var declaration in document.Declarations)
                    {
                        if (declaration.Node is ConstantBufferNode buffer)
                        {
                            string key = declaration.Scope + "::" + buffer.Name.GetName();
                            if (!slots.ContainsKey(key)) slots[key] = Slot('b');
                        }
                        else if (declaration.Node is VariableDeclarationStatementNode variable && IsGlobal(variable))
                        {
                            if (Resource(variable, document.Source))
                            {
                                foreach (var field in variable.Declarators)
                                {
                                    string key = declaration.Scope + "::" + field.Name.Identifier;
                                    if (slots.ContainsKey(key)) continue;
                                    var explicitSlot = field.Qualifiers.OfType<RegisterLocationNode>().FirstOrDefault();
                                    string type = ShaderSource.Code(document.Source, variable.Kind);
                                    char kind = type.Contains("Sampler") ? 's' : type.StartsWith("RW", StringComparison.Ordinal) || type.StartsWith("Append", StringComparison.Ordinal) || type.StartsWith("Consume", StringComparison.Ordinal) ? 'u' : 't';
                                    slots[key] = explicitSlot == null ? Slot(kind) : ShaderSource.Code(document.Source, explicitSlot);
                                }
                            }
                            else
                            {
                                if (!globals.TryGetValue(declaration.Scope, out var fields)) globals[declaration.Scope] = fields = new();
                                foreach (var field in variable.Declarators)
                                {
                                    string text = ShaderSource.Code(document.Source, variable.Kind) + " " + ShaderSource.Code(document.Source, field) + ";\n";
                                    if (fields.TryGetValue(field.Name.Identifier, out string previous) && previous != text)
                                        throw new InvalidOperationException($"Conflicting global '{field.Name.Identifier}'.");
                                    fields[field.Name.Identifier] = text;
                                }
                            }
                        }
                    }
                foreach (var pair in globals)
                    declarations[pair.Key] = "cbuffer IllusionInlineGlobals : " + Slot('b') + " {\n" + string.Concat(pair.Value.Values) + "}\n";

                string Slot(char kind)
                {
                    if (kind == 'u' && counters[kind] == 7) counters[kind]++;
                    return $"register({kind}{counters[kind]++}, space0)";
                }
            }

            private static bool Resource(VariableDeclarationStatementNode variable, string source) =>
                variable.Kind is PredefinedObjectTypeNode || ShaderSource.Code(source, variable.Kind) == "RaytracingAccelerationStructure";

            private static bool IsGlobal(VariableDeclarationStatementNode variable) =>
                !variable.Modifiers.Contains(BindingModifier.Static) && !variable.Modifiers.Contains(BindingModifier.Const);

            public string Apply(HlslDocument document)
            {
                var edits = new List<(int, int, string)>();
                var emitted = new HashSet<string>();
                foreach (var declaration in document.Declarations)
                {
                    var node = declaration.Node;
                    if (node is ConstantBufferNode buffer)
                    {
                        string slot = slots[declaration.Scope + "::" + buffer.Name.GetName()];
                        if (buffer.RegisterLocation != null)
                            edits.Add((buffer.RegisterLocation.Span.StartIndex, buffer.RegisterLocation.Span.Length, slot));
                        else edits.Add((buffer.Name.Span.EndIndex, 0, " : " + slot));
                    }
                    else if (node is VariableDeclarationStatementNode variable && IsGlobal(variable))
                    {
                        if (Resource(variable, document.Source))
                        {
                            foreach (var field in variable.Declarators)
                            {
                                string slot = slots[declaration.Scope + "::" + field.Name.Identifier];
                                var previous = field.Qualifiers.OfType<RegisterLocationNode>().FirstOrDefault();
                                if (previous != null) edits.Add((previous.Span.StartIndex, previous.Span.Length, slot));
                                else edits.Add((field.Span.EndIndex, 0, " : " + slot));
                            }
                        }
                        else edits.Add((node.Span.StartIndex, node.Span.Length, emitted.Add(declaration.Scope) ? declarations[declaration.Scope] : ""));
                    }
                }
                return ShaderSource.Edit(document.Source, edits);
            }
        }
    }
}
