using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Illusion.ShaderTools;
using Illusion.ShaderTools.Syntax.HLSL;

namespace Illusion.Rendering.Editor
{
    internal static class PathTracingInlineUnitTransform
    {
        internal const string MaterialConstantsBuffer = "UnityPerMaterial";
        internal const string ObjectConstantsBuffer = "UnityPerDraw";

        internal struct Constant
        {
            public string Name;
            public string Type;
            public int Components;
            public int Count;
            public int Offset;
        }

        internal sealed class Unit
        {
            public string Body;
            public string AnyHitBody;
            public string SharedHitContext;
            public string StandardCoverage;
            public readonly List<Constant> Constants = new();
            public readonly List<string> Textures = new();
            public readonly List<KeyValuePair<string, string>> Hoisted = new();
            public readonly Dictionary<string, string> HoistedResources = new();
            public bool HasClosestHit;
            public bool HasAnyHit;
            public bool HasObjectConstants;
            public int ConstantsOffset;
            public int RecordSize;
        }

        private static readonly Dictionary<string, string> s_MarkerTypes = new()
        {
            ["PathTracingInlineTexture2DArray"] = "PathTracingInlineTexture2DArray",
            ["PathTracingInlineTextureCube"] = "PathTracingInlineTextureCube",
            ["PathTracingInlineTextureCubeArray"] = "PathTracingInlineTextureCubeArray",
            ["PathTracingInlineTexture3D"] = "PathTracingInlineTexture3D",
            ["PathTracingInlineSamplerComparison"] = "PathTracingInlineSamplerComparison",
            ["Texture2DArray"] = "PathTracingInlineTexture2DArray",
            ["TextureCube"] = "PathTracingInlineTextureCube",
            ["TextureCubeArray"] = "PathTracingInlineTextureCubeArray",
            ["Texture3D"] = "PathTracingInlineTexture3D",
            ["SamplerComparisonState"] = "PathTracingInlineSamplerComparison"
        };

        private static string MarkerType(string type)
        {
            int generic = type.IndexOf('<');
            string plain = (generic >= 0 ? type.Substring(0, generic) : type).Trim();
            if (plain == "Texture2D")
                return "PathTracingInlineTexture2D";
            if (plain == "SamplerState")
                return "PathTracingInlineSampler";
            return s_MarkerTypes.TryGetValue(plain, out string marker) ? marker : type;
        }

