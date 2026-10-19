using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

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

        private static readonly Regex s_ShaderAttribute = new(@"\[\s*shader\s*\(\s*""[^""]*""\s*\)\s*\]", RegexOptions.Compiled);
        private static readonly Regex s_Identifier = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
        private static readonly Regex s_Member = new(@"^\s*(?:(?:uniform|row_major|column_major|precise|nointerpolation)\s+)*([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[\s*(\d+)\s*\])?\s*(?::\s*packoffset\s*\([^)]*\))?\s*$", RegexOptions.Compiled);

        private static readonly HashSet<string> s_KeepKeywords = new()
        {
            "struct", "typedef", "static", "const", "namespace", "groupshared", "inline", "template", "class", "interface", "enum"
        };

        private static readonly HashSet<string> s_Qualifiers = new()
        {
            "uniform", "extern", "shared", "volatile", "row_major", "column_major", "precise", "globallycoherent"
        };

        private static readonly string[] s_ResourcePrefixes =
        {
            "Texture", "RWTexture", "Buffer", "RWBuffer", "StructuredBuffer", "RWStructuredBuffer", "ByteAddressBuffer",
            "RWByteAddressBuffer", "AppendStructuredBuffer", "ConsumeStructuredBuffer", "SamplerState", "SamplerComparisonState",
            "RaytracingAccelerationStructure", "RasterizerOrdered", "FeedbackTexture"
        };

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
            string plain = generic >= 0 ? type.Substring(0, generic) : type;
            if (plain == "Texture2D")
                return "PathTracingInlineTexture2D";
            if (plain == "SamplerState")
                return "PathTracingInlineSampler";
            return s_MarkerTypes.TryGetValue(plain, out string marker) ? marker : type;
        }

        public static Unit Transform(string preprocessed, string unitNamespace)
        {
            var unit = new Unit();
            var segments = new List<string>();
            string text = s_ShaderAttribute.Replace(StripPragmas(preprocessed), string.Empty);

            var textureSlots = new Dictionary<string, int>();
            var pendingSamplers = new List<(int segment, string name)>();
            var hoisted = new List<(int Order, string Key, string Text)>();
            var structs = new List<(int Order, int Segment, string Name, string Text)>();
            var named = new List<(int Segment, string Name)>();
            int order = 0;

            foreach (string item in SplitTopLevel(text))
            {
                order++;
                string code = StripLineDirectives(item, out string lines);
                if (code.Trim().Length == 0)
                {
                    segments.Add(item);
                    continue;
                }

                List<string> words = LeadingWords(code);
                while (words.Count > 0 && s_Qualifiers.Contains(words[0]))
                    words.RemoveAt(0);
                string first = words.Count > 0 ? words[0] : string.Empty;

                if (first == "cbuffer" && words.Count > 1)
                {
                    if (words[1] == MaterialConstantsBuffer)
                        segments.Add(lines + MaterialConstants(code, unit));
                    else if (words[1] == ObjectConstantsBuffer)
                        segments.Add(lines + ObjectConstants(code, unit));
                    else
                        hoisted.Add((order, "cbuffer " + words[1], item));
                    continue;
                }

                if (first == "struct" && words.Count > 1 && code.Contains("{"))
                {
                    structs.Add((order, segments.Count, words[1], item));
                    named.Add((segments.Count, words[1]));
                    segments.Add(item);
                    continue;
                }

                if (s_KeepKeywords.Contains(first) || IsFunction(code) || words.Count < 2)
                {
                    if (IsFunction(code) && words.Count >= 2 && code.Contains("{"))
                    {
                        string name = FunctionName(code);
                        unit.HasClosestHit |= name == "PathTracingClosestHit";
                        unit.HasAnyHit |= name == "PathTracingAnyHit";
                        named.Add((segments.Count, name));
                    }
                    else if (first is "static" or "const" or "typedef" && DeclaredName(code) is { } declaredName)
                    {
                        named.Add((segments.Count, declaredName));
                    }
                    segments.Add(item);
                    continue;
                }

                string type = MarkerType(words[0]);
                string declared = words[1];
                if (type == "PathTracingInlineTexture2D")
                {
                    if (!textureSlots.TryGetValue(declared, out int slot))
                    {
                        slot = unit.Textures.Count;
                        unit.Textures.Add(declared);
                        textureSlots.Add(declared, slot);
                    }
                    named.Add((segments.Count, declared));
                    segments.Add(lines + $"static const uint {declared} = {slot}u;\n");
                }
                else if (type == "PathTracingInlineSampler")
                {
                    pendingSamplers.Add((segments.Count, declared));
                    named.Add((segments.Count, declared));
                    segments.Add(lines);
                }
                else if (type.StartsWith("PathTracingInline", StringComparison.Ordinal))
                {
                    named.Add((segments.Count, declared));
                    segments.Add(lines + $"static const {type} {declared} = ({type})0;\n");
                }
                else if (IsResource(type))
                {
                    hoisted.Add((order, declared, item));
                    unit.HoistedResources[declared] = type;
                }
                else if (IsGlobalVariable(code))
                {
                    hoisted.Add((order, declared, item));
                }
                else
                {
                    segments.Add(item);
                }
            }

            var namedSegments = new HashSet<int>();
            var byName = new Dictionary<string, List<int>>();
            foreach ((int segment, string name) in named)
            {
                namedSegments.Add(segment);
                if (!byName.TryGetValue(name, out List<int> list))
                    byName.Add(name, list = new List<int>());
                list.Add(segment);
            }
            var reachable = new HashSet<int>();
            var pending = new Stack<string>();
            pending.Push("PathTracingClosestHit");
            pending.Push("PathTracingAnyHit");
            for (int i = 0; i < segments.Count; i++)
                if (!namedSegments.Contains(i))
                    foreach (Match m in s_Identifier.Matches(StripLineDirectives(segments[i], out _)))
                        pending.Push(m.Value);
            foreach (var h in hoisted)
                foreach (Match m in s_Identifier.Matches(StripLineDirectives(h.Text, out _)))
                    pending.Push(m.Value);
            var visited = new HashSet<string>();
            while (pending.Count > 0)
            {
                string name = pending.Pop();
                if (!visited.Add(name) || !byName.TryGetValue(name, out List<int> list))
                    continue;
                foreach (int segment in list)
                    if (reachable.Add(segment))
                        foreach (Match m in s_Identifier.Matches(StripLineDirectives(segments[segment], out _)))
                            pending.Push(m.Value);
            }
            foreach (int segment in namedSegments)
                if (!reachable.Contains(segment))
                    segments[segment] = string.Empty;
            structs.RemoveAll(st => !reachable.Contains(st.Segment));

            var required = new HashSet<string>();
            foreach (var h in hoisted)
                foreach (Match m in s_Identifier.Matches(h.Text))
                    required.Add(m.Value);
            var hoistedStructs = new HashSet<int>();
            bool added = true;
            while (added)
            {
                added = false;
                for (int i = 0; i < structs.Count; i++)
                {
                    if (hoistedStructs.Contains(i) || !required.Contains(structs[i].Name))
                        continue;
                    hoistedStructs.Add(i);
                    added = true;
                    foreach (Match m in s_Identifier.Matches(structs[i].Text))
                        required.Add(m.Value);
                }
            }
            foreach (int i in hoistedStructs)
            {
                hoisted.Add((structs[i].Order, "struct " + structs[i].Name, structs[i].Text));
                segments[structs[i].Segment] = string.Empty;
            }
            hoisted.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var h in hoisted)
                unit.Hoisted.Add(new KeyValuePair<string, string>(h.Key, h.Text.Trim() + "\n"));

            foreach ((int segment, string name) in pendingSamplers)
                segments[segment] += $"static const uint {name} = {SamplerSlot(name, textureSlots)};\n";
            var body = new StringBuilder();
            foreach (string segment in segments)
                body.Append(segment).Append('\n');

            int textureBytes = unit.Textures.Count * 4;
            unit.ConstantsOffset = (textureBytes + 15) & ~15;
            int constantSlots = 0;
            for (int i = 0; i < unit.Constants.Count; i++)
            {
                Constant c = unit.Constants[i];
                c.Offset = unit.ConstantsOffset + constantSlots * 16;
                constantSlots += Math.Max(1, c.Count);
                unit.Constants[i] = c;
            }
            unit.RecordSize = unit.ConstantsOffset + constantSlots * 16;

            string loader = MaterialLoader(unit);
            string code2 = body.ToString().Replace(MaterialLoaderMarker, loader);
            if (!code2.Contains("void PathTracingInlineLoadMaterial("))
                code2 = loader + code2;

            string loadObject = unit.HasObjectConstants ? "    PathTracingInlineLoadObject();\n" : string.Empty;
            var result = new StringBuilder();
            result.Append("namespace ").Append(unitNamespace).Append("\n{\n");
            result.Append(code2);
            result.Append("\nvoid PathTracingInlineClosestHit(inout IllusionPathPayload payload, AttributeData attributes)\n{\n");
            if (unit.HasClosestHit)
                result.Append(loadObject).Append("    PathTracingInlineLoadMaterial(g_PathTracingInlineMaterialBase);\n    PathTracingClosestHit(payload, attributes);\n");
            result.Append("}\n");
            result.Append("bool PathTracingInlineAnyHit(inout IllusionPathPayload payload, AttributeData attributes)\n{\n");
            if (unit.HasAnyHit)
                result.Append(loadObject).Append("    PathTracingInlineLoadMaterial(g_PathTracingInlineMaterialBase);\n    g_PathTracingInlineIgnored = false;\n    PathTracingAnyHit(payload, attributes);\n    return !g_PathTracingInlineIgnored;\n");
            else
                result.Append("    return true;\n");
            result.Append("}\n}\n");
            unit.Body = result.ToString();
            return unit;
        }

        private const string MaterialLoaderMarker = "/*PT_INLINE_MATERIAL_LOADER*/";

        private static string MaterialConstants(string cbuffer, Unit unit)
        {
            int open = cbuffer.IndexOf('{');
            int close = cbuffer.LastIndexOf('}');
            var declarations = new StringBuilder();
            if (open >= 0 && close > open)
            {
                foreach (string statement in cbuffer.Substring(open + 1, close - open - 1).Split(';'))
                {
                    Match m = s_Member.Match(statement.Replace('\n', ' ').Replace('\r', ' '));
                    if (!m.Success)
                        continue;
                    string type = m.Groups[1].Value;
                    string name = m.Groups[2].Value;
                    int count = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                    int components = Components(type);
                    unit.Constants.Add(new Constant { Name = name, Type = type, Components = components, Count = count });
                    declarations.Append("static ").Append(type).Append(' ').Append(name);
                    if (count > 0)
                        declarations.Append('[').Append(count).Append(']');
                    declarations.Append(";\n");
                }
            }
            declarations.Append(MaterialLoaderMarker).Append('\n');
            return declarations.ToString();
        }

        private static string ObjectConstants(string cbuffer, Unit unit)
        {
            int open = cbuffer.IndexOf('{');
            int close = cbuffer.LastIndexOf('}');
            var declarations = new StringBuilder();
            var names = new HashSet<string>();
            if (open >= 0 && close > open)
            {
                foreach (string statement in cbuffer.Substring(open + 1, close - open - 1).Split(';'))
                {
                    Match m = s_Member.Match(statement.Replace('\n', ' ').Replace('\r', ' '));
                    if (!m.Success)
                        continue;
                    names.Add(m.Groups[2].Value);
                    declarations.Append("static ").Append(m.Groups[1].Value).Append(' ').Append(m.Groups[2].Value);
                    if (m.Groups[3].Success)
                        declarations.Append('[').Append(m.Groups[3].Value).Append(']');
                    declarations.Append(";\n");
                }
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
                if (c.Components <= 0)
                    continue;
                if (c.Count > 0)
                {
                    for (int i = 0; i < c.Count; i++)
                        sb.Append($"    {c.Name}[{i}] = ({c.Type}){Load(c, c.Offset + i * 16)};\n");
                }
                else
                {
                    sb.Append($"    {c.Name} = ({c.Type}){Load(c, c.Offset)};\n");
                }
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

        private static bool IsResource(string type)
        {
            foreach (string prefix in s_ResourcePrefixes)
                if (type.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool IsGlobalVariable(string code)
        {
            int paren = code.IndexOf('(');
            return paren < 0 || IsRegisterParen(code, paren);
        }

        public static string RemoveDeclared(string preprocessed, string declaredBefore)
        {
            var items = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string item in SplitTopLevel(declaredBefore))
            {
                string code = StripLineDirectives(item, out _);
                items.Add(NormalizeItem(code));
                AddItemKeys(code, keys);
            }
            var result = new StringBuilder(preprocessed.Length);
            RemoveDeclared(preprocessed, items, keys, result, false);
            return result.ToString();
        }

        // Inside namespaces only constant buffers are removed, since their members are global wherever they are declared.
        private static void RemoveDeclared(string text, HashSet<string> items, HashSet<string> keys, StringBuilder result, bool nested)
        {
            var itemKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string item in SplitTopLevel(text))
            {
                string code = StripLineDirectives(item, out string directives);
                List<string> words = LeadingWords(code);
                int open = item.IndexOf('{');
                int close = item.LastIndexOf('}');
                if (words.Count > 0 && words[0] == "namespace" && open >= 0 && close > open)
                {
                    result.Append(item, 0, open + 1);
                    RemoveDeclared(item.Substring(open + 1, close - open - 1), items, keys, result, true);
                    result.Append(item, close, item.Length - close).Append('\n');
                    continue;
                }
                itemKeys.Clear();
                AddItemKeys(code, itemKeys);
                bool declared = nested
                    ? words.Count > 0 && words[0] == "cbuffer" && itemKeys.Overlaps(keys)
                    : items.Contains(NormalizeItem(code)) || !IsFunction(code) && itemKeys.Overlaps(keys);
                result.Append(declared ? directives : item);
                if (!declared && !item.EndsWith("\n", StringComparison.Ordinal))
                    result.Append('\n');
            }
        }

        private static string NormalizeItem(string code) => Regex.Replace(code, @"\s+", " ").Trim();

        private static void AddItemKeys(string code, HashSet<string> keys)
        {
            List<string> words = LeadingWords(code);
            while (words.Count > 0 && s_Qualifiers.Contains(words[0]))
                words.RemoveAt(0);
            if (words.Count < 2)
                return;
            if (words[0] == "cbuffer")
            {
                keys.Add("cbuffer " + words[1]);
                int open = code.IndexOf('{');
                int close = code.LastIndexOf('}');
                if (open >= 0 && close > open)
                    foreach (string statement in code.Substring(open + 1, close - open - 1).Split(';'))
                        if (s_Member.Match(statement.Replace('\n', ' ').Replace('\r', ' ')) is { Success: true } m)
                            keys.Add(m.Groups[2].Value);
            }
            else if (words[0] == "struct")
            {
                keys.Add("struct " + words[1]);
            }
            else if (!IsFunction(code) && !s_KeepKeywords.Contains(words[0]))
            {
                keys.Add(words[1]);
            }
        }

        private static bool IsFunction(string code)
        {
            int paren = code.IndexOf('(');
            if (paren < 0)
                return false;
            int brace = code.IndexOf('{');
            int equals = code.IndexOf('=');
            if (brace >= 0 && brace < paren)
                return false;
            if (equals >= 0 && equals < paren)
                return false;
            return !IsRegisterParen(code, paren);
        }

        private static bool IsRegisterParen(string code, int paren)
        {
            int end = paren - 1;
            while (end >= 0 && char.IsWhiteSpace(code[end]))
                end--;
            int start = end;
            while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '_'))
                start--;
            string word = code.Substring(start + 1, end - start);
            return word is "register" or "packoffset";
        }

        private static readonly Regex s_DeclaredName = new(@"([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[[^\]]*\]\s*)*(?:=|;|:)", RegexOptions.Compiled);

        private static string DeclaredName(string code)
        {
            Match m = s_DeclaredName.Match(code);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string FunctionName(string code)
        {
            int paren = code.IndexOf('(');
            int end = paren - 1;
            while (end >= 0 && char.IsWhiteSpace(code[end]))
                end--;
            int start = end;
            while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '_'))
                start--;
            return end > start ? code.Substring(start + 1, end - start) : string.Empty;
        }

        private static List<string> LeadingWords(string code)
        {
            var words = new List<string>();
            int i = 0;
            while (i < code.Length && words.Count < 6)
            {
                char ch = code[i];
                if (char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }
                if (ch == '[')
                {
                    int depth = 0;
                    for (; i < code.Length; i++)
                    {
                        if (code[i] == '[') depth++;
                        else if (code[i] == ']' && --depth == 0) { i++; break; }
                    }
                    continue;
                }
                if (ch == '(' || ch == '{' || ch == ';' || ch == '=' || ch == ':' || ch == ',')
                    break;
                Match m = s_Identifier.Match(code, i);
                if (!m.Success || m.Index != i)
                    break;
                string word = m.Value;
                i += word.Length;
                if (i < code.Length && code[i] == '<')
                {
                    int depth = 0;
                    int start = i;
                    for (; i < code.Length; i++)
                    {
                        if (code[i] == '<') depth++;
                        else if (code[i] == '>' && --depth == 0) { i++; break; }
                    }
                    word += code.Substring(start, i - start);
                }
                words.Add(word);
            }
            return words;
        }

        private static string StripPragmas(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#pragma", StringComparison.Ordinal))
                    continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private static string StripLineDirectives(string item, out string directives)
        {
            var code = new StringBuilder(item.Length);
            var lines = new StringBuilder();
            foreach (string line in item.Split('\n'))
            {
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    lines.Append(line).Append('\n');
                else
                    code.Append(line).Append('\n');
            }
            directives = lines.ToString();
            return code.ToString();
        }

        private static IEnumerable<string> SplitTopLevel(string text)
        {
            int depth = 0;
            int parens = 0;
            int start = 0;
            bool lineStart = true;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (lineStart && ch == '#')
                {
                    int end = text.IndexOf('\n', i);
                    i = end < 0 ? text.Length - 1 : end;
                    lineStart = true;
                    continue;
                }
                if (ch == '\n')
                {
                    lineStart = true;
                    continue;
                }
                if (!char.IsWhiteSpace(ch))
                    lineStart = false;

                switch (ch)
                {
                    case '{': depth++; break;
                    case '(': parens++; break;
                    case ')': parens--; break;
                    case ';':
                        if (depth == 0 && parens == 0)
                        {
                            yield return text.Substring(start, i + 1 - start);
                            start = i + 1;
                        }
                        break;
                    case '}':
                        depth--;
                        if (depth == 0 && parens == 0)
                        {
                            int next = i + 1;
                            while (next < text.Length && (text[next] == ' ' || text[next] == '\t' || text[next] == '\r' || text[next] == '\n'))
                                next++;
                            if (next >= text.Length || text[next] != ';')
                            {
                                yield return text.Substring(start, i + 1 - start);
                                start = i + 1;
                            }
                        }
                        break;
                }
            }
            if (start < text.Length)
                yield return text.Substring(start);
        }
    }
}
