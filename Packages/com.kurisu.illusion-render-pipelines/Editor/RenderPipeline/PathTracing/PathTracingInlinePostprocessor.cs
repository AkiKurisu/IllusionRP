using System;
using UnityEditor;

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
            if (!PathTracingInlineGenerator.IsEnabled)
                return;
            if (Affects(imported) || Affects(deleted) || Affects(moved))
                Schedule();
        }

        private static bool Affects(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.StartsWith(PathTracingInlineGenerator.OutputFolder, StringComparison.Ordinal))
                    continue;
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".raytrace", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void Schedule()
        {
            if (s_Scheduled)
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
