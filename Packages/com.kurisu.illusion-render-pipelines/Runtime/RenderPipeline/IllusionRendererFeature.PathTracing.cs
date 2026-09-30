using System.Collections.Generic;
using Illusion.Rendering.PathTracing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public partial class IllusionRendererFeature
    {
        private const int RasterSuppressionFrames = 4;

        [SerializeField, Tooltip("Enable path tracing when its Volume is active and the device supports ray tracing.")]
        public bool pathTracing = true;

        private PathTracingPass _pathTracingPass;

        private VolumeStack _pathTracingVolumeStack;

        private readonly Dictionary<Camera, int> _pathTracedCameras = new();

        private readonly Dictionary<Camera, SuppressedCameraState> _suppressedCameras = new();

        private readonly List<Camera> _staleCameras = new();

        private bool _rayReconstructionReported;

        private struct SuppressedCameraState
        {
            public int CullingMask;
            public DepthTextureMode DepthTextureMode;
            public AntialiasingMode Antialiasing;
            public bool RenderShadows;
            public DepthOfField DepthOfField;
            public DepthOfFieldMode DepthOfFieldMode;
        }

        public bool IsPathTracingAvailable => pathTracing && _pathTracingPass != null;

        internal bool IsPathTracingRayReconstructionAvailable => _pathTracingPass is { IsRayReconstructionAvailable: true };

        private void CreatePathTracingPass()
        {
            DisposePathTracing();
            var resources = _renderPipelineResources;
            if (!pathTracing || !PathTracingPass.IsSupported
                || !resources.pathTracingRS || !resources.pathTracingAccumulationCS || !resources.pathTracingOutputShader
                || !resources.pathTracingLightsBakerCS || !resources.pathTracingEnvironmentCS || !resources.pathTracingEnvironmentLightingShader || !resources.pathTracingEmissiveCS
                || !resources.pathTracingRectangleLightShader || !resources.pathTracingBuildRS || !resources.pathTracingFillRS || !resources.pathTracingRealtimeCS || !resources.pathTracingMotionCS)
                return;
            _pathTracingPass = new PathTracingPass(resources, _rendererData);
            RenderPipelineManager.beginContextRendering += RestoreStaleCameras;
            RenderPipelineManager.beginCameraRendering += SuppressRasterForPathTracing;
            RenderPipelineManager.endCameraRendering += RestoreRasterAfterPathTracing;
        }

        private void DisposePathTracing()
        {
            if (_pathTracingPass == null)
                return;
            RenderPipelineManager.beginContextRendering -= RestoreStaleCameras;
            RenderPipelineManager.beginCameraRendering -= SuppressRasterForPathTracing;
            RenderPipelineManager.endCameraRendering -= RestoreRasterAfterPathTracing;
            RestoreSuppressedCameras();
            _pathTracedCameras.Clear();
            _pathTracingVolumeStack?.Dispose();
            _pathTracingVolumeStack = null;
            SafeDispose(ref _pathTracingPass);
        }

        private bool TryEnqueuePathTracing(ScriptableRenderer renderer, ref RenderingData renderingData,
            IllusionRuntimeRenderingConfig config, bool useConvolutionBloom, bool isPostProcessEnabled)
        {
            var camera = renderingData.cameraData.camera;
            if (!IsPathTracingActive(camera, config, VolumeManager.instance.stack))
                return false;

            _pathTracedCameras[camera] = PathTracingFrame.Index;
            if (!_suppressedCameras.TryGetValue(camera, out var state))
                return false;

            var lens = PathTracingLens.From(camera, isPostProcessEnabled, VolumeManager.instance.stack);
            _pathTracingPass.Lens = lens;
            if (lens.IsThinLens && !state.DepthOfField)
            {
                state.DepthOfField = VolumeManager.instance.stack.GetComponent<DepthOfField>();
                state.DepthOfFieldMode = state.DepthOfField.mode.value;
                state.DepthOfField.mode.value = DepthOfFieldMode.Off;
                _suppressedCameras[camera] = state;
            }

            renderer.EnqueuePass(_setupPass);
            _wetSurfaceResetPass.SetEnabled(false);
            renderer.EnqueuePass(_wetSurfaceResetPass);
            renderer.EnqueuePass(_advancedTonemappingPass);
            renderer.EnqueuePass(_pathTracingPass);
            if (useConvolutionBloom)
                renderer.EnqueuePass(_convolutionBloomPass);
            renderer.EnqueuePass(_exposurePass);
            renderer.EnqueuePass(_processingPostPass);
            EnqueueDLSSNeuralRendering(renderer, ref renderingData, config, isPostProcessEnabled);
            return true;
        }

        private bool IsPathTracingActive(Camera camera, IllusionRuntimeRenderingConfig config, VolumeStack stack)
        {
            if (_pathTracingPass == null || !isActive || !config.EnablePathTracing || !IsPathTracingCamera(camera))
                return false;
            var volume = stack.GetComponent<PathTracing.PathTracing>();
            if (volume == null || !volume.IsActive())
                return false;
            if (PathTracingPass.UsesRealtime(camera, volume) && volume.rayReconstruction.value && !_pathTracingPass.IsRayReconstructionAvailable)
            {
                if (!_rayReconstructionReported)
                {
                    _rayReconstructionReported = true;
                    Debug.LogWarning("[PathTracing] Realtime mode needs DLSS Ray Reconstruction, which is not available (UnityRHI package, Direct3D 12 and an NVIDIA RTX GPU are required); the camera stays raster. Turn Ray Reconstruction off in the Path Tracing Volume to see the raw Realtime frame.");
                }
                return false;
            }
            return true;
        }

        private static bool IsPathTracingCamera(Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
                return true;
            if (camera.cameraType != CameraType.Game)
                return false;
            if (camera.targetTexture && camera.targetTexture.format == RenderTextureFormat.Depth)
                return false;
            return !camera.TryGetComponent(out UniversalAdditionalCameraData cameraData) || cameraData.renderType == CameraRenderType.Base;
        }

        private void SuppressRasterForPathTracing(ScriptableRenderContext context, Camera camera)
        {
            if (!_pathTracedCameras.TryGetValue(camera, out int lastFrame) || PathTracingFrame.Index - lastFrame > RasterSuppressionFrames)
                return;

            _pathTracingVolumeStack ??= VolumeManager.instance.CreateStack();
            LayerMask layerMask = 1;
            Transform trigger = camera.transform;
            if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                layerMask = cameraData.volumeLayerMask;
                if (cameraData.volumeTrigger)
                    trigger = cameraData.volumeTrigger;
            }
            VolumeManager.instance.Update(_pathTracingVolumeStack, trigger, layerMask);
            if (!IsPathTracingActive(camera, IllusionRuntimeRenderingConfig.Get(), _pathTracingVolumeStack))
                return;

            var state = new SuppressedCameraState { CullingMask = camera.cullingMask, DepthTextureMode = camera.depthTextureMode };
            camera.cullingMask = 0;
            if (PathTracingPass.UsesRealtime(camera, _pathTracingVolumeStack.GetComponent<PathTracing.PathTracing>()))
                camera.depthTextureMode |= DepthTextureMode.MotionVectors;
            if (cameraData)
            {
                state.Antialiasing = cameraData.antialiasing;
                state.RenderShadows = cameraData.renderShadows;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.renderShadows = false;
            }
            _suppressedCameras[camera] = state;
        }

        private void RestoreRasterAfterPathTracing(ScriptableRenderContext context, Camera camera)
        {
            if (_suppressedCameras.Remove(camera, out var state))
                RestoreCamera(camera, state);
        }

        private void RestoreStaleCameras(ScriptableRenderContext context, List<Camera> cameras)
        {
            RestoreSuppressedCameras();
            _staleCameras.Clear();
            foreach (var pair in _pathTracedCameras)
            {
                if (!pair.Key || PathTracingFrame.Index - pair.Value > RasterSuppressionFrames)
                    _staleCameras.Add(pair.Key);
            }
            foreach (var camera in _staleCameras)
                _pathTracedCameras.Remove(camera);
            _pathTracingPass.ReleaseUnused();
        }

        private void RestoreSuppressedCameras()
        {
            foreach (var pair in _suppressedCameras)
            {
                if (pair.Key)
                    RestoreCamera(pair.Key, pair.Value);
            }
            _suppressedCameras.Clear();
        }

        private static void RestoreCamera(Camera camera, in SuppressedCameraState state)
        {
            camera.cullingMask = state.CullingMask;
            camera.depthTextureMode = state.DepthTextureMode;
            if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                cameraData.antialiasing = state.Antialiasing;
                cameraData.renderShadows = state.RenderShadows;
            }
            if (state.DepthOfField)
                state.DepthOfField.mode.value = state.DepthOfFieldMode;
        }
    }
}
