using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
// UnityEditor also has an unrelated PackageInfo (the legacy .unitypackage one).
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityRhi.EditorTools
{
    /// <summary>
    /// Translation between asset-database paths and real file-system paths.
    ///
    /// "Assets/..." is relative to the project and resolves directly, but
    /// "Packages/&lt;name&gt;/..." is a virtual path: it only matches a real
    /// directory when the package happens to be embedded under the project.
    /// This project references its packages by file: path from a sibling folder,
    /// so both directions have to go through the package manager.
    /// </summary>
    internal static class RhiShaderPaths
    {
        private static Dictionary<string, string> s_packageRootsByResolvedPath;

        /// <summary>Absolute directory or file for an asset-database path.</summary>
        internal static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;
            if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                PackageInfo package = PackageInfo.FindForAssetPath(assetPath);
                if (package != null)
                {
                    // package.assetPath is "Packages/<name>"; keep whatever follows it.
                    string relative = assetPath.Length > package.assetPath.Length
                        ? assetPath.Substring(package.assetPath.Length).TrimStart('/')
                        : string.Empty;
                    return Path.GetFullPath(Path.Combine(package.resolvedPath, relative));
                }
            }
            return Path.GetFullPath(assetPath);
        }

        /// <summary>
        /// Asset-database path for an absolute path, or null when the file is
        /// outside the project and every registered package. Package files must
        /// go through the resolved-path map: they live outside the project
        /// folder here, so a plain project-prefix test silently drops them.
        /// </summary>
        internal static string ToAssetPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;
            string full = Path.GetFullPath(absolutePath);

            string project = ProjectRoot;
            if (full.StartsWith(project, StringComparison.OrdinalIgnoreCase))
            {
                string relative = full.Substring(project.Length).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                    return relative;
            }

            foreach (KeyValuePair<string, string> package in PackageRootsByResolvedPath)
            {
                if (!full.StartsWith(package.Key, StringComparison.OrdinalIgnoreCase)) continue;
                string relative = full.Substring(package.Key.Length)
                    .Replace(Path.DirectorySeparatorChar, '/').TrimStart('/');
                return relative.Length == 0 ? package.Value : package.Value + "/" + relative;
            }
            return null;
        }

        /// <summary>
        /// Resolves a path stored in importer settings. Paths starting with "Assets/"
        /// or "Packages/" are asset-database paths; anything else is relative to
        /// the marker file itself, which keeps intra-package references short and
        /// survives the package being renamed.
        /// </summary>
        internal static string ResolveManifestPath(string manifestAssetPath, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Replace('\\', '/');
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return ToAbsolutePath(path);
            string manifestDirectory = Path.GetDirectoryName(ToAbsolutePath(manifestAssetPath));
            return string.IsNullOrEmpty(manifestDirectory)
                ? null
                : Path.GetFullPath(Path.Combine(manifestDirectory, path));
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        private static Dictionary<string, string> PackageRootsByResolvedPath
        {
            get
            {
                if (s_packageRootsByResolvedPath != null) return s_packageRootsByResolvedPath;
                var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
                {
                    if (string.IsNullOrEmpty(package.resolvedPath) ||
                        string.IsNullOrEmpty(package.assetPath)) continue;
                    string resolved = Path.GetFullPath(package.resolvedPath)
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    roots[resolved] = package.assetPath;
                }
                s_packageRootsByResolvedPath = roots;
                return s_packageRootsByResolvedPath;
            }
        }
    }
}
