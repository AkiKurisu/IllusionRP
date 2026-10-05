using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.Editor
{
    internal sealed class PRTBakeScene : IDisposable
    {
        internal readonly PRTGBufferCaptureDrawItem[] drawItems;
        internal readonly PRTMeshVirtualOffset placement = new();
        internal readonly Bounds bounds;
        internal readonly Hash128 geometrySignature;
        internal readonly Hash128 materialSignature;
        internal readonly string[] emptyGeometry;
        private readonly List<Mesh> _meshes = new();
        private readonly List<Material> _materials = new();

        internal PRTBakeScene(Renderer[] renderers)
        {
            var items = new List<PRTGBufferCaptureDrawItem>();
            var meshCopies = new Dictionary<Mesh, Mesh>();
            var meshHashes = new Dictionary<Mesh, Hash128>();
            var materialKeys = new Dictionary<(Material, Renderer), uint>();
            var empties = new List<string>();
            Hash128 geometry = default, materials = default;
            Bounds geometryBounds = default;
            bool hasBounds = false;
            try
            {
                foreach (Renderer renderer in renderers.OrderBy(r => GlobalObjectId.GetGlobalObjectIdSlow(r).ToString(), StringComparer.Ordinal))
                {
                    if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    if (renderer is ParticleSystemRenderer && renderer.TryGetComponent(out ParticleSystem particles))
                    {
                        if (particles.particleCount != 0)
                            throw new NotSupportedException($"PRT capture does not snapshot nonempty camera-facing particles ({renderer.name}).");
                        string empty = $"{renderer.name}: ParticleSystemRenderer, 0 particles";
                        empties.Add(empty);
                        geometry.Append(GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString());
                        geometry.Append(empty);
                        continue;
                    }
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        skinned.BakeMesh(mesh);
                        _meshes.Add(mesh);
                    }
                    else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh)
                    {
                        Mesh original = filter.sharedMesh;
                        if (!meshCopies.TryGetValue(original, out mesh))
                        {
                            mesh = UObject.Instantiate(original);
                            mesh.hideFlags = HideFlags.HideAndDontSave;
                            meshCopies.Add(original, mesh);
                            _meshes.Add(mesh);
                        }
                        AppendObject(ref geometry, original);
                    }
                    else throw new NotSupportedException($"PRT capture requires a mesh snapshot for {renderer.GetType().Name} ({renderer.name}).");
                    Matrix4x4 matrix = renderer.localToWorldMatrix;
                    if (!meshHashes.TryGetValue(mesh, out Hash128 meshHash))
                    {
                        meshHash = default;
                        AppendMesh(ref meshHash, mesh);
                        meshHashes.Add(mesh, meshHash);
                    }
                    geometry.Append(meshHash.ToString());
                    geometry.Append(GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString());
                    for (int i = 0; i < 16; i++) geometry.Append(matrix[i]);
                    geometry.Append(unchecked((int)renderer.renderingLayerMask));
                    geometry.Append(renderer.gameObject.layer);
                    Material[] sourceMaterials = renderer.sharedMaterials;
                    if (sourceMaterials.Length != mesh.subMeshCount)
                        throw new NotSupportedException($"PRT capture requires matching material/submesh slots ({renderer.name}).");
                    Bounds rendererBounds = renderer.bounds;
                    float windPadding = 0;
                    int firstItem = items.Count;
                    var solid = new bool[mesh.subMeshCount];
                    for (int submesh = 0; submesh < sourceMaterials.Length; submesh++)
                    {
                        Material source = sourceMaterials[submesh];
                        if (!source) throw new InvalidOperationException($"PRT capture has an empty material slot ({renderer.name}).");
                        var key = (source, renderer.HasPropertyBlock() ? renderer : null);
                        if (!materialKeys.TryGetValue(key, out uint materialKey))
                        {
                            materialKey = (uint)materialKeys.Count + 1;
                            materialKeys.Add(key, materialKey);
                        }
                        if (materialKey >= 16777216u) throw new InvalidOperationException("PRT material metadata exceeds FP32 exact integer range.");
                        Material capture = PRTCaptureMaterial.Create(source, renderer, submesh, materialKey,
                            out MaterialPropertyBlock properties, out int pass, out bool isSolid);
                        _materials.Add(capture);
                        if (submesh < solid.Length) solid[submesh] = isSolid;
                        items.Add(new PRTGBufferCaptureDrawItem(renderer, mesh, matrix, capture, properties, submesh, pass, rendererBounds));
                        AppendMaterial(ref materials, source);
                        AppendPropertyBlock(ref materials, source.shader, properties);
                        if (source.shader.name == "AE/Leaves")
                        {
                            float power = properties.HasFloat("_WindPower") ? properties.GetFloat("_WindPower") : source.GetFloat("_WindPower");
                            Vector3 scale = renderer.transform.lossyScale;
                            windPadding = Mathf.Max(windPadding, 2f * Mathf.Sqrt(3f) * Mathf.Abs(power) *
                                Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                        }
                        materials.Append(properties.GetVector("_PRTMetadata").ToString("R"));
                    }
                    rendererBounds.Expand(windPadding);
                    for (int i = firstItem; i < items.Count; i++)
                    {
                        PRTGBufferCaptureDrawItem item = items[i];
                        items[i] = new PRTGBufferCaptureDrawItem(item.Renderer, item.Mesh, item.LocalToWorld, item.Material,
                            item.Properties, item.SubmeshIndex, item.PassIndex, rendererBounds);
                    }
                    if (!hasBounds) { geometryBounds = rendererBounds; hasBounds = true; }
                    else geometryBounds.Encapsulate(rendererBounds);
                    placement.AddMesh(mesh, matrix, solid);
                }
                placement.Build();
                drawItems = items.ToArray();
                bounds = geometryBounds;
                geometrySignature = geometry;
                materialSignature = materials;
                emptyGeometry = empties.ToArray();
            }
            catch { Dispose(); throw; }
        }

        private static void AppendObject(ref Hash128 hash, UObject value)
        {
            if (!value) { hash.Append("null"); return; }
            hash.Append(GlobalObjectId.GetGlobalObjectIdSlow(value).ToString());
            string path = AssetDatabase.GetAssetPath(value);
            if (!string.IsNullOrEmpty(path)) hash.Append(AssetDatabase.GetAssetDependencyHash(path).ToString());
        }
        private static void AppendMesh(ref Hash128 hash, Mesh mesh)
        {
            using var array = MeshUtility.AcquireReadOnlyMeshData(mesh);
            Mesh.MeshData data = array[0];
            using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            data.GetVertices(vertices);
            hash.Append(data.vertexCount);
            hash.Append(vertices);
            for (int submesh = 0; submesh < data.subMeshCount; submesh++)
            {
                SubMeshDescriptor descriptor = data.GetSubMesh(submesh);
                hash.Append((int)descriptor.topology);
                using var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp);
                data.GetIndices(indices, submesh);
                hash.Append(indices);
            }
        }
        private static void AppendMaterial(ref Hash128 hash, Material material)
        {
            AppendObject(ref hash, material);
            hash.Append(material.shader.name);
            foreach (string keyword in material.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)) hash.Append(keyword);
            for (int i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                string property = material.shader.GetPropertyName(i);
                hash.Append(property);
                switch (material.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: hash.Append(material.GetColor(property).ToString("R")); break;
                    case ShaderPropertyType.Vector: hash.Append(material.GetVector(property).ToString("R")); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: hash.Append(material.GetFloat(property)); break;
                    case ShaderPropertyType.Int: hash.Append(material.GetInteger(property)); break;
                    case ShaderPropertyType.Texture:
                        Texture texture = material.GetTexture(property);
                        if (texture is RenderTexture) throw new NotSupportedException($"PRT capture requires a frozen texture ({material.name}.{property}).");
                        AppendObject(ref hash, texture);
                        hash.Append(material.GetTextureScale(property).ToString("R"));
                        hash.Append(material.GetTextureOffset(property).ToString("R"));
                        break;
                }
            }
        }
        private static void AppendPropertyBlock(ref Hash128 hash, Shader shader, MaterialPropertyBlock properties)
        {
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string property = shader.GetPropertyName(i);
                if (!properties.HasProperty(property)) continue;
                hash.Append(property);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: hash.Append(properties.GetColor(property).ToString("R")); break;
                    case ShaderPropertyType.Vector: hash.Append(properties.GetVector(property).ToString("R")); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: hash.Append(properties.GetFloat(property)); break;
                    case ShaderPropertyType.Int: hash.Append(properties.GetInteger(property)); break;
                    case ShaderPropertyType.Texture:
                        Texture texture = properties.GetTexture(property);
                        if (texture is RenderTexture) throw new NotSupportedException($"PRT capture requires a frozen property-block texture ({property}).");
                        AppendObject(ref hash, texture);
                        break;
                }
            }
        }
        public void Dispose()
        {
            foreach (Material material in _materials) UObject.DestroyImmediate(material);
            foreach (Mesh mesh in _meshes) UObject.DestroyImmediate(mesh);
            _materials.Clear();
            _meshes.Clear();
        }
    }
}
