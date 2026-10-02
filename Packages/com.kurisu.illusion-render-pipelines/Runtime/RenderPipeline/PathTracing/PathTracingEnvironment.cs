using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingEnvironment : IDisposable
    {
        public const int CubeSize = 1024;

        public const int ImportanceMapSize = 1024;

        private const int SamplesPerTexelAxis = 4;

        private const int ImportanceThreadGroupSize = 16;

        private const int ProjectionDirectionCount = 4096;

        private const int LightingEnvironmentPass = 1;

        private const int LightingProbePass = 0;

        private static readonly string[] SunDiskKeywords = { "_SUNDISK_SIMPLE", "_SUNDISK_HIGH_QUALITY" };

        private readonly ComputeShader _importanceShader;

        private readonly int _importanceKernel;

        private readonly GraphicsBuffer _constants;

        private readonly EnvMapImportanceSamplingBakerConstants[] _constantsData = new EnvMapImportanceSamplingBakerConstants[1];

        private readonly Mesh _skyMesh;

        private readonly Matrix4x4 _faceProjection;

        private Material _bakeMaterial;

        private readonly Material _lightingMaterial;

        private static readonly Vector3[] ProjectionDirections = CreateProjectionDirections();

        private readonly Color[] _probeRadiance = new Color[ProjectionDirectionCount];

        private readonly Vector4[] _radianceSH = new Vector4[9];

        private readonly RenderTexture _sourceCube;

        private RenderTexture _background;

        private int _hash;

        public RenderTexture Cube { get; }

        public RenderTexture ImportanceMap { get; }

        public RenderTexture RadianceMap { get; }

        public RenderTexture Background { get; private set; }

        public int ImportanceMapMipCount => ImportanceMap.mipmapCount;

        public int Version { get; private set; }

        public PathTracingEnvironment(ComputeShader importanceShader, Shader lightingShader)
        {
            _lightingMaterial = CoreUtils.CreateEngineMaterial(lightingShader);
            _importanceShader = importanceShader;
            _importanceKernel = importanceShader.FindKernel("BuildMIPDescentImportanceMapCS");
            _constants = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, EnvMapImportanceSamplingBakerConstants.Stride);
            _skyMesh = CreateSkyMesh();
            _faceProjection = Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f)) * Matrix4x4.Perspective(90.0f, 1.0f, 0.1f, 10.0f);

            _sourceCube = CreateTexture("_PathTracingEnvironmentSource", CubeSize, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Cube, false);
            Cube = CreateTexture("_PathTracingEnvironmentCube", CubeSize, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Cube, false);
            ImportanceMap = CreateTexture("_PathTracingEnvironmentImportance", ImportanceMapSize, GraphicsFormat.R32_SFloat, TextureDimension.Tex2D, true);
            RadianceMap = CreateTexture("_PathTracingEnvironmentRadiance", ImportanceMapSize, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Tex2D, true);
        }

        public static int ComputeHash(PathTracingLightCollector lights)
        {
            var skybox = RenderSettings.skybox;
            int hash = HashCode.Combine(skybox ? skybox.GetInstanceID() : 0, skybox ? skybox.ComputeCRC() : 0,
                SunDirection(lights.Sun), SunColor(lights.Sun), lights.HasDirectionalLights, LightsWithSkybox);
            if (LightsWithSkybox)
                return HashCode.Combine(hash, RenderSettings.ambientIntensity);
            var probe = RenderSettings.ambientProbe;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    hash = HashCode.Combine(hash, probe[channel, coefficient]);
            return hash;
        }

        private static bool LightsWithSkybox => RenderSettings.ambientMode == AmbientMode.Skybox;

        public static Vector4 CameraBackground(Camera camera)
        {
            if (camera.clearFlags == CameraClearFlags.Skybox)
                return Vector4.zero;
            var color = camera.backgroundColor.linear;
            return new Vector4(color.r, color.g, color.b, 1.0f);
        }

        public void Update(CommandBuffer cmd, PathTracingLightCollector lights, RenderTargetIdentifier exposureTexture)
        {
            int hash = ComputeHash(lights);
            if (hash == _hash && Cube.IsCreated() && _sourceCube.IsCreated() && ImportanceMap.IsCreated() && RadianceMap.IsCreated()
                && Background && Background.IsCreated() && Version != 0)
                return;
            _hash = hash; Version++;
            if (!Cube.IsCreated()) Cube.Create();
            if (!_sourceCube.IsCreated()) _sourceCube.Create();
            if (!ImportanceMap.IsCreated()) ImportanceMap.Create();
            if (!RadianceMap.IsCreated()) RadianceMap.Create();

            var sunDirection = SunDirection(lights.Sun);
            cmd.SetGlobalTexture(ShaderIDs._ExposureTexture, Texture2D.whiteTexture);
            cmd.SetGlobalVector(ShaderIDs._WorldSpaceLightPos0, new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0));
            cmd.SetGlobalVector(ShaderIDs._LightColor0, SunColor(lights.Sun));
            var skybox = RenderSettings.skybox;
            var skyMaterial = skybox ? GetBakeMaterial(skybox, lights.HasDirectionalLights) : null;
            if (LightsWithSkybox)
            {
                DrawFaces(cmd, _sourceCube, skyMaterial, 0);
                Background = _sourceCube;
            }
            else
            {
                _background ??= CreateTexture("_PathTracingEnvironmentBackground", CubeSize, GraphicsFormat.R16G16B16A16_SFloat, TextureDimension.Cube, false);
                if (!_background.IsCreated()) _background.Create();
                DrawFaces(cmd, _background, skyMaterial, 0);
                Background = _background;
                ProjectAmbientProbe();
                DrawFaces(cmd, _sourceCube, _lightingMaterial, LightingProbePass);
            }
            _lightingMaterial.SetTexture(ShaderIDs._PathTracingSourceCube, _sourceCube);
            _lightingMaterial.SetFloat(ShaderIDs._PathTracingSourceScale, LightsWithSkybox ? RenderSettings.ambientIntensity : 1);
            _lightingMaterial.SetInt(ShaderIDs._PathTracingCubeDim, CubeSize);
            DrawFaces(cmd, Cube, _lightingMaterial, LightingEnvironmentPass);
            cmd.SetGlobalTexture(ShaderIDs._ExposureTexture, exposureTexture);
            cmd.GenerateMips(Cube);
            BuildImportanceMaps(cmd);
        }

        private void DrawFaces(CommandBuffer cmd, RenderTexture target, Material material, int pass)
        {
            for (int face = 0; face < 6; face++)
            {
                cmd.SetRenderTarget(target, 0, (CubemapFace)face);
                cmd.SetGlobalInt(ShaderIDs._PathTracingCubeFace, face);
                cmd.ClearRenderTarget(false, true, Color.clear);
                if (!material)
                    continue;
                cmd.SetViewProjectionMatrices(FaceView((CubemapFace)face), _faceProjection);
                cmd.DrawMesh(_skyMesh, Matrix4x4.identity, material, 0, pass);
            }
        }

        private void ProjectAmbientProbe()
        {
            var radiance = RenderSettings.ambientProbe;
            for (int channel = 0; channel < 3; channel++)
            {
                for (int coefficient = 1; coefficient < 9; coefficient++)
                    radiance[channel, coefficient] *= coefficient < 4 ? 1.5f : 4.0f;
            }
            radiance.Evaluate(ProjectionDirections, _probeRadiance);

            Array.Clear(_radianceSH, 0, _radianceSH.Length);
            float weight = 4.0f * Mathf.PI / ProjectionDirectionCount;
            for (int i = 0; i < ProjectionDirectionCount; i++)
            {
                var d = ProjectionDirections[i];
                var value = new Vector4(_probeRadiance[i].r, _probeRadiance[i].g, _probeRadiance[i].b, 0.0f) * weight;
                _radianceSH[0] += value * 0.282095f;
                _radianceSH[1] += value * (0.488603f * d.y);
                _radianceSH[2] += value * (0.488603f * d.z);
                _radianceSH[3] += value * (0.488603f * d.x);
                _radianceSH[4] += value * (1.092548f * d.x * d.y);
                _radianceSH[5] += value * (1.092548f * d.y * d.z);
                _radianceSH[6] += value * (0.315392f * (3.0f * d.z * d.z - 1.0f));
                _radianceSH[7] += value * (1.092548f * d.x * d.z);
                _radianceSH[8] += value * (0.546274f * (d.x * d.x - d.y * d.y));
            }
            _lightingMaterial.SetVectorArray(ShaderIDs._PathTracingRadianceSH, _radianceSH);
        }

        private static Vector3[] CreateProjectionDirections()
        {
            var directions = new Vector3[ProjectionDirectionCount];
            float golden = Mathf.PI * (3.0f - Mathf.Sqrt(5.0f));
            for (int i = 0; i < ProjectionDirectionCount; i++)
            {
                float y = 1.0f - (i + 0.5f) * 2.0f / ProjectionDirectionCount;
                float r = Mathf.Sqrt(1.0f - y * y);
                float phi = golden * i;
                directions[i] = new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
            }
            return directions;
        }

        private void BuildImportanceMaps(CommandBuffer cmd)
        {
            int size = ImportanceMapSize;
            _constantsData[0] = new EnvMapImportanceSamplingBakerConstants
            {
                SourceCubeDim = (uint)CubeSize,
                SourceCubeMIPCount = (uint)Cube.mipmapCount,
                ImportanceMapDimX = (uint)size,
                ImportanceMapDimY = (uint)size,
                ImportanceMapDimInSamplesX = (uint)(size * SamplesPerTexelAxis),
                ImportanceMapDimInSamplesY = (uint)(size * SamplesPerTexelAxis),
                ImportanceMapNumSamplesX = SamplesPerTexelAxis,
                ImportanceMapNumSamplesY = SamplesPerTexelAxis,
                ImportanceMapInvSamples = 1.0f / (SamplesPerTexelAxis * SamplesPerTexelAxis),
                ImportanceMapBaseMip = (uint)(ImportanceMap.mipmapCount - 1)
            };
            cmd.SetBufferData(_constants, _constantsData);

            cmd.SetComputeBufferParam(_importanceShader, _importanceKernel, ShaderIDs.t_BuilderConstants, _constants);
            cmd.SetComputeTextureParam(_importanceShader, _importanceKernel, ShaderIDs.t_EnvMapCube, Cube);
            cmd.SetComputeTextureParam(_importanceShader, _importanceKernel, ShaderIDs.u_ImportanceMap, ImportanceMap, 0);
            cmd.SetComputeTextureParam(_importanceShader, _importanceKernel, ShaderIDs.u_RadianceMap, RadianceMap, 0);
            int groups = size / ImportanceThreadGroupSize;
            cmd.DispatchCompute(_importanceShader, _importanceKernel, groups, groups, 1);

            cmd.GenerateMips(ImportanceMap);
            cmd.GenerateMips(RadianceMap);
        }

        private static Vector3 SunDirection(Light sun) => sun ? -sun.transform.forward : Vector3.up;

        private static Color SunColor(Light sun) => sun ? sun.color.linear * sun.intensity : Color.black;

        private Material GetBakeMaterial(Material skybox, bool removeSunDisk)
        {
            if (!removeSunDisk || !HasSunDisk(skybox))
                return skybox;

            if (!_bakeMaterial || _bakeMaterial.shader != skybox.shader)
            {
                CoreUtils.Destroy(_bakeMaterial);
                _bakeMaterial = new Material(skybox) { name = "PathTracingSkybox", hideFlags = HideFlags.HideAndDontSave };
            }
            _bakeMaterial.CopyPropertiesFromMaterial(skybox);
            foreach (var keyword in SunDiskKeywords)
                _bakeMaterial.DisableKeyword(keyword);
            _bakeMaterial.EnableKeyword("_SUNDISK_NONE");
            return _bakeMaterial;
        }

        private static bool HasSunDisk(Material material)
        {
            foreach (var keyword in SunDiskKeywords)
            {
                if (material.IsKeywordEnabled(keyword))
                    return true;
            }
            return false;
        }

        private static Matrix4x4 FaceView(CubemapFace face)
        {
            Vector3 forward, up;
            switch (face)
            {
                case CubemapFace.PositiveX: forward = Vector3.right; up = Vector3.up; break;
                case CubemapFace.NegativeX: forward = Vector3.left; up = Vector3.up; break;
                case CubemapFace.PositiveY: forward = Vector3.up; up = Vector3.back; break;
                case CubemapFace.NegativeY: forward = Vector3.down; up = Vector3.forward; break;
                case CubemapFace.PositiveZ: forward = Vector3.forward; up = Vector3.up; break;
                default: forward = Vector3.back; up = Vector3.up; break;
            }
            return Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * Matrix4x4.LookAt(Vector3.zero, forward, up).inverse;
        }

        private static Mesh CreateSkyMesh()
        {
            var vertices = new Vector3[8];
            for (int i = 0; i < 8; i++)
                vertices[i] = new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1);
            var mesh = new Mesh { name = "PathTracingSkyCube", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, vertices);
            mesh.SetTriangles(new[]
            {
                0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5
            }, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);
            return mesh;
        }

        private static RenderTexture CreateTexture(string name, int size, GraphicsFormat format, TextureDimension dimension, bool randomWrite)
        {
            var texture = new RenderTexture(new RenderTextureDescriptor(size, size, format, GraphicsFormat.None)
            {
                dimension = dimension,
                useMipMap = true,
                autoGenerateMips = false,
                enableRandomWrite = randomWrite,
                msaaSamples = 1
            })
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return texture;
        }

        public void Release()
        {
            if (!Cube.IsCreated())
                return;
            Cube.Release();
            ImportanceMap.Release();
            RadianceMap.Release();
            _sourceCube.Release();
            _background?.Release();
        }

        public void Dispose()
        {
            _constants.Release();
            CoreUtils.Destroy(_bakeMaterial);
            CoreUtils.Destroy(_lightingMaterial);
            CoreUtils.Destroy(_sourceCube);
            CoreUtils.Destroy(_background);
            CoreUtils.Destroy(_skyMesh);
            CoreUtils.Destroy(Cube);
            CoreUtils.Destroy(ImportanceMap);
            CoreUtils.Destroy(RadianceMap);
        }

        private static class ShaderIDs
        {
            public static readonly int _ExposureTexture = Shader.PropertyToID("_ExposureTexture");
            public static readonly int _WorldSpaceLightPos0 = Shader.PropertyToID("_WorldSpaceLightPos0");
            public static readonly int _LightColor0 = Shader.PropertyToID("_LightColor0");
            public static readonly int _PathTracingRadianceSH = Shader.PropertyToID("_PathTracingRadianceSH");
            public static readonly int _PathTracingSourceCube = Shader.PropertyToID("_PathTracingSourceCube");
            public static readonly int _PathTracingSourceScale = Shader.PropertyToID("_PathTracingSourceScale");
            public static readonly int _PathTracingCubeDim = Shader.PropertyToID("_PathTracingCubeDim");
            public static readonly int _PathTracingCubeFace = Shader.PropertyToID("_PathTracingCubeFace");
            public static readonly int t_BuilderConstants = Shader.PropertyToID("t_BuilderConstants");
            public static readonly int t_EnvMapCube = Shader.PropertyToID("t_EnvMapCube");
            public static readonly int u_ImportanceMap = Shader.PropertyToID("u_ImportanceMap");
            public static readonly int u_RadianceMap = Shader.PropertyToID("u_RadianceMap");
        }
    }
}
