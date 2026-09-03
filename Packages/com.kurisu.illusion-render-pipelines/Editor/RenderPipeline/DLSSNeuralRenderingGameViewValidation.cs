using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    /// <summary>Automated non-Play-Mode Game View A/B validation for the standalone project.</summary>
    public static class DLSSNeuralRenderingGameViewValidation
    {
        private static EditorWindow _gameView;
        private static DLSSNeuralRendering _volume;
        private static IllusionRuntimeRenderingConfig _config;
        private static bool _oldEnabled;
        private static DLSSNeuralRenderingDebugMode _oldDebugMode;
        private static float _oldDebugMotionRange;
        private static float _oldDebugDepthRange;
        private static double _startedAt;
        private static double _stageStartedAt;
        private static int _stage;

        public static void RunGameViewAb()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Game View validation requires a regular Editor outside Play Mode.");

            EditorSceneManager.OpenScene("Assets/Scenes/Sponza.unity", OpenSceneMode.Single);
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                "Assets/Scenes/Sponza/Sponza.asset");
            if (profile == null || !profile.TryGet(out _volume))
                throw new InvalidOperationException("The Demo DLSS Neural Rendering Volume override is missing.");

            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor", true);
            _gameView = EditorWindow.GetWindow(gameViewType);
            _gameView.Show();
            _gameView.Focus();
            _config = IllusionRuntimeRenderingConfig.Get();
            _oldEnabled = _volume.enable.value;
            _oldDebugMode = _config.DLSSNeuralRenderingDebugMode;
            _oldDebugMotionRange = _config.DLSSNeuralRenderingDebugMotionRange;
            _oldDebugDepthRange = _config.DLSSNeuralRenderingDebugDepthRange;
            _volume.enable.overrideState = true;
            _volume.enable.value = false;
            _config.DLSSNeuralRenderingDebugMode = DLSSNeuralRenderingDebugMode.Off;
            _config.DLSSNeuralRenderingDebugMotionRange = 32f;
            _config.DLSSNeuralRenderingDebugDepthRange = 100f;
            _startedAt = EditorApplication.timeSinceStartup;
            _stageStartedAt = _startedAt;
            _stage = 0;
            Directory.CreateDirectory(Path.GetFullPath("Snapshots"));
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            Debug.Log("[IllusionRP.DLSS Neural Rendering.GameView] Started non-Play-Mode A/B validation.");
        }

        private static void Update()
        {
            _gameView?.Repaint();
            double elapsed = EditorApplication.timeSinceStartup - _startedAt;
            try
            {
                if (_stage == 0 && elapsed >= 8d)
                {
                    CaptureGameView("Snapshots/DLSSNeuralRendering-GameView-Off.png");
                    _volume.enable.value = true;
                    _stage = 1;
                }

                if (_stage == 1)
                {
                    DLSSNeuralRenderingRuntimeStatus status = DLSSNeuralRenderingBackendLoader.GetStatus();
                    if (status.LastCreateResult > 0 && status.LastEvaluateResult > 0)
                    {
                        CaptureGameView("Snapshots/DLSSNeuralRendering-GameView-On.png");
                        Debug.Log($"[IllusionRP.DLSS Neural Rendering.GameView] playMode={EditorApplication.isPlaying}, " +
                                  $"available={status.RuntimeAvailable}, " +
                                  $"create=0x{unchecked((uint)status.LastCreateResult):X8}, " +
                                  $"evaluate=0x{unchecked((uint)status.LastEvaluateResult):X8}");
                        _config.DLSSNeuralRenderingDebugMode = DLSSNeuralRenderingDebugMode.Color;
                        _stage = 2;
                        _stageStartedAt = EditorApplication.timeSinceStartup;
                    }
                    else if (elapsed >= 30d)
                    {
                        throw new TimeoutException(
                            $"Game View did not evaluate DLSS Neural Rendering (create=0x{unchecked((uint)status.LastCreateResult):X8}, " +
                            $"evaluate=0x{unchecked((uint)status.LastEvaluateResult):X8}).");
                    }
                }

                if (_stage == 2 && EditorApplication.timeSinceStartup - _stageStartedAt >= 1d)
                {
                    CaptureGameView("Snapshots/DLSSNeuralRendering-Debug-Color.png");
                    _config.DLSSNeuralRenderingDebugMode = DLSSNeuralRenderingDebugMode.MotionVectors;
                    _stage = 3;
                    _stageStartedAt = EditorApplication.timeSinceStartup;
                }
                else if (_stage == 3 && EditorApplication.timeSinceStartup - _stageStartedAt >= 1d)
                {
                    CaptureGameView("Snapshots/DLSSNeuralRendering-Debug-MotionVectors.png");
                    _config.DLSSNeuralRenderingDebugMode = DLSSNeuralRenderingDebugMode.LinearEyeDepth;
                    _stage = 4;
                    _stageStartedAt = EditorApplication.timeSinceStartup;
                }
                else if (_stage == 4 && EditorApplication.timeSinceStartup - _stageStartedAt >= 1d)
                {
                    CaptureGameView("Snapshots/DLSSNeuralRendering-Debug-LinearDepth.png");
                    Debug.Log("[IllusionRP.DLSS Neural Rendering.GameView] Captured Color, MotionVectors and LinearEyeDepth inputs.");
                    Finish(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(2);
            }
        }

        private static void CaptureGameView(string path)
        {
            RenderTexture target = GetGameViewTargetTexture();
            if (target == null || target.width == 0 || target.height == 0)
                throw new InvalidOperationException("The Game View render target is unavailable.");

            RenderTexture oldActive = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(Path.GetFullPath(path), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static RenderTexture GetGameViewTargetTexture()
        {
            for (Type type = _gameView.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty("targetTexture",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.GetValue(_gameView) is RenderTexture target)
                    return target;
                FieldInfo field = type.GetField("m_RenderTexture",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(_gameView) is RenderTexture fieldTarget)
                    return fieldTarget;
            }
            return null;
        }

        private static void Finish(int exitCode)
        {
            EditorApplication.update -= Update;
            if (_volume != null)
                _volume.enable.value = _oldEnabled;
            if (_config != null)
            {
                _config.DLSSNeuralRenderingDebugMode = _oldDebugMode;
                _config.DLSSNeuralRenderingDebugMotionRange = _oldDebugMotionRange;
                _config.DLSSNeuralRenderingDebugDepthRange = _oldDebugDepthRange;
            }
            _volume = null;
            _config = null;
            _gameView = null;
            EditorApplication.Exit(exitCode);
        }
    }
}
