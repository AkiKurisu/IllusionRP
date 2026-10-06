using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Illusion.Rendering.PRTGI;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.Editor
{
    public sealed class PRTBaker : IPRTBaker, IDisposable
    {
        private readonly int _cubemapSize;
        private readonly float _sceneTime;
        private readonly ComputeShader _surfelSampleCS, _reflectionProbeSampleCS;
        private readonly int _surfelKernel, _reflectionKernel;
        private PRTBakeScene _scene;
        private Camera _camera;
        private RenderTexture _position, _normal, _albedo, _metadata, _lighting;
        private ComputeBuffer _directions;
        private const string CaptureKeyword = "_PRT_CAPTURE";
        private static readonly int CaptureModeId = Shader.PropertyToID("_PRTCaptureMode");
        private static readonly ProfilerMarker SceneMarker = new("PRT Bake Scene Snapshot");
        private static readonly ProfilerMarker CaptureMarker = new("PRT Bake GBuffer Capture");
        public Action<string, float> OnProgressUpdate;
        public Bounds GeometryBounds => _scene.bounds;
        public Hash128 GeometrySignature => _scene.geometrySignature;
        public Hash128 MaterialSignature => _scene.materialSignature;
        public string BackendName => "RasterDiffuseCapture";
        public float SceneTime => _sceneTime;

        public PRTBaker(PRTBakeResolution resolution)
        {
            _cubemapSize = (int)resolution;
            _sceneTime = Shader.GetGlobalVector("_Time").y;
            var resources = Resources.Load<IllusionRenderPipelineResources>(nameof(IllusionRenderPipelineResources));
            _surfelSampleCS = resources.prtSurfelSampleCS;
            _reflectionProbeSampleCS = resources.reflectionProbeSampleCS;
            EnsureCompiled(_surfelSampleCS);
            EnsureCompiled(_reflectionProbeSampleCS);
            _surfelKernel = _surfelSampleCS.FindKernel("CSMain");
            _reflectionKernel = _reflectionProbeSampleCS.FindKernel("CSMain");
        }

        void IPRTBaker.UpdateProgress(string status, float progress) => OnProgressUpdate?.Invoke(status, progress);
        PRTProbePlacement IPRTBaker.PlaceProbe(Vector3 position, float geometryBias, float rayOriginBias, float searchDistance) =>
            _scene.placement.Place(position, geometryBias, rayOriginBias, searchDistance);

        private void PrepareScene()
        {
            if (_scene != null) return;
            using var scope = SceneMarker.Auto();
            Renderer[] renderers = UObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0)
                .ToArray();
            renderers = SelectHighestDetailLodRenderers(renderers, UObject.FindObjectsByType<LODGroup>(FindObjectsSortMode.None));
            _scene = new PRTBakeScene(renderers);
            if (_scene.emptyGeometry.Length > 0)
                Debug.Log($"[PRT Capture] Empty geometry: {string.Join("; ", _scene.emptyGeometry)}.");
        }

        public async Task BakeVolume(PRTProbeVolume volume, CancellationToken token = default)
        {
            PrepareScene();
            await volume.BakeDataAsync(this, token);
        }
        internal async Task BakePlacementPreview(PRTProbeVolume volume, CancellationToken token)
        {
            PrepareScene();
            await volume.BakePlacementAsync(this, token);
        }

        async Task<PRTProbeBakeSamples[]> IPRTBaker.CaptureProbesAsync(Vector3[] positions, Vector4[] samples, CancellationToken token)
        {
            EnsureCaptureResources();
            UploadDirections(samples);
            var result = new PRTProbeBakeSamples[positions.Length];
            using var buffer = new ComputeBuffer(samples.Length * positions.Length, PRTCaptureSample.Stride);
            for (int i = 0; i < positions.Length; i++)
            {
                if (token.IsCancellationRequested) break;
                Capture(positions[i]);
                _surfelSampleCS.SetVector("_probePos", positions[i]);
                _surfelSampleCS.SetInt("_sampleCount", samples.Length);
                _surfelSampleCS.SetInt("_surfelOutputOffset", i * samples.Length);
                _surfelSampleCS.SetBuffer(_surfelKernel, "_sampleDirections", _directions);
                _surfelSampleCS.SetTexture(_surfelKernel, "_worldPosCubemap", _position);
                _surfelSampleCS.SetTexture(_surfelKernel, "_normalCubemap", _normal);
                _surfelSampleCS.SetTexture(_surfelKernel, "_albedoCubemap", _albedo);
                _surfelSampleCS.SetTexture(_surfelKernel, "_metadataCubemap", _metadata);
                _surfelSampleCS.SetBuffer(_surfelKernel, "_surfels", buffer);
                _surfelSampleCS.Dispatch(_surfelKernel, (samples.Length + 63) / 64, 1, 1);
            }
            PRTCaptureSample[] batch = await Readback<PRTCaptureSample>(buffer);
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < positions.Length; i++)
            {
                var data = new PRTCaptureSample[samples.Length];
                Array.Copy(batch, i * samples.Length, data, 0, samples.Length);
                result[i] = new PRTProbeBakeSamples(positions[i], data);
            }
            return result;
        }

        private void EnsureCaptureResources()
        {
            EnsureCamera();
            if (_position) return;
            _position = CreateCube(RenderTextureFormat.ARGBFloat, "PRT Capture Position");
            _normal = CreateCube(RenderTextureFormat.ARGBFloat, "PRT Capture Normal");
            _albedo = CreateCube(RenderTextureFormat.ARGBFloat, "PRT Capture Diffuse");
            _metadata = CreateCube(RenderTextureFormat.ARGBFloat, "PRT Capture Metadata");
        }
        private void EnsureCamera()
        {
            if (_camera) return;
            var go = new GameObject("PRT Bake Camera") { hideFlags = HideFlags.HideAndDontSave };
            _camera = go.AddComponent<Camera>();
            _camera.cameraType = CameraType.Reflection;
            _camera.enabled = false;
            _camera.allowMSAA = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.nearClipPlane = 0.001f;
        }
        private RenderTexture CreateCube(RenderTextureFormat format, string name)
        {
            var target = new RenderTexture(_cubemapSize, _cubemapSize, 24, format, RenderTextureReadWrite.Linear)
            { dimension = TextureDimension.Cube, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            target.Create();
            return target;
        }
        private void UploadDirections(Vector4[] samples)
        {
            if (_directions == null || _directions.count != samples.Length)
            {
                _directions?.Release();
                _directions = new ComputeBuffer(samples.Length, 16);
            }
            _directions.SetData(samples);
        }
        private void Capture(Vector3 position)
        {
            using var scope = CaptureMarker.Auto();
            _camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            _camera.farClipPlane = Mathf.Max(1f, Vector3.Distance(position, _scene.bounds.center) + _scene.bounds.extents.magnitude + 1f);
            _camera.cullingMask = 0;
            bool originalKeyword = Shader.IsKeywordEnabled(CaptureKeyword);
            int originalMode = Shader.GetGlobalInteger(CaptureModeId);
            try
            {
                Shader.EnableKeyword(CaptureKeyword);
                using (PRTGBufferCaptureBridge.Begin(_camera, _scene.drawItems, _sceneTime))
                {
                    CaptureMode(0, _position);
                    CaptureMode(1, _normal);
                    CaptureMode(2, _albedo);
                    CaptureMode(3, _metadata);
                }
            }
            finally
            {
                if (originalKeyword) Shader.EnableKeyword(CaptureKeyword); else Shader.DisableKeyword(CaptureKeyword);
                Shader.SetGlobalInteger(CaptureModeId, originalMode);
            }
        }
        private void CaptureMode(int mode, RenderTexture target)
        {
            Shader.SetGlobalInteger(CaptureModeId, mode);
            if (!_camera.RenderToCubemap(target, -1, StaticEditorFlags.ContributeGI))
                throw new InvalidOperationException("PRT cubemap capture failed.");
        }

        public async Task BakeReflectionProbe(ReflectionProbeAdditionalData probe, CancellationToken token = default)
        {
            EnsureCamera();
            _lighting ??= CreateCube(RenderTextureFormat.ARGBFloat, "PRT Reflection Reference Radiance");
            UploadDirections(PRTBakeSampling.GenerateDirections(512, 0));
            _camera.cullingMask = -1;
            _camera.farClipPlane = 10000f;
            _camera.transform.position = probe.transform.position;
            if (!_camera.RenderToCubemap(_lighting)) throw new InvalidOperationException("Reflection lighting capture failed.");
            using var coefficients = new ComputeBuffer(27, sizeof(float));
            _reflectionProbeSampleCS.SetInt("_sampleCount", 512);
            _reflectionProbeSampleCS.SetTexture(_reflectionKernel, "_inputCubemap", _lighting);
            _reflectionProbeSampleCS.SetBuffer(_reflectionKernel, "_sampleDirections", _directions);
            _reflectionProbeSampleCS.SetBuffer(_reflectionKernel, "_coefficientSH9", coefficients);
            _reflectionProbeSampleCS.Dispatch(_reflectionKernel, 1, 1, 1);
            float[] values = await Readback<float>(coefficients);
            token.ThrowIfCancellationRequested();
            probe.SetSHCoefficients(values);
        }

        private static async Task<T[]> Readback<T>(ComputeBuffer buffer) where T : struct
        {
            var completion = new TaskCompletionSource<T[]>();
            AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(buffer);
            readback.forcePlayerLoopUpdate = true;
            GL.Flush();
            EditorApplication.CallbackFunction poll = () =>
            {
                EditorApplication.QueuePlayerLoopUpdate();
                if (completion.Task.IsCompleted) return;
                readback.Update();
                if (!readback.done) return;
                if (readback.hasError) completion.TrySetException(new InvalidOperationException("PRT GPU readback failed."));
                else completion.TrySetResult(readback.GetData<T>().ToArray());
            };
            EditorApplication.update += poll;
            try
            {
                poll();
                return await completion.Task;
            }
            finally { EditorApplication.update -= poll; }
        }
        private static void EnsureCompiled(ComputeShader shader)
        {
            if (!shader) throw new InvalidOperationException("PRT bake compute shader is unavailable.");
            ShaderMessage[] errors = ShaderUtil.GetComputeShaderMessages(shader)
                .Where(message => message.severity == ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException($"PRT bake compute '{shader.name}' failed to compile: " +
                string.Join("; ", errors.Select(message => message.file + ":" + message.line + " " + message.message)));
        }
        internal static Renderer[] SelectHighestDetailLodRenderers(Renderer[] renderers, LODGroup[] groups)
        {
            var all = new HashSet<Renderer>();
            var first = new HashSet<Renderer>();
            foreach (LODGroup group in groups)
            {
                if (!group) continue;
                LOD[] lods = group.GetLODs();
                foreach (LOD lod in lods) foreach (Renderer r in lod.renderers) if (r) all.Add(r);
                if (lods.Length > 0) foreach (Renderer r in lods[0].renderers) if (r) first.Add(r);
            }
            return renderers.Where(r => r && (!all.Contains(r) || first.Contains(r))).ToArray();
        }
        public void Dispose()
        {
            _scene?.Dispose();
            _scene = null;
            _directions?.Release();
            _directions = null;
            foreach (RenderTexture target in new[] { _position, _normal, _albedo, _metadata, _lighting })
                if (target) { target.Release(); UObject.DestroyImmediate(target); }
            _position = _normal = _albedo = _metadata = _lighting = null;
            if (_camera) UObject.DestroyImmediate(_camera.gameObject);
            _camera = null;
            OnProgressUpdate = null;
        }
    }
}