        public static Unit Transform(string preprocessed, string unitNamespace, int materialStart = 0)
        {
            HlslDocument document = ShaderSource.ParseHlsl(preprocessed);
            var unit = new Unit();
            var local = document.Declarations.Where(d => d.Node.Span.StartIndex >= materialStart).ToList();
            var replacements = new Dictionary<HlslDeclaration, string>();
            var hoisted = new HashSet<HlslDeclaration>();
            var mandatory = new HashSet<HlslDeclaration>();
            var textureSlots = new Dictionary<string, int>();
            var samplers = new Dictionary<HlslDeclaration, List<string>>();

            foreach (HlslDeclaration declaration in local)
            {
                HLSLSyntaxNode node = declaration.Node;
                if (declaration.Names.Length == 0) mandatory.Add(declaration);
                if (node is ConstantBufferNode buffer)
                {
                    string name = buffer.Name.GetName();
                    if (name == MaterialConstantsBuffer || name == ObjectConstantsBuffer)
                    {
                        replacements[declaration] = name == MaterialConstantsBuffer
                            ? MaterialConstants(document, buffer, unit)
                            : ObjectConstants(document, buffer, unit);
                        mandatory.Add(declaration);
                    }
                    else hoisted.Add(declaration);
                }
                else if (node is VariableDeclarationStatementNode variable)
                {
                    if (variable.Modifiers.Contains(BindingModifier.Static) || variable.Modifiers.Contains(BindingModifier.Const))
                        continue;
                    string type = MarkerType(ShaderSource.Code(document.Source, variable.Kind));
                    var text = new StringBuilder();
                    foreach (VariableDeclaratorNode declarator in variable.Declarators)
                    {
                        string name = declarator.Name.Identifier;
                        if (declarator.ArrayRanks.Count != 0 &&
                            (type == "PathTracingInlineTexture2D" || type == "PathTracingInlineSampler" || s_MarkerTypes.Values.Contains(type)))
                            throw Unsupported(declarator, $"resource array '{name}'");
                        if (type == "PathTracingInlineTexture2D")
                        {
                            if (!textureSlots.TryGetValue(name, out int slot))
                            {
                                slot = unit.Textures.Count;
                                unit.Textures.Add(name);
                                textureSlots.Add(name, slot);
                            }
                            text.Append($"static const uint {name} = {slot}u;\n");
                        }
                        else if (type == "PathTracingInlineSampler")
                        {
                            if (!samplers.TryGetValue(declaration, out var names))
                                samplers.Add(declaration, names = new List<string>());
                            names.Add(name);
                        }
                        else if (s_MarkerTypes.Values.Contains(type))
                            text.Append($"static const {type} {name} = ({type})0;\n");
                        else if (variable.Kind is PredefinedObjectTypeNode)
                        {
                            hoisted.Add(declaration);
                            unit.HoistedResources[name] = type;
                        }
                        else hoisted.Add(declaration);
                    }
                    if (text.Length > 0) replacements[declaration] = text.ToString();
                }
                else if (node is FunctionNode function)
                {
                    string name = function.Name.GetName();
                    unit.HasClosestHit |= name == "PathTracingClosestHit";
                    unit.HasAnyHit |= name == "PathTracingAnyHit";
                }
            }

            foreach (var pair in samplers)
                replacements[pair.Key] = string.Join("\n", pair.Value.Select(name =>
                    $"static const uint {name} = {SamplerSlot(name, textureSlots)};"));

            foreach (HlslDeclaration dependency in document.Reachable(hoisted).ToArray())
                if (local.Contains(dependency) && (dependency.Node is StructDefinitionNode || dependency.Node is TypedefNode ||
                    dependency.Node is EnumDefinitionNode || dependency.Node is VariableDeclarationStatementNode constant &&
                    constant.Modifiers.Contains(BindingModifier.Const)))
                    hoisted.Add(dependency);
            foreach (HlslDeclaration declaration in local.Where(hoisted.Contains))
                unit.Hoisted.Add(new KeyValuePair<string, string>(declaration.Key,
                    InScope(declaration.Scope, ShaderSource.LocatedCode(document.Source, declaration.Node)) + "\n"));

            unit.ConstantsOffset = (unit.Textures.Count * 4 + 15) & ~15;
            for (int i = 0; i < unit.Constants.Count; i++)
            {
                Constant constant = unit.Constants[i];
                constant.Offset = unit.ConstantsOffset + i * 16;
                unit.Constants[i] = constant;
            }
            unit.RecordSize = unit.ConstantsOffset + unit.Constants.Count * 16;
            unit.Body = Emit(false);
            unit.AnyHitBody = PathTracingCoverageSlice.Apply(Emit(true));
            unit.SharedHitContext = HitContextSignature(document, new HashSet<HlslDeclaration>(local));
            return unit;

            string Emit(bool anyHitOnly)
            {
                var roots = new List<HlslDeclaration>(mandatory);
                roots.AddRange(hoisted);
                roots.AddRange(document.Find("PathTracingAnyHit"));
                if (!anyHitOnly) roots.AddRange(document.Find("PathTracingClosestHit"));
                HashSet<HlslDeclaration> reachable = document.Reachable(roots);
                var body = new StringBuilder();
                foreach (HlslDeclaration declaration in local)
                {
                    if (!reachable.Contains(declaration) || hoisted.Contains(declaration)) continue;
                    string code;
                    if (!replacements.TryGetValue(declaration, out code))
                    {
                        var node = declaration.Node;
                        code = ShaderSource.Code(document.Source, node);
                        if (node is FunctionNode function)
                            code = ShaderSource.Edit(code, function.Attributes
                                .Where(a => a.Name.Identifier == "shader")
                                .Select(a => (a.Span.StartIndex - node.Span.StartIndex, a.Span.Length, "")));
                    }
                    if (!string.IsNullOrEmpty(declaration.Node.Span.FileName))
                        body.Append($"#line {declaration.Node.Span.Start.Line} \"{declaration.Node.Span.FileName}\"\n");
                    body.Append(InScope(declaration.Scope, code)).Append('\n');
                }
                string loader = MaterialLoader(unit);
                string content = body.ToString();
                content = content.Contains(MaterialLoaderMarker)
                    ? content.Replace(MaterialLoaderMarker, loader) : loader + content;
                string loadObject = unit.HasObjectConstants ? "    PathTracingInlineLoadObject();\n" : "";
                var result = new StringBuilder($"namespace {unitNamespace}\n{{\n");
                result.Append(content);
                result.Append("\nvoid PathTracingInlineClosestHit(inout IllusionPathPayload payload, AttributeData attributes)\n{\n");
                if (unit.HasClosestHit && !anyHitOnly)
                    result.Append(loadObject).Append("    PathTracingInlineLoadMaterial(g_PathTracingInlineMaterialBase);\n    PathTracingClosestHit(payload, attributes);\n");
                result.Append("}\nbool PathTracingInlineAnyHit(inout IllusionPathPayload payload, AttributeData attributes)\n{\n");
                if (unit.HasAnyHit)
                    result.Append(loadObject).Append("    PathTracingInlineLoadMaterial(g_PathTracingInlineMaterialBase);\n    g_PathTracingInlineIgnored = false;\n    PathTracingAnyHit(payload, attributes);\n    return !g_PathTracingInlineIgnored;\n");
                else result.Append("    return true;\n");
                return result.Append("}\n}\n").ToString();
            }
        }

