using System.IO;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal static class PathTracingInlinePluginInstaller
    {
        private const string PluginName = "GfxPluginIllusionRHI.dll";
        private const string SourceFolder = "Packages/com.kurisu.unityrhi/Plugins/x86_64/";
        private const string InstalledFolder = PathTracingInlineGenerator.OutputFolder + "/Plugins/x86_64/";
        internal const string InstalledPath = InstalledFolder + PluginName;

        private static readonly string[] s_RuntimeFiles = { "D3D12/D3D12Core.dll", "D3D12/d3d12SDKLayers.dll" };

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.delayCall += Install;
        }

        private static void Install()
        {
            string previousPath = InstalledFolder + "GfxPluginUnityRhiBindless.dll";
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
            bool changed = false;
            foreach (string file in s_RuntimeFiles)
                changed |= Copy(file, false);
            changed |= Copy(PluginName, true);
            if (changed)
                Debug.Log($"[IllusionRP] Updated the native plugin files in '{InstalledFolder}'. Restart the Editor so they load before the graphics device.");
        }

        private static bool Copy(string file, bool editor)
        {
            string source = FileUtil.GetPhysicalPath(SourceFolder + file);
            string installed = InstalledFolder + file;
            if (!File.Exists(source))
                return false;
            byte[] bytes = File.ReadAllBytes(source);
            if (File.Exists(installed) && Equal(File.ReadAllBytes(installed), bytes))
                return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(installed));
                File.WriteAllBytes(installed, bytes);
            }
            catch (IOException)
            {
                Debug.LogWarning($"[IllusionRP] Could not update '{installed}' while the Editor uses it; close the Editor and copy it from '{SourceFolder + file}'.");
                return false;
            }
            AssetDatabase.ImportAsset(installed);
            if (AssetImporter.GetAtPath(installed) is PluginImporter importer)
            {
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(editor);
                importer.SetEditorData("CPU", "x86_64");
                importer.SetEditorData("OS", "Windows");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.isPreloaded = editor;
                importer.SaveAndReimport();
            }
            return true;
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
