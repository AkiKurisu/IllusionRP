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
        private const int ReflectionCubemapSize = 512;
        private readonly float _sceneTime;
        private readonly RayTracingShader _traceShader;
        private readonly ComputeShader _reflectionProbeSampleCS;
        private readonly int _reflectionKernel;
        private PRTBakeScene _scene;
        private PRTBakeTracer _tracer;
        private Camera _camera;
        private RenderTexture _lighting;
        private ComputeBuffer _directions;
        private static readonly ProfilerMarker SceneMarker = new("PRT Bake Scene Snapshot");
        public Action<string, float> OnProgressUpdate;
        public Bounds GeometryBounds => _scene.bounds;
        public Hash128 GeometrySignature => _scene.geometrySignature;
        public Hash128 MaterialSignature => _scene.materialSignature;
        public string BackendName => "RayTracedCapture";
        public float SceneTime => _sceneTime;

        public PRTBaker()
        {
            _sceneTime = Shader.GetGlobalVector("_Time").y;
            var resources = Resources.Load<IllusionRenderPipelineResources>(nameof(IllusionRenderPipelineResources));
            _traceShader = resources.prtBakeTraceRS;
            _reflectionProbeSampleCS = resources.reflectionProbeSampleCS;
            EnsureCompiled(_reflectionProbeSampleCS);
            _reflectionKernel = _reflectionProbeSampleCS.FindKernel("CSMain");
        }

        void IPRTBaker.UpdateProgress(string status, float progress) => OnProgressUpdate?.Invoke(status, progress);

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
            _tracer = new PRTBakeTracer(_traceShader, _scene.instances, _sceneTime);
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
            using var probes = Upload(positions.Select(p => (Vector4)p).ToArray(), 16);
            using var directions = Upload(samples, 16);
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, samples.Length * positions.Length, PRTCaptureSample.Stride);
            using (var cmd = new CommandBuffer { name = "PRT Bake Capture" })
            {
                _tracer.Capture(cmd, probes, positions.Length, directions, samples.Length, output);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            PRTCaptureSample[] batch = await Readback<PRTCaptureSample>(output);
            token.ThrowIfCancellationRequested();
            var result = new PRTProbeBakeSamples[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var data = new PRTCaptureSample[samples.Length];
                Array.Copy(batch, i * samples.Length, data, 0, samples.Length);
                result[i] = new PRTProbeBakeSamples(positions[i], data);
            }
            return result;
        }

        async Task<PRTProbePlacement[]> IPRTBaker.PlaceProbesAsync(Vector3[] positions, Vector2[] biases, float searchDistance,
            CancellationToken token)
        {
            using var probes = Upload(positions.Select(p => (Vector4)p).ToArray(), 16);
            using var bias = Upload(biases, 8);
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, positions.Length, 16);
            using (var cmd = new CommandBuffer { name = "PRT Bake Placement" })
            {
                _tracer.Place(cmd, probes, bias, positions.Length, searchDistance, output);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Vector4[] placements = await Readback<Vector4>(output);
            token.ThrowIfCancellationRequested();
            return placements.Select(p => new PRTProbePlacement(p, p.w > 0.5f)).ToArray();
        }

        private static GraphicsBuffer Upload<T>(T[] data, int stride) where T : struct
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, data.Length), stride);
            buffer.SetData(data);
            return buffer;
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
        private void UploadDirections(Vector4[] samples)
        {
            if (_directions == null || _directions.count != samples.Length)
            {
                _directions?.Release();
                _directions = new ComputeBuffer(samples.Length, 16);
            }
            _directions.SetData(samples);
        }

        public async Task BakeReflectionProbe(ReflectionProbeAdditionalData probe, CancellationToken token = default)
        {
            EnsureCamera();
            if (!_lighting)
            {
                _lighting = new RenderTexture(ReflectionCubemapSize, ReflectionCubemapSize, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { dimension = TextureDimension.Cube, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "PRT Reflection Reference Radiance" };
                _lighting.Create();
            }
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
            float[] values = await Readback<float>(AsyncGPUReadback.Request(coefficients));
            token.ThrowIfCancellationRequested();
            probe.SetSHCoefficients(values);
        }

        private static Task<T[]> Readback<T>(GraphicsBuffer buffer) where T : struct => Readback<T>(AsyncGPUReadback.Request(buffer));

        private static async Task<T[]> Readback<T>(AsyncGPUReadbackRequest readback) where T : struct
        {
            var completion = new TaskCompletionSource<T[]>();
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
            _tracer?.Dispose();
            _tracer = null;
            _scene?.Dispose();
            _scene = null;
            _directions?.Release();
            _directions = null;
            if (_lighting) { _lighting.Release(); UObject.DestroyImmediate(_lighting); }
            _lighting = null;
            if (_camera) UObject.DestroyImmediate(_camera.gameObject);
            _camera = null;
            OnProgressUpdate = null;
        }
    }
}
