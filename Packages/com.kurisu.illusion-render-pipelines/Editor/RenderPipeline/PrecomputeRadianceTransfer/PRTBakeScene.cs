using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Rendering;
using Illusion.Rendering.PathTracing;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.Editor
{
    internal readonly struct PRTBakeInstance
    {
        internal readonly Mesh Mesh;
        internal readonly Matrix4x4 LocalToWorld;
        internal readonly Material Material;
        internal readonly MaterialPropertyBlock Properties;
        internal readonly int SubmeshIndex;
        internal readonly uint RenderingLayers, ObjectLayerMask, MaterialKey;
        internal readonly CullMode Cull;
        internal readonly bool AnyHit, Solid;

        internal PRTBakeInstance(Renderer renderer, Mesh mesh, Matrix4x4 localToWorld, Material material, MaterialPropertyBlock properties,
            int submeshIndex, uint materialKey)
        {
            Mesh = mesh;
            LocalToWorld = localToWorld;
            Material = material;
            Properties = properties.isEmpty ? null : properties;
            SubmeshIndex = submeshIndex;
            RenderingLayers = renderer.renderingLayerMask;
            ObjectLayerMask = 1u << renderer.gameObject.layer;
            MaterialKey = materialKey;
            Cull = CullOf(material, properties);
            AnyHit = material.renderQueue >= (int)RenderQueue.AlphaTest || material.IsKeywordEnabled("_ALPHATEST_ON") ||
                material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
            Solid = Cull == CullMode.Back && !AnyHit && !material.doubleSidedGI &&
                material.GetTag("RenderType", false) is not ("Transparent" or "TransparentCutout") &&
                !(material.HasProperty("_Surface") && material.GetFloat("_Surface") != 0);
        }

        // Shaders without a cull property are treated as two-sided.
        private static CullMode CullOf(Material material, MaterialPropertyBlock properties)
        {
            foreach (string name in new[] { "_CullMode", "_Cull" })
            {
                if (!material.HasProperty(name)) continue;
                return (CullMode)Mathf.RoundToInt(properties.HasFloat(name) ? properties.GetFloat(name) : material.GetFloat(name));
            }
            return CullMode.Off;
        }
    }

    internal sealed class PRTBakeScene : IDisposable
    {
        internal readonly PRTBakeInstance[] instances;
        internal readonly Bounds bounds;
        internal readonly Hash128 geometrySignature;
        internal readonly Hash128 materialSignature;
        internal readonly string[] emptyGeometry;
        private readonly List<Mesh> _meshes = new();

        internal PRTBakeScene(Renderer[] renderers)
        {
            var items = new List<PRTBakeInstance>();
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
                    for (int submesh = 0; submesh < sourceMaterials.Length; submesh++)
                    {
                        Material source = sourceMaterials[submesh];
                        if (!source) throw new InvalidOperationException($"PRT capture has an empty material slot ({renderer.name}).");
                        RequirePathTracingPass(source);
                        var key = (source, renderer.HasPropertyBlock() ? renderer : null);
                        if (!materialKeys.TryGetValue(key, out uint materialKey))
                        {
                            materialKey = (uint)materialKeys.Count + 1;
                            materialKeys.Add(key, materialKey);
                        }
                        var properties = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(properties, submesh);
                        if (properties.isEmpty) renderer.GetPropertyBlock(properties);
                        var item = new PRTBakeInstance(renderer, mesh, matrix, source, properties, submesh, materialKey);
                        items.Add(item);
                        AppendMaterial(ref materials, source);
                        AppendPropertyBlock(ref materials, source.shader, properties);
                        materials.Append(item.RenderingLayers);
                        materials.Append(item.ObjectLayerMask);
                        materials.Append(materialKey);
                    }
                    if (!hasBounds) { geometryBounds = renderer.bounds; hasBounds = true; }
                    else geometryBounds.Encapsulate(renderer.bounds);
                }
                instances = items.ToArray();
                bounds = geometryBounds;
                geometrySignature = geometry;
                materialSignature = materials;
                emptyGeometry = empties.ToArray();
            }
            catch { Dispose(); throw; }
        }

        private static void RequirePathTracingPass(Material material)
        {
            if (!material.shader) throw new InvalidOperationException($"PRT capture material '{material.name}' has no shader.");
            if (material.FindPass(PathTracingPass.MaterialPassName) < 0)
                throw new NotSupportedException($"PRT capture requires a {PathTracingPass.MaterialPassName} pass for '{material.shader.name}' ({material.name}).");
            ShaderMessage[] errors = ShaderUtil.GetShaderMessages(material.shader)
                .Where(message => message.severity == ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException($"PRT capture shader '{material.shader.name}' failed to compile: " +
                string.Join("; ", errors.Select(message => message.file + ":" + message.line + " " + message.message)));
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
            foreach (Mesh mesh in _meshes) UObject.DestroyImmediate(mesh);
            _meshes.Clear();
        }
    }
}
