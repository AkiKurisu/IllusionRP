using System.IO;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal static class PathTracingInlinePluginInstaller
    {
        private const string PluginName = "GfxPluginIllusionRHI.dll";
        private const string SourcePath = "Packages/com.kurisu.unityrhi/Plugins/x86_64/" + PluginName;
        internal const string InstalledPath = PathTracingInlineGenerator.OutputFolder + "/Plugins/x86_64/" + PluginName;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.delayCall += Install;
        }

        private static void Install()
        {
            string source = FileUtil.GetPhysicalPath(SourcePath);
            if (!File.Exists(source))
                return;
            string previousPath = Path.GetDirectoryName(InstalledPath) + "/GfxPluginUnityRhiBindless.dll";
            if (AssetImporter.GetAtPath(previousPath) is PluginImporter previous)
            {
                if (previous.GetCompatibleWithEditor() || previous.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))
                {
                    previous.SetCompatibleWithAnyPlatform(false);
                    previous.SetCompatibleWithEditor(false);
                    previous.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, false);
                    previous.SaveAndReimport();
                }
            }
            byte[] bytes = File.ReadAllBytes(source);
            if (File.Exists(InstalledPath) && Equal(File.ReadAllBytes(InstalledPath), bytes))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(InstalledPath));
                File.WriteAllBytes(InstalledPath, bytes);
            }
            catch (IOException)
            {
                Debug.LogWarning($"[IllusionRP] Could not update '{InstalledPath}' while the Editor uses it; close the Editor and copy it from '{SourcePath}'.");
                return;
            }
            AssetDatabase.ImportAsset(InstalledPath);
            if (AssetImporter.GetAtPath(InstalledPath) is PluginImporter importer)
            {
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetEditorData("CPU", "x86_64");
                importer.SetEditorData("OS", "Windows");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.isPreloaded = true;
                importer.SaveAndReimport();
            }
            Debug.Log($"[IllusionRP] Installed '{InstalledPath}'. Restart the Editor so it loads before the graphics device.");
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