        private static string InScope(string scope, string code)
        {
            if (scope.Length == 0) return code;
            string[] parts = scope.Split(new[] { "::" }, StringSplitOptions.None);
            return string.Concat(parts.Select(p => "namespace " + p + "\n{\n")) + code + "\n" +
                   string.Concat(parts.Select(_ => "}\n"));
        }

        private static Exception Unsupported(HLSLSyntaxNode node, string feature) =>
            new InvalidOperationException($"{node.Span.FileName}({node.Span.Start.Line}): inline material layout does not support {feature}.");

        private const string MaterialLoaderMarker = "/*PT_INLINE_MATERIAL_LOADER*/";

        private static string MaterialConstants(HlslDocument document, ConstantBufferNode buffer, Unit unit)
        {
            var declarations = new StringBuilder();
            foreach (var variable in buffer.Declarations)
            {
                string type = ShaderSource.Code(document.Source, variable.Kind);
                int components = Components(type);
                foreach (var field in variable.Declarators)
                {
                    string name = field.Name.Identifier;
                    if (components == 0 || BaseType(type) == "double" || field.ArrayRanks.Count != 0)
                        throw Unsupported(field, $"'{type} {name}'");
                    unit.Constants.Add(new Constant { Name = name, Type = type, Components = components, Count = 0 });
                    declarations.Append($"static {type} {name};\n");
                }
            }
            return declarations.Append(MaterialLoaderMarker).Append('\n').ToString();
        }

