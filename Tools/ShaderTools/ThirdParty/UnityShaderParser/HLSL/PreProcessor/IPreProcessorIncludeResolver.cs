using System.Collections.Generic;
using System.IO;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL.PreProcessor
{
    public interface IPreProcessorIncludeResolver
    {
        // Called when an include is found, updates the current base path and file path/name.
        void EnterFile(ref string basePath, ref string filePath, string includePath);

        // Called when an include is done being processed, updates the current base path and file path/name.
        void ExitFile(ref string basePath, ref string filePath);

        // Called when an include is found, reads the file and returns its content
        string ReadFile(string basePath, string includePath);

    }

    public sealed class DefaultPreProcessorIncludeResolver : IPreProcessorIncludeResolver
    {
        private List<string> includePaths = new List<string>();

        private struct FileState
        {
            public string basePath;
            public string filePath;
        }
        private Stack<FileState> fileStates = new Stack<FileState>();

        public DefaultPreProcessorIncludeResolver() { }

        public DefaultPreProcessorIncludeResolver(List<string> includePaths)
        {
            foreach (var includePath in includePaths)
            {
                if (!Directory.Exists(includePath))
                {
                    this.includePaths.Add(Path.GetFullPath(includePath));
                }
                else
                {
                    this.includePaths.Add(includePath);
                }
            }
        }

        public void EnterFile(ref string basePath, ref string filePath, string includePath)
        {
            fileStates.Push(new FileState
            {
                basePath = basePath,
                filePath = filePath
            });

            string[] pathParts = includePath.Split('/', '\\');
            if (pathParts.Length > 1)
            {
                basePath = Path.Combine(basePath, string.Join("/", pathParts.Take(pathParts.Length - 1)));
            }
            filePath = pathParts.LastOrDefault();
        }

        public void ExitFile(ref string basePath, ref string filePath)
        {
            var state = fileStates.Pop();
            basePath = state.basePath;
            filePath = state.filePath;
        }

        public string ReadFile(string basePath, string filePath)
        {
            // Fix windows-specific include paths
            filePath = filePath.Replace("\\", "/");

            string path = string.IsNullOrEmpty(basePath)
                ? filePath
                : Path.Combine(basePath, filePath);

            // Try include paths instead
            if (!File.Exists(path))
            {
                foreach (string includePath in includePaths)
                {
                    string combinedPath = Path.Combine(includePath, filePath);
                    if (File.Exists(combinedPath))
                        return File.ReadAllText(combinedPath);
                    combinedPath = Path.Combine(basePath, includePath, filePath);
                    if (File.Exists(combinedPath))
                        return File.ReadAllText(combinedPath);
                }
            }

            // Still not found, so try current directory instead
            if (!File.Exists(path))
            {
                string lastFolder = Path.GetFileName(basePath);
                if (!string.IsNullOrEmpty(lastFolder) && filePath.StartsWith(lastFolder))
                {
                    path = Path.Combine($"{basePath}/..", filePath);
                }
            }

            return File.ReadAllText(path);
        }
    }
}
