using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEditor.Rendering;
using Random = UnityEngine.Random;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.Editor
{
    public sealed class PRTBaker : IPRTBaker, IDisposable
    {
        /// <summary>
        /// G-buffer capture modes for PRT baking
        /// </summary>
        private enum GBufferCaptureMode
        {
            WorldPosition,
            Normal,
            Albedo
        }

        private const int ThreadX = 32;

        private const int ThreadY = 16;

        private const int RayNum = ThreadX * ThreadY; // 512 per probe

        private const int SurfelByteSize = 3 * 12 + 4; // sizeof(Surfel)

        // Shared render textures for G-buffer capture
        private RenderTexture _worldPosRT;

        private RenderTexture _normalRT;

        private RenderTexture _albedoRT;

        // Baking settings
        private readonly int _cubemapSize;

        // Progress tracking
        public Action<string, float> OnProgressUpdate;

        private bool _isInitialized;

        private bool _disposed;

        private Renderer[] _renderers;

        // Dictionary to store original shaders for restoration
        private readonly Dictionary<Material, Shader> _originalShaders = new();

        private Camera _cubemapCamera;

        private readonly ComputeShader _surfelSampleCS;

        private readonly int _surfelSampleKernel;

        private readonly ComputeShader _reflectionProbeSampleCS;

        private readonly int _reflectionProbeSampleKernel;

        /// <summary>
        /// Initialize the PRTBaker with the specified cubemap size
        /// </summary>
        /// <param name="bakeResolution">Resolution of the cubemap textures</param>
        public PRTBaker(PRTBakeResolution bakeResolution)
        {
            _cubemapSize = (int)bakeResolution;
            InitializeRenderTextures();
            var resources = Resources.Load<IllusionRenderPipelineResources>(nameof(IllusionRenderPipelineResources));
            _surfelSampleCS = resources.prtSurfelSampleCS;
            _surfelSampleKernel = _surfelSampleCS.FindKernel("CSMain");
            _reflectionProbeSampleCS = resources.reflectionProbeSampleCS;
            _reflectionProbeSampleKernel = _reflectionProbeSampleCS.FindKernel("CSMain");
        }

        /// <summary>
        /// Initialize render textures for G-buffer capture
        /// </summary>
        private void InitializeRenderTextures()
        {
            if (_isInitialized) return;

            // Create cubemap render textures
            _worldPosRT = new RenderTexture(_cubemapSize, _cubemapSize, 24, RenderTextureFormat.ARGBFloat)
            {
                dimension = TextureDimension.Cube,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _worldPosRT.Create();

            _normalRT = new RenderTexture(_cubemapSize, _cubemapSize, 24, RenderTextureFormat.ARGBFloat)
            {
                dimension = TextureDimension.Cube,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _normalRT.Create();

            _albedoRT = new RenderTexture(_cubemapSize, _cubemapSize, 24, RenderTextureFormat.ARGB32)
            {
                dimension = TextureDimension.Cube,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _albedoRT.Create();

            _isInitialized = true;
        }

        void IPRTBaker.UpdateProgress(string status, float progress)
        {
            OnProgressUpdate?.Invoke(status, progress);
        }

        /// <summary>
        /// Batch set shader for all game objects in the scene and record original shaders
        /// </summary>
        /// <param name="renderers">Array of renderer to modify</param>
        /// <param name="shader">Shader to apply</param>
        private void RecordAndSetShaders(Renderer[] renderers, Shader shader)
        {
            // Record
            foreach (var renderer in renderers)
            {
                if (renderer && renderer.sharedMaterial)
                {
                    _originalShaders[renderer.sharedMaterial] = renderer.sharedMaterial.shader;
                }
            }

            // Set
            foreach (var renderer in renderers)
            {
                if (renderer && renderer.sharedMaterial)
                {
                    renderer.sharedMaterial.shader = shader;
                }
            }
        }

        /// <summary>
        /// Restore original shaders for all materials
        /// </summary>
        private void RestoreOriginalShaders()
        {
            foreach (var kvp in _originalShaders)
            {
                var material = kvp.Key;
                var originalShader = kvp.Value;

                if (material && originalShader)
                {
                    material.shader = originalShader;
                }
            }

            _originalShaders.Clear();
        }

        /// <summary>
        /// Set global shader keywords for G-buffer capture mode
        /// This is much more efficient than setting keywords per material
        /// </summary>
        /// <param name="captureMode">G-buffer capture mode</param>
        private static void SetGlobalGBufferCaptureMode(GBufferCaptureMode captureMode)
        {
            // Enable the specific keyword based on capture mode
            switch (captureMode)
            {
                case GBufferCaptureMode.WorldPosition:
                    Shader.EnableKeyword("_GBUFFER_WORLDPOS");
                    Shader.DisableKeyword("_GBUFFER_NORMAL");
                    break;
                case GBufferCaptureMode.Normal:
                    Shader.DisableKeyword("_GBUFFER_WORLDPOS");
                    Shader.EnableKeyword("_GBUFFER_NORMAL");
                    break;
                case GBufferCaptureMode.Albedo:
                    Shader.DisableKeyword("_GBUFFER_WORLDPOS");
                    Shader.DisableKeyword("_GBUFFER_NORMAL");
                    break;
            }
        }

        /// <summary>
        /// Clear all global G-buffer keywords
        /// </summary>
        private static void ClearGlobalGBufferKeywords()
        {
            Shader.DisableKeyword("_GBUFFER_WORLDPOS");
            Shader.DisableKeyword("_GBUFFER_NORMAL");
        }

        private static Camera CreateCubemapCamera()
        {
            GameObject cubemapCamera = new GameObject("PRTBaker_CubemapCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Camera camera = cubemapCamera.AddComponent<Camera>();
            camera.cameraType = CameraType.Reflection;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            camera.enabled = false;
            return camera;
        }

        private void CaptureGbufferCubemaps(Vector3 position)
        {
            _cubemapCamera.transform.SetPositionAndRotation(position, Quaternion.identity);

            // Capture GBuffers
            SetGlobalGBufferCaptureMode(GBufferCaptureMode.WorldPosition);
            _cubemapCamera.RenderToCubemap(_worldPosRT, -1, StaticEditorFlags.ContributeGI);
            SetGlobalGBufferCaptureMode(GBufferCaptureMode.Normal);
            _cubemapCamera.RenderToCubemap(_normalRT, -1, StaticEditorFlags.ContributeGI);
            SetGlobalGBufferCaptureMode(GBufferCaptureMode.Albedo);
            _cubemapCamera.RenderToCubemap(_albedoRT, -1, StaticEditorFlags.ContributeGI);

            // Clean up global keywords after capture
            ClearGlobalGBufferKeywords();

            // Force GPU to flush and release temporary resources
            GL.Flush();
        }

        private void CaptureLightingCubemap(Vector3 position)
        {
            _cubemapCamera.transform.SetPositionAndRotation(position, Quaternion.identity);
            _cubemapCamera.RenderToCubemap(_albedoRT);
        }

        /// <summary>
        /// Bake surfel data using PRTBaker
        /// </summary>
        /// <param name="probePosition">Probe position</param>
        public Surfel[] BakeSurfelData(Vector3 probePosition)
        {
            CaptureGbufferCubemaps(probePosition);

            // Sample surfels using PRTBaker's render textures
            return SampleSurfels(_worldPosRT, _normalRT, _albedoRT, probePosition);
        }

        public float[] BakeAdditionalProbeData(Vector3 probePosition)
        {
            CaptureLightingCubemap(probePosition);
            return SampleReflectionProbe(_albedoRT);
        }

        private Surfel[] SampleSurfels(RenderTexture worldPosCubemap, RenderTexture normalCubemap,
            RenderTexture albedoCubemap, Vector3 position)
        {
            var surfels = new ComputeBuffer(RayNum, SurfelByteSize);
            try
            {
                var readBackBuffer = new Surfel[RayNum];

                var cs = _surfelSampleCS;
                var kernel = _surfelSampleKernel;

                // set necessary data and start sample
                cs.SetVector(ShaderProperties.ProbePos, new Vector4(position.x, position.y, position.z, 1.0f));
                cs.SetFloat(ShaderProperties.RandSeed, Random.Range(0.0f, 1.0f));
                cs.SetTexture(kernel, ShaderProperties.WorldPosCubemap, worldPosCubemap);
                cs.SetTexture(kernel, ShaderProperties.NormalCubemap, normalCubemap);
                cs.SetTexture(kernel, ShaderProperties.AlbedoCubemap, albedoCubemap);
                cs.SetBuffer(kernel, ShaderProperties.Surfels, surfels);

                // start CS
                cs.Dispatch(kernel, 1, 1, 1);

                // readback
                surfels.GetData(readBackBuffer);
                return readBackBuffer;
            }
            finally
            {
                surfels.Release();
            }
        }

        private float[] SampleReflectionProbe(RenderTexture lightingCubemap)
        {
            var coefficientSH9 = new ComputeBuffer(27, sizeof(float));
            try
            {
                var cs = _reflectionProbeSampleCS;
                var kernel = _reflectionProbeSampleKernel;

                // set necessary data and start sample
                cs.SetFloat(ShaderProperties.RandSeed, Random.Range(0.0f, 1.0f));
                cs.SetTexture(kernel, ShaderProperties.InputCubemap, lightingCubemap);
                cs.SetBuffer(kernel, ShaderProperties.CoefficientSH9, coefficientSH9);

                // start CS
                cs.Dispatch(kernel, 1, 1, 1);

                var readBackBuffer = new float[27];
                // readback
                coefficientSH9.GetData(readBackBuffer);
                return readBackBuffer;
            }
            finally
            {
                coefficientSH9.Release();
            }
        }

        /// <summary>
        /// Check if the baker is properly initialized
        /// </summary>
        /// <returns>True if initialized</returns>
        private bool IsInitialized()
        {
            return _isInitialized && !_disposed;
        }

        private static bool ContributesGI(GameObject go) => (GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.ContributeGI) != 0;

        public async Task BakeVolume(PRTProbeVolume volume, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized())
            {
                Debug.LogError("[PRTBaker] Baker is already disposed or not initialized");
                return;
            }

            Renderer[] renderers = UObject.FindObjectsOfType(typeof(Renderer))
                .OfType<Renderer>().Where(r => ContributesGI(r.gameObject))
                .ToArray();
            var captureShader = Shader.Find(IllusionShaders.ProbeGBuffer);
            RecordAndSetShaders(renderers, captureShader);
            _cubemapCamera = CreateCubemapCamera();
            try
            {
                await volume.BakeDataAsync(this, cancellationToken);
            }
            finally
            {
                RestoreOriginalShaders();
                // Clean up temporary camera
                UObject.DestroyImmediate(_cubemapCamera.gameObject);
                _cubemapCamera = null;
            }
        }

        public void BakeReflectionProbe(ReflectionProbeAdditionalData probeAdditionalData, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized())
            {
                Debug.LogError("[PRTBaker] Baker is already disposed or not initialized");
                return;
            }

            _cubemapCamera = CreateCubemapCamera();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var coefficients = BakeAdditionalProbeData(probeAdditionalData.transform.position);
                probeAdditionalData.SetSHCoefficients(coefficients);
            }
            finally
            {
                // Clean up temporary camera
                UObject.DestroyImmediate(_cubemapCamera.gameObject);
                _cubemapCamera = null;
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _worldPosRT?.Release();
            _worldPosRT = null;
            _normalRT?.Release();
            _normalRT = null;
            _albedoRT?.Release();
            _albedoRT = null;

            _isInitialized = false;
            OnProgressUpdate = null;
            _disposed = true;
        }

        private static class ShaderProperties
        {
            public static readonly int ProbePos = Shader.PropertyToID("_probePos");

            public static readonly int RandSeed = Shader.PropertyToID("_randSeed");

            public static readonly int WorldPosCubemap = Shader.PropertyToID("_worldPosCubemap");

            public static readonly int NormalCubemap = Shader.PropertyToID("_normalCubemap");

            public static readonly int AlbedoCubemap = Shader.PropertyToID("_albedoCubemap");

            public static readonly int InputCubemap = Shader.PropertyToID("_inputCubemap");

            public static readonly int CoefficientSH9 = Shader.PropertyToID("_coefficientSH9");

            public static readonly int Surfels = Shader.PropertyToID("_surfels");
        }
    }
}