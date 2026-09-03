using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UnityRhi.EditorTools
{
    /// <summary>
    /// Walks "#include" edges to find every file a program is built from. The
    /// importer registers the result as source dependencies, so unlike the old
    /// compiler this only has to enumerate the files; invalidating and
    /// rebuilding on a change is the asset database's job.
    /// </summary>
    internal sealed class RhiShaderIncludeScanner
    {
        private static readonly Regex IncludePattern = new Regex(
            "^\\s*#\\s*include\\s*[\\\"<]([^\\\">]+)[\\\">]",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private readonly Dictionary<string, string> _text =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Source text, read once per file for the lifetime of an import.</summary>
        internal string ReadAllText(string absolutePath)
        {
            absolutePath = Path.GetFullPath(absolutePath);
            if (!_text.TryGetValue(absolutePath, out string text))
            {
                text = File.ReadAllText(absolutePath);
                _text.Add(absolutePath, text);
            }
            return text;
        }

        /// <summary>
        /// Absolute paths of the source and everything it transitively includes.
        /// Unresolvable includes are skipped rather than reported: system and
        /// compiler-provided headers legitimately resolve nowhere on disk.
        /// </summary>
        internal void CollectDependencies(string sourcePath,
            IReadOnlyList<string> includeDirectories, HashSet<string> into)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            if (!into.Add(sourcePath) || !File.Exists(sourcePath)) return;
            string directory = Path.GetDirectoryName(sourcePath);
            foreach (Match match in IncludePattern.Matches(ReadAllText(sourcePath)))
            {
                string include = match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                string resolved = Resolve(include, directory, includeDirectories);
                if (resolved != null) CollectDependencies(resolved, includeDirectories, into);
            }
        }

        private static string Resolve(string include, string localDirectory,
            IReadOnlyList<string> includeDirectories)
        {
            if (!string.IsNullOrEmpty(localDirectory))
            {
                string local = Path.GetFullPath(Path.Combine(localDirectory, include));
                if (File.Exists(local)) return local;
            }
            for (int i = 0; i < includeDirectories.Count; ++i)
            {
                string candidate = Path.GetFullPath(Path.Combine(includeDirectories[i], include));
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