        private static string ObjectConstants(HlslDocument document, ConstantBufferNode buffer, Unit unit)
        {
            var declarations = new StringBuilder();
            var names = new HashSet<string>();
            foreach (var variable in buffer.Declarations)
                foreach (var field in variable.Declarators)
                {
                    names.Add(field.Name.Identifier);
                    declarations.Append("static ").Append(ShaderSource.Code(document.Source, variable.Kind))
                        .Append(' ').Append(field.Name.Identifier);
                    foreach (var rank in field.ArrayRanks) declarations.Append(ShaderSource.Code(document.Source, rank));
                    declarations.Append(";\n");
                }
            unit.HasObjectConstants = true;
            declarations.Append("void PathTracingInlineLoadObject()\n{\n");
            if (names.Contains("unity_ObjectToWorld"))
                declarations.Append("    unity_ObjectToWorld = float4x4(ObjectToWorld3x4()[0], ObjectToWorld3x4()[1], ObjectToWorld3x4()[2], float4(0, 0, 0, 1));\n");
            if (names.Contains("unity_WorldToObject"))
                declarations.Append("    unity_WorldToObject = float4x4(WorldToObject3x4()[0], WorldToObject3x4()[1], WorldToObject3x4()[2], float4(0, 0, 0, 1));\n");
            if (names.Contains("unity_MatrixPreviousM"))
                declarations.Append("    unity_MatrixPreviousM = float4x4(ObjectToWorld3x4()[0], ObjectToWorld3x4()[1], ObjectToWorld3x4()[2], float4(0, 0, 0, 1));\n");
            if (names.Contains("unity_MatrixPreviousMI"))
                declarations.Append("    unity_MatrixPreviousMI = float4x4(WorldToObject3x4()[0], WorldToObject3x4()[1], WorldToObject3x4()[2], float4(0, 0, 0, 1));\n");
            if (names.Contains("unity_WorldTransformParams"))
                declarations.Append("    unity_WorldTransformParams = float4(0, 0, 0, determinant((float3x3)ObjectToWorld3x4()) < 0.0 ? -1.0 : 1.0);\n");
            if (names.Contains("unity_RenderingLayer"))
                declarations.Append("    unity_RenderingLayer = float4(asfloat(_PathTracingInstanceData[InstanceID()].renderingLayers), 0, 0, 0);\n");
            if (names.Contains("unity_LODFade"))
                declarations.Append("    unity_LODFade = float4(1, 16, 0, 0);\n");
            declarations.Append("}\n");
            return declarations.ToString();
        }

