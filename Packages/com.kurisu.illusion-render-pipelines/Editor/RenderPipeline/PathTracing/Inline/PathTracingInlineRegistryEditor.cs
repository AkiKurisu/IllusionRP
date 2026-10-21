using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PathTracingInlineRegistry))]
    internal sealed class PathTracingInlineRegistryEditor : UnityEditor.Editor
    {
        private const int ErrorLines = 6;

        private sealed class ShaderGroup
        {
            public Shader Shader;
            public string Name;
            public List<PathTracingInlineRegistry.Evaluator> Evaluators;
            public bool Expanded;
        }

        private sealed class MissingVariant
        {
            public string ShaderPath;
            public string Keywords;
            public string Error;
            public readonly List<string> Materials = new();
            public bool Expanded;
        }

        private static class Styles
        {
            public static readonly GUIContent Evaluators = new("Evaluators");
            public static readonly GUIContent Regenerate = new("Regenerate", "Regenerate the evaluators whose shaders, includes or materials changed.");
            public static readonly GUIContent RebuildAll = new("Rebuild All", "Regenerate every evaluator and kernel, ignoring the cache.");
            public static readonly GUIContent InitialSamples = new("Initial Samples");
            public static readonly GUIContent Resampling = new("Resampling");
            public static readonly GUIContent NoKeywords = new("No keywords");

            public static readonly GUIStyle Problem = new(EditorStyles.wordWrappedMiniLabel)
            {
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.62f, 0.35f) : new Color(0.7f, 0.3f, 0f) }
            };

            public static readonly GUIStyle Detail = new(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        }

        private readonly List<ShaderGroup> _groups = new();
        private readonly List<MissingVariant> _missing = new();
        private string _search = string.Empty;

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            var registry = (PathTracingInlineRegistry)target;
            _groups.Clear();
            foreach (IGrouping<Shader, PathTracingInlineRegistry.Evaluator> group in registry.Evaluators.Where(e => e.Shader).GroupBy(e => e.Shader))
            {
                _groups.Add(new ShaderGroup
                {
                    Shader = group.Key,
                    Name = group.Key.name,
                    Evaluators = group.OrderBy(e => string.Join(" ", e.Keywords), StringComparer.Ordinal).ToList()
                });
            }
            _groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            CollectMissing(registry);
        }

        private void CollectMissing(PathTracingInlineRegistry registry)
        {
            _missing.Clear();
            string Guid(Shader shader) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shader));
            var generated = new HashSet<(string, string)>(registry.Evaluators.Where(e => e.Shader)
                .Select(e => (Guid(e.Shader), string.Join(" ", e.Keywords))));
            var passKeywords = new Dictionary<string, HashSet<string>>();
            foreach (PathTracingInlineRegistry.ShaderKeywords entry in registry.PassKeywords.Where(p => p.Shader))
                passKeywords[Guid(entry.Shader)] = new HashSet<string>(entry.Keywords);

            var variants = new Dictionary<(string, string), MissingVariant>();
            MissingVariant Variant(string shaderGuid, string keywords)
            {
                if (!variants.TryGetValue((shaderGuid, keywords), out MissingVariant variant))
                    variants.Add((shaderGuid, keywords), variant = new MissingVariant { ShaderPath = AssetDatabase.GUIDToAssetPath(shaderGuid), Keywords = keywords });
                return variant;
            }

            foreach (GeneratorReport.Failure failure in GeneratorReport.Load().Failures)
                Variant(AssetDatabase.AssetPathToGUID(failure.Shader), failure.Keywords).Error = failure.Error;
            foreach ((string path, string shaderGuid, string keywords) in MaterialScanCache.Load().Materials())
            {
                if (string.IsNullOrEmpty(shaderGuid) || !passKeywords.TryGetValue(shaderGuid, out HashSet<string> pass))
                    continue;
                string key = string.Join(" ", keywords.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                    .Where(pass.Contains).Distinct().OrderBy(k => k, StringComparer.Ordinal));
                if (!generated.Contains((shaderGuid, key)))
                    Variant(shaderGuid, key).Materials.Add(path);
            }
            _missing.AddRange(variants.Values.OrderBy(v => v.ShaderPath, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Keywords, StringComparer.Ordinal));
        }

        public override void OnInspectorGUI()
        {
            var registry = (PathTracingInlineRegistry)target;
            DrawSummary(registry);
            if (_missing.Count > 0)
            {
                EditorGUILayout.Space();
                DrawMissing();
            }
            EditorGUILayout.Space();
            DrawEvaluators();
        }

        private void DrawSummary(PathTracingInlineRegistry registry)
        {
            EditorGUILayout.LabelField($"{registry.Evaluators.Length} evaluators from {_groups.Count} shaders, {registry.Buffers.Length} scene buffers",
                EditorStyles.boldLabel);
            EditorGUILayout.ObjectField(Styles.InitialSamples, registry.Performance?.Compute, typeof(ComputeShader), false);
            EditorGUILayout.ObjectField(Styles.Resampling, registry.RayTracedGIResampling, typeof(ComputeShader), false);
            EditorGUILayout.LabelField("Hash", registry.Hash);
            using (new EditorGUI.DisabledScope(!PathTracingInlineGenerator.IsEnabled))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Styles.Regenerate, GUILayout.Width(100)))
                    Regenerate(false);
                if (GUILayout.Button(Styles.RebuildAll, GUILayout.Width(100)))
                    Regenerate(true);
            }
        }

        private void DrawMissing()
        {
            int materials = _missing.Sum(v => v.Materials.Count);
            EditorGUILayout.LabelField($"{Count(_missing.Count, "variant")} without an inline evaluator, used by {Count(materials, "material")}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Their surfaces render as the diagnostic surface in inline ray tracing.", Styles.Problem);
            foreach (MissingVariant variant in _missing)
            {
                string keywords = variant.Keywords.Length > 0 ? variant.Keywords : Styles.NoKeywords.text;
                variant.Expanded = EditorGUILayout.Foldout(variant.Expanded,
                    $"{System.IO.Path.GetFileNameWithoutExtension(variant.ShaderPath)}  {keywords}  ({variant.Materials.Count})", true);
                if (!variant.Expanded)
                    continue;
                using (new EditorGUI.IndentLevelScope())
                {
                    string error = variant.Error != null
                        ? string.Join("\n", variant.Error.Split('\n').Take(ErrorLines))
                        : "Not generated: no material used this variant when the evaluators were generated.";
                    EditorGUILayout.LabelField(error, Styles.Problem);
                    foreach (string path in variant.Materials)
                        if (EditorGUILayout.LinkButton(path))
                            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
                }
            }
        }

        private void DrawEvaluators()
        {
            EditorGUILayout.LabelField(Styles.Evaluators, EditorStyles.boldLabel);
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            foreach (ShaderGroup group in _groups)
            {
                bool shaderMatches = Matches(group.Name);
                if (!shaderMatches && !group.Evaluators.Any(e => Matches(string.Join(" ", e.Keywords))))
                    continue;
                bool revealed = _search.Length > 0 && !shaderMatches;
                bool expanded = EditorGUILayout.Foldout(group.Expanded || revealed, $"{group.Name}  ({group.Evaluators.Count})", true);
                if (!revealed)
                    group.Expanded = expanded;
                if (!expanded)
                    continue;
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.ObjectField(group.Shader, typeof(Shader), false);
                    foreach (PathTracingInlineRegistry.Evaluator evaluator in group.Evaluators)
                    {
                        string keywords = string.Join(" ", evaluator.Keywords);
                        if (!shaderMatches && !Matches(keywords))
                            continue;
                        Rect row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        string detail = evaluator.HasAnyHit ? $"{evaluator.RecordSize} B, any-hit" : $"{evaluator.RecordSize} B";
                        float detailWidth = Styles.Detail.CalcSize(new GUIContent(detail)).x;
                        GUI.Label(new Rect(row.x, row.y, row.width - detailWidth - 4f, row.height),
                            keywords.Length > 0 ? new GUIContent(keywords, keywords) : Styles.NoKeywords,
                            keywords.Length > 0 ? EditorStyles.label : EditorStyles.miniLabel);
                        GUI.Label(new Rect(row.xMax - detailWidth, row.y, detailWidth, row.height), detail, Styles.Detail);
                    }
                }
            }
        }

        private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        private bool Matches(string text) => _search.Length == 0 || text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void Regenerate(bool force)
        {
            EditorApplication.delayCall += () =>
            {
                PathTracingInlineGenerator.Generate(force);
                if (!this)
                    return;
                Refresh();
                Repaint();
            };
        }
    }
}
