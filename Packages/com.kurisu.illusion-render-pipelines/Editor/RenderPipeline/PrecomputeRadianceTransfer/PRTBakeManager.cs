using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Illusion.Rendering.Editor
{
    public class PRTBakeManager : PRTVolumeManager
    {
        private static CancellationTokenSource _cancellation;
        public static async void GenerateLighting() => await RunBake(async token =>
        {
            if (ProbeVolume) await BakeVolume(ProbeVolume, token);
            foreach (ReflectionProbeAdditionalData probe in ReflectionProbeAdditionalData)
                if (probe) await BakeReflection(probe, token);
        }, "PRT Lighting");
        public static async void BakeReflectionProbe(ReflectionProbeAdditionalData probe) =>
            await RunBake(token => BakeReflection(probe, token), "PRT Reflection");
        public static async void BakeAllReflectionProbes() => await RunBake(async token =>
        {
            foreach (ReflectionProbeAdditionalData probe in ReflectionProbeAdditionalData)
                if (probe) await BakeReflection(probe, token);
        }, "PRT Reflections");
        internal static async void BakePlacementPreview(PRTProbeVolume volume) => await RunBake(async token =>
        {
            using var baker = new PRTBaker(volume.bakeResolution);
            await baker.BakePlacementPreview(volume, token);
            SceneView.RepaintAll();
        }, "PRT Probe Placement");
        private static async Task RunBake(Func<CancellationToken, Task> action, string label)
        {
            if (IsBaking) return;
            _cancellation = new CancellationTokenSource();
            IsBaking = true;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await action(_cancellation.Token);
                Debug.Log($"[{label}] Completed in {stopwatch.Elapsed.TotalSeconds:F3}s.");
            }
            catch (OperationCanceledException) { Debug.Log($"[{label}] Cancelled; incomplete transport was not saved."); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally
            {
                _cancellation.Dispose();
                _cancellation = null;
                IsBaking = false;
            }
        }
        public static void StopBaking() => _cancellation?.Cancel();
        private static async Task BakeVolume(PRTProbeVolume volume, CancellationToken token)
        {
            if (!volume.asset && !InitializeProbeVolumeData(volume)) return;
            int progress = Progress.Start($"Bake PRT Volume ({volume.name})", options: Progress.Options.Managed);
            using var baker = new PRTBaker(volume.bakeResolution);
            baker.OnProgressUpdate = (status, value) => Progress.Report(progress, value, status);
            try
            {
                await baker.BakeVolume(volume, token);
                EditorUtility.SetDirty(volume.asset);
                AssetDatabase.SaveAssetIfDirty(volume.asset);
                volume.ReloadBakedData();
            }
            finally { Progress.Remove(progress); }
        }
        private static async Task BakeReflection(ReflectionProbeAdditionalData probe, CancellationToken token)
        {
            if (!probe) return;
            int progress = Progress.Start($"Bake Reflection Normalization ({probe.name})", options: Progress.Options.Managed);
            using var baker = new PRTBaker(PRTBakeResolution._512);
            try
            {
                token.ThrowIfCancellationRequested();
                await baker.BakeReflectionProbe(probe, token);
                EditorUtility.SetDirty(probe);
            }
            finally { Progress.Remove(progress); }
        }
        public static void ClearBakedData()
        {
            if (IsBaking) return;
            if (ProbeVolume && ProbeVolume.asset)
            {
                ProbeVolume.ClearBakedData();
                EditorUtility.SetDirty(ProbeVolume.asset);
            }
            foreach (ReflectionProbeAdditionalData probe in ReflectionProbeAdditionalData)
            {
                if (!probe) continue;
                probe.ClearSHCoefficients();
                EditorUtility.SetDirty(probe);
            }
        }
        private static bool InitializeProbeVolumeData(PRTProbeVolume volume)
        {
            string scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath)) { Debug.LogError("Save the scene before baking PRT transport."); return false; }
            string folder = Path.Combine(Path.GetDirectoryName(scenePath), Path.GetFileNameWithoutExtension(scenePath)).Replace('\\', '/');
            if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); AssetDatabase.Refresh(); }
            var asset = ScriptableObject.CreateInstance<PRTProbeVolumeAsset>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Path.GetFileNameWithoutExtension(scenePath)}_ProbeVolume.asset");
            AssetDatabase.CreateAsset(asset, path);
            volume.asset = asset;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return true;
        }
    }
}
