using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTEnvironmentSnapshot : IDisposable
    {
        private const int CubeSize = 64;
        public int ContentHash { get; }
        public float Intensity { get; }
        public RTHandle Cube { get; }
        private readonly Material _skyMaterial;
        private readonly Mesh _skyMesh;
        private readonly Vector4 _sunDirection;
        private readonly Color _sunColor;
        private readonly float _sceneTime;
        private readonly List<RTHandle> _materialTextures = new();
        private bool _recorded;

        public static int ComputeHash(bool hasDirectionalLight)
        {
            var hash = new HashCode();
            hash.Add(RenderSettings.ambientMode);
            hash.Add(RenderSettings.ambientIntensity);
            hash.Add(RenderSettings.ambientLight);
            var probe = RenderSettings.ambientProbe;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    hash.Add(probe[channel, coefficient]);
            var material = RenderSettings.skybox;
            hash.Add(material ? material.ComputeCRC() : 0);
            hash.Add(hasDirectionalLight);
            if (material)
            {
                foreach (string name in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(name);
                    hash.Add(texture ? texture.GetInstanceID() : 0);
                    hash.Add(texture ? texture.updateCount : 0);
                }
            }
            Light sun = RenderSettings.sun;
            if (sun)
            {
                hash.Add(sun.transform.rotation);
                hash.Add(sun.color);
                hash.Add(sun.intensity);
                hash.Add(sun.colorTemperature);
                hash.Add(sun.useColorTemperature);
            }
            return hash.ToHashCode();
        }

        public PRTEnvironmentSnapshot(int contentHash, bool hasDirectionalLight, float sceneTime)
        {
            ContentHash = contentHash;
            _sceneTime = sceneTime;
            var skybox = RenderSettings.skybox;
            bool sky = RenderSettings.ambientMode == AmbientMode.Skybox && skybox;
            Intensity = sky ? RenderSettings.ambientIntensity : 1;
            Light sun = RenderSettings.sun;
            Vector3 direction = sun ? -sun.transform.forward : Vector3.up;
            _sunDirection = new Vector4(direction.x, direction.y, direction.z, 0);
            _sunColor = sun ? sun.color.linear * sun.intensity : Color.white;
            if (sun && sun.useColorTemperature)
                _sunColor *= Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature);
            if (!sky)
            {
                Cube = RTHandles.Alloc(CreateAmbientCube());
                _recorded = true;
                return;
            }
            _skyMaterial = new Material(skybox) { name = "PRT frozen environment", hideFlags = HideFlags.HideAndDontSave };
            if (hasDirectionalLight && (_skyMaterial.IsKeywordEnabled("_SUNDISK_SIMPLE") || _skyMaterial.IsKeywordEnabled("_SUNDISK_HIGH_QUALITY")))
            {
                _skyMaterial.DisableKeyword("_SUNDISK_SIMPLE");
                _skyMaterial.DisableKeyword("_SUNDISK_HIGH_QUALITY");
                _skyMaterial.EnableKeyword("_SUNDISK_NONE");
            }
            foreach (string name in _skyMaterial.GetTexturePropertyNames())
                if (_skyMaterial.GetTexture(name))
                    _materialTextures.Add(RTHandles.Alloc(_skyMaterial.GetTexture(name)));
            _skyMesh = CreateSkyMesh();
            Cube = RTHandles.Alloc(CubeSize, CubeSize, dimension: TextureDimension.Cube,
                colorFormat: GraphicsFormat.R32G32B32A32_SFloat, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, name: "PRT environment radiance");
        }

        private static Cubemap CreateAmbientCube()
        {
            var cube = new Cubemap(CubeSize, TextureFormat.RGBAFloat, false)
            {
                name = "PRT ambient radiance", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var probe = RenderSettings.ambientProbe;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 1; coefficient < 9; coefficient++)
                    probe[channel, coefficient] *= coefficient < 4 ? 1.5f : 4;
            var directions = new Vector3[CubeSize * CubeSize];
            var colors = new Color[directions.Length];
            bool flat = RenderSettings.ambientMode == AmbientMode.Flat;
            Color flatColor = RenderSettings.ambientLight.linear;
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < CubeSize; y++)
                    for (int x = 0; x < CubeSize; x++)
                        directions[y * CubeSize + x] = CubeDirection(face, (x + 0.5f) / CubeSize, (y + 0.5f) / CubeSize);
                if (!flat)
                    probe.Evaluate(directions, colors);
                for (int i = 0; i < colors.Length; i++)
                {
                    Color value = flat ? flatColor : colors[i];
                    colors[i] = new Color(Mathf.Max(0, value.r), Mathf.Max(0, value.g), Mathf.Max(0, value.b), 0);
                }
                cube.SetPixels(colors, (CubemapFace)face);
            }
            cube.Apply(false, true);
            return cube;
        }

        private static Vector3 CubeDirection(int face, float u, float v)
        {
            float x = 2 * u - 1, y = 2 * v - 1;
            return (face switch
            {
                0 => new Vector3(1, -y, -x), 1 => new Vector3(-1, -y, x),
                2 => new Vector3(x, 1, y), 3 => new Vector3(x, -1, -y),
                4 => new Vector3(x, -y, 1), _ => new Vector3(-x, -y, -1)
            }).normalized;
        }

        private sealed class CaptureData
        {
            public PRTEnvironmentSnapshot snapshot;
            public Matrix4x4 restoreView, restoreProjection;
            public RTHandle restoreExposure;
            public Vector4 restoreLightDirection, restoreLightColor, restoreTime, restoreSinTime, restoreCosTime;
        }

        public TextureHandle Record(RenderGraph graph, UniversalCameraData camera, UniversalLightData lights,
            RTHandle restoreExposure, TextureHandle cameraColor)
        {
            TextureHandle cube = graph.ImportTexture(Cube);
            if (_recorded)
                return cube;
            using (var builder = graph.AddUnsafePass<CaptureData>("PRT freeze environment radiance", out var data))
            {
            data.snapshot = this;
            data.restoreView = camera.GetViewMatrix();
            data.restoreProjection = camera.GetProjectionMatrix();
            data.restoreExposure = restoreExposure;
            if (lights.mainLightIndex >= 0)
            {
                var light = lights.visibleLights[lights.mainLightIndex];
                data.restoreLightDirection = light.lightType == LightType.Directional
                    ? -light.localToWorldMatrix.GetColumn(2) : light.localToWorldMatrix.GetColumn(3);
                data.restoreLightDirection.w = light.lightType == LightType.Directional ? 0 : 1;
                data.restoreLightColor = light.finalColor;
            }
            data.restoreTime = Shader.GetGlobalVector("_Time");
            data.restoreSinTime = Shader.GetGlobalVector("_SinTime");
            data.restoreCosTime = Shader.GetGlobalVector("_CosTime");
            builder.UseTexture(cube, AccessFlags.Write);
            builder.UseTexture(graph.ImportTexture(restoreExposure), AccessFlags.Read);
            foreach (var texture in _materialTextures)
                builder.UseTexture(graph.ImportTexture(texture), AccessFlags.Read);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (CaptureData pass, UnsafeGraphContext context) =>
            {
                var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                var snapshot = pass.snapshot;
                float time = snapshot._sceneTime;
                command.SetGlobalTexture("_ExposureTexture", Texture2D.whiteTexture);
                command.SetGlobalVector("_WorldSpaceLightPos0", snapshot._sunDirection);
                command.SetGlobalColor("_LightColor0", snapshot._sunColor);
                command.SetGlobalVector("_Time", new Vector4(time / 20, time, time * 2, time * 3));
                command.SetGlobalVector("_SinTime", new Vector4(Mathf.Sin(time / 8), Mathf.Sin(time / 4), Mathf.Sin(time / 2), Mathf.Sin(time)));
                command.SetGlobalVector("_CosTime", new Vector4(Mathf.Cos(time / 8), Mathf.Cos(time / 4), Mathf.Cos(time / 2), Mathf.Cos(time)));
                Matrix4x4 projection = Matrix4x4.Scale(new Vector3(1, -1, 1)) * Matrix4x4.Perspective(90, 1, 0.1f, 10);
                for (int face = 0; face < 6; face++)
                {
                    command.SetRenderTarget(snapshot.Cube.rt, 0, (CubemapFace)face);
                    command.ClearRenderTarget(false, true, Color.clear);
                    command.SetViewProjectionMatrices(FaceView(face), projection);
                    command.DrawMesh(snapshot._skyMesh, Matrix4x4.identity, snapshot._skyMaterial, 0, 0);
                }
                command.SetViewProjectionMatrices(pass.restoreView, pass.restoreProjection);
                command.SetGlobalTexture("_ExposureTexture", pass.restoreExposure);
                command.SetGlobalVector("_WorldSpaceLightPos0", pass.restoreLightDirection);
                command.SetGlobalVector("_LightColor0", pass.restoreLightColor);
                command.SetGlobalVector("_Time", pass.restoreTime);
                command.SetGlobalVector("_SinTime", pass.restoreSinTime);
                command.SetGlobalVector("_CosTime", pass.restoreCosTime);
                snapshot._recorded = true;
            });
            }
            if (camera.renderer is UniversalRenderer renderer)
                renderer.SetupRenderGraphCameraProperties(graph, cameraColor);
            return cube;
        }

        private static Matrix4x4 FaceView(int face)
        {
            Vector3 forward = face switch { 0 => Vector3.right, 1 => Vector3.left, 2 => Vector3.up, 3 => Vector3.down, 4 => Vector3.forward, _ => Vector3.back };
            Vector3 up = face switch { 2 => Vector3.back, 3 => Vector3.forward, _ => Vector3.up };
            return Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.LookAt(Vector3.zero, forward, up).inverse;
        }

        private static Mesh CreateSkyMesh()
        {
            var vertices = new Vector3[8];
            for (int i = 0; i < 8; i++)
                vertices[i] = new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1);
            var mesh = new Mesh { name = "PRT environment cube", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, vertices);
            mesh.SetTriangles(new[] { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 }, 0);
            return mesh;
        }

        public void Dispose()
        {
            Texture ambient = Cube.rt ? null : Cube.externalTexture;
            Cube.Release();
            if (ambient)
                CoreUtils.Destroy(ambient);
            foreach (var texture in _materialTextures)
                texture.Release();
            CoreUtils.Destroy(_skyMaterial);
            CoreUtils.Destroy(_skyMesh);
        }
    }
}
