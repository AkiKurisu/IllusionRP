using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal sealed class PathTracingInlinePostprocessor : AssetPostprocessor
    {
        private static bool s_Scheduled;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.delayCall += () =>
            {
                var registry = AssetDatabase.LoadAssetAtPath<PathTracingInlineRegistry>(PathTracingInlineGenerator.RegistryPath);
                if (PathTracingInlineGenerator.IsEnabled && (!registry || registry.Quality?.RayGenSer == null || registry.Fill?.RayGenSer == null))
                    Schedule();
            };
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool wasEnabled = PathTracingInlineGenerator.IsEnabled;
            if (imported.Any(IsRendererData) || deleted.Any(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
                PathTracingInlineGenerator.InvalidateEnabled();
            if (!PathTracingInlineGenerator.IsEnabled)
                return;
            if (!wasEnabled)
            {
                EditorApplication.delayCall += PathTracingInlinePluginInstaller.Install;
                Schedule();
                return;
            }
            if (AutoGenerate && (Affects(imported) || Affects(deleted) || Affects(moved)))
                Schedule();
        }

        private static bool IsRendererData(string path) =>
            typeof(UnityEngine.Rendering.Universal.ScriptableRendererData).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(path));

        private static bool Affects(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.StartsWith(PathTracingInlineGenerator.OutputFolder, StringComparison.Ordinal))
                    continue;
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".hlsli", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".raytrace", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase)
                    || MissesEvaluator(path))
                    return true;
            }
            return false;
        }

        private static bool MissesEvaluator(string path)
        {
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && AssetImporter.GetAtPath(path) is not (ModelImporter or SpeedTreeImporter))
                return false;
            var registry = AssetDatabase.LoadAssetAtPath<PathTracingInlineRegistry>(PathTracingInlineGenerator.RegistryPath);
            foreach (Material material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                if (!material.shader)
                    continue;
                if (!registry)
                    return true;
                if (registry.PassKeywords.Any(entry => entry.Shader == material.shader) && registry.FindEvaluator(material) == PathTracingInlineRegistry.NoEvaluator)
                    return true;
            }
            return false;
        }

        private static bool AutoGenerate => IllusionRenderPipelineSettings.instance.autoGenerateInlineEvaluators;

        internal static void Schedule()
        {
            if (s_Scheduled || !AutoGenerate)
                return;
            s_Scheduled = true;
            EditorApplication.delayCall += () =>
            {
                s_Scheduled = false;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Schedule();
                    return;
                }
                PathTracingInlineGenerator.Generate(false);
            };
        }
    }
}