        private static string MaterialLoader(Unit unit)
        {
            var sb = new StringBuilder("void PathTracingInlineLoadMaterial(uint recordBase)\n{\n");
            foreach (Constant c in unit.Constants)
            {
                sb.Append($"    {c.Name} = ({c.Type}){Load(c, c.Offset)};\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string Load(Constant c, int offset)
        {
            string raw = c.Components switch
            {
                1 => $"_PathTracingInlineMaterialData.Load(recordBase + {offset}u)",
                2 => $"_PathTracingInlineMaterialData.Load2(recordBase + {offset}u)",
                3 => $"_PathTracingInlineMaterialData.Load3(recordBase + {offset}u)",
                _ => $"_PathTracingInlineMaterialData.Load4(recordBase + {offset}u)"
            };
            string scalar = BaseType(c.Type);
            return scalar switch
            {
                "int" or "min16int" or "min12int" => $"asint({raw})",
                "uint" or "min16uint" => raw,
                "bool" => $"({raw} != 0)",
                _ => $"asfloat({raw})"
            };
        }

        internal static string BaseType(string type)
        {
            int i = type.Length;
            while (i > 0 && (char.IsDigit(type[i - 1]) || type[i - 1] == 'x'))
                i--;
            return i > 0 ? type.Substring(0, i) : type;
        }

        internal static int Components(string type)
        {
            string scalar = BaseType(type);
            if (scalar is not ("float" or "half" or "int" or "uint" or "bool" or "double" or "min16float" or "min10float" or "min16int" or "min12int" or "min16uint" or "real"))
                return 0;
            string suffix = type.Substring(scalar.Length);
            if (suffix.Length == 0)
                return 1;
            if (suffix.Contains("x"))
                return 0;
            return int.TryParse(suffix, out int n) && n >= 1 && n <= 4 ? n : 0;
        }

        private static string SamplerSlot(string name, Dictionary<string, int> textures)
        {
            if (name.StartsWith("sampler", StringComparison.Ordinal))
            {
                string texture = name.Substring("sampler".Length);
                if (textures.TryGetValue(texture, out int slot))
                    return $"{slot + 1}u";
                if (texture.StartsWith("_", StringComparison.Ordinal) && textures.TryGetValue(texture.Substring(1), out slot))
                    return $"{slot + 1}u";
            }

            string lower = name.ToLowerInvariant();
            bool named = lower.Contains("point") || lower.Contains("linear") || lower.Contains("trilinear");
            if (!named)
                return "0u";
            uint filter = lower.Contains("point") ? 0u : lower.Contains("trilinear") ? 2u : 1u;
            uint wrap = lower.Contains("mirror") ? 2u : lower.Contains("clamp") ? 1u : 0u;
            return $"(PT_INLINE_SAMPLER_STATE | {filter | (wrap << 2)}u)";
        }

        public static string RemoveDeclared(string preprocessed, string declaredBefore)
        {
            return RemoveDeclared(preprocessed, ShaderSource.ParseHlsl(declaredBefore));
        }

        internal static string RemoveDeclared(string preprocessed, HlslDocument before)
        {
            HlslDocument current = ShaderSource.ParseHlsl(preprocessed);
            var known = before.Declarations.GroupBy(DeclarationKey).ToDictionary(g => g.Key,
                g => new HashSet<string>(g.Select(d => ShaderSource.Fingerprint(d.Node))));
            var edits = new List<(int Start, int Length, string Text)>();
            foreach (var declaration in current.Declarations)
            {
                if (!known.TryGetValue(DeclarationKey(declaration), out var definitions)) continue;
                if (!definitions.Contains(ShaderSource.Fingerprint(declaration.Node)))
                    throw new InvalidOperationException($"Conflicting inline declaration '{declaration.Key}'.");
                edits.Add((declaration.Node.Span.StartIndex, declaration.Node.Span.Length, ""));
            }
            return ShaderSource.Edit(preprocessed, edits);
        }

        private static string DeclarationKey(HlslDeclaration declaration) => declaration.Node is ConstantBufferNode
            ? declaration.Key.Substring(declaration.Scope.Length) : declaration.Key;

        internal static string HitContextSignature(string body)
        {
            HlslDocument document = ShaderSource.ParseHlsl(body);
            return HitContextSignature(document, new HashSet<HlslDeclaration>(document.Declarations));
        }

        private static string HitContextSignature(HlslDocument document, HashSet<HlslDeclaration> local)
        {
            var context = local.FirstOrDefault(d => d.Names.Contains("PathTracingGetHitContext"));
            if (context == null) return "";
            string[] shared = { "PathTracingGetHitContext", "PathTracingFetchVertex", "SafeNormalize", "g_PathTracingUVFootprint" };
            var roots = shared.SelectMany(name => document.Find(name, context.Scope)).ToArray();
            if (shared.Any(name => !roots.Any(d => d.Names.Contains(name)))) return null;
            if (roots.Any(d => d.Node is FunctionNode function && function.Parameters.Any(p =>
                p.Modifiers.Contains(BindingModifier.Out) || p.Modifiers.Contains(BindingModifier.Inout)))) return null;
            var allowed = new HashSet<HlslDeclaration>(roots);
            if (roots.Any(d => d.Dependencies.Any(dependency => local.Contains(dependency) && !allowed.Contains(dependency)))) return null;
            var writes = new HashSet<string> { "g_PathTracingUVFootprint", "g_PathTracingInlineFootprintGeometry" };
            if (roots.Any(d => d.Writes.Any(name => !writes.Contains(name)))) return null;
            var external = new HashSet<string>
            {
                "PathTracingHitContext", "PathTracingHitVertex", "AttributeData", "IllusionPathPayload",
                "g_PathTracingInlineHitContextReady", "g_PathTracingInlineCachedHit", "g_PathTracingInline",
                "g_PathTracingInlineFootprintGeometry", "UnityRayTracingFetchTriangleIndices",
                "UnityRayTracingFetchVertexAttribute3", "UnityRayTracingFetchVertexAttribute4", "UnityRayTracingHasVertexAttribute",
                "all", "abs", "mul", "cross", "length", "normalize", "dot", "determinant", "max", "sqrt", "rsqrt"
            };
            if (roots.Any(d => d.ExternalReferences.Any(name => !external.Contains(name)) ||
                d.Dependencies.Any(dependency => !local.Contains(dependency) && !dependency.Names.Any(external.Contains)))) return null;
            return string.Join("\n", roots.Select(d => ShaderSource.Fingerprint(d.Node)));
        }
    }
}
