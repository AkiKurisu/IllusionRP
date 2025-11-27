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
        private static CancellationTokenSource _cancellationTokenSource;
        
        public static float Progress { get; private set; }

        /// <summary>
        /// Bake all probe volume and reflection normalization data in current scene
        /// </summary>
        public static async void GenerateLighting()
        {
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                await BakeProbeVolume_Internal(ProbeVolume);
                foreach (var probe in ReflectionProbeAdditionalData)
                {
                    await BakeReflectionProbe_Internal(probe);
                }
                totalStopwatch.Stop();
                Debug.Log($"[PRTBaker] Bake completed, total time: {FormatElapsed(totalStopwatch.Elapsed)}");
            }
            catch
            {
                totalStopwatch.Stop();
                Debug.LogError($"[PRTBaker] Bake failed, total time: {FormatElapsed(totalStopwatch.Elapsed)}");
            }
        }

        /// <summary>
        /// Clear all baked data in current scene
        /// </summary>
        public static void ClearBakedData()
        {
            ProbeVolume.ClearBakedData();
            EditorUtility.SetDirty(ProbeVolume.asset);
            foreach (var probe in ReflectionProbeAdditionalData)
            {
                probe.ClearSHCoefficients();
                EditorUtility.SetDirty(probe);
            }
            Debug.Log($"[PRTBaker] Clear baked data completed.");
        }
        
        /// <summary>
        /// Bake single reflection probe normalization data
        /// </summary>
        /// <param name="reflectionProbe"></param>
        public static async void BakeReflectionProbe(ReflectionProbeAdditionalData reflectionProbe)
        {
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                await BakeReflectionProbe_Internal(reflectionProbe);
                totalStopwatch.Stop();
                Debug.Log($"[PRTBaker] Bake completed, total time: {FormatElapsed(totalStopwatch.Elapsed)}");
            }
            catch
            {
                totalStopwatch.Stop();
                Debug.LogError($"[PRTBaker] Bake failed, total time: {FormatElapsed(totalStopwatch.Elapsed)}");
            }
        }
        
        /// <summary>
        /// Bake all reflection probes normalization data
        /// </summary>
        public static async void BakeAllReflectionProbes()
        {
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                foreach (var probe in ReflectionProbeAdditionalData)
                {
                    await BakeReflectionProbe_Internal(probe);
                }

                totalStopwatch.Stop();
                Debug.Log($"[PRTBaker] Bake completed, total time: {FormatElapsed(totalStopwatch.Elapsed)}");
            }
            catch
            {
                totalStopwatch.Stop();
                Debug.LogError($"[PRTBaker] Bake failed, total time: {FormatElapsed(totalStopwatch.Elapsed)}");
            }
        }
        
        /// <summary>
        /// Stop all baking tasks in the scene
        /// </summary>
        public static void StopBaking()
        {
            if (!IsBaking) return;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = null;
        }
        
        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1)
            {
                // mm min ss.sss s
                return $"{(int)elapsed.TotalMinutes:D2}m {elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}s";
            }

            if (elapsed.TotalSeconds >= 1)
            {
                // ss.sss s
                return $"{elapsed.TotalSeconds:F3}s";
            }

            // xxx.xxx ms
            return $"{elapsed.TotalMilliseconds:F3}ms";
        }

        private static async Task BakeProbeVolume_Internal(PRTProbeVolume prtProbeVolume)
        {
            if (!prtProbeVolume.asset)
            {
                if (!InitializeProbeVolumeData(prtProbeVolume))
                {
                    return;
                }
            }
            
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            IsBaking = true;
            using var prtBaker = new PRTBaker(prtProbeVolume.bakeResolution);

            Progress = 0;
            // Setup progress callbacks
            prtBaker.OnProgressUpdate = (status, progress) =>
            {
                Progress = progress;
                EditorUtility.DisplayProgressBar($"Bake Probe Volume ({prtProbeVolume.name})", status, progress);
            };

            try
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    await prtBaker.BakeVolume(prtProbeVolume, _cancellationTokenSource.Token);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                EditorUtility.SetDirty(prtProbeVolume.asset);
                AssetDatabase.SaveAssets();
                prtProbeVolume.ReloadBakedData();
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Baking was cancelled by user.");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                IsBaking = false;
                EditorUtility.ClearProgressBar();
            }
        }
        
        private static async Task BakeReflectionProbe_Internal(ReflectionProbeAdditionalData reflectionProbe)
        {
            IsBaking = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            using var prtBaker = new PRTBaker(PRTBakeResolution._512);

            // Setup progress callbacks
            prtBaker.OnProgressUpdate = (status, progress) =>
            {
                EditorUtility.DisplayProgressBar($"Bake Reflection Probe ({reflectionProbe.name})", status, progress);
            };

            try
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    await Task.Delay(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token);
                    prtBaker.BakeReflectionProbe(reflectionProbe);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                EditorUtility.SetDirty(reflectionProbe);
                AssetDatabase.SaveAssets();
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Baking was cancelled by user.");
            }
            finally
            {
                Progress = 0;
                IsBaking = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool InitializeProbeVolumeData(PRTProbeVolume prtProbeVolume)
        {
            string scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogError("Please save your scene before baking.");
                return false;
            }
                
            string sceneDir = Path.GetDirectoryName(scenePath);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                
            string targetDir = Path.Combine(sceneDir!, sceneName);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                AssetDatabase.Refresh();
            }


            var asset = ScriptableObject.CreateInstance<PRTProbeVolumeAsset>();
            string assetPath = Path.Combine(targetDir, $"{sceneName}_ProbeVolume.asset");
            assetPath = assetPath.Replace("\\", "/"); 
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            prtProbeVolume.asset = asset;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return true;
        }
    }
}