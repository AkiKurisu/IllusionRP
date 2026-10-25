using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal struct PathTracingMaterialSlot
    {
        public (int Material, int Renderer, int Slot) Key;
        public uint IndexStart;
        public PathTracingMaterialData Data;
    }

    internal struct PathTracingEmissiveSubMesh
    {
        public int SubMesh;
        public int IndexStart;
        public int IndexCount;
        public int BaseVertex;
        public Vector4 Emission;
        public Texture Texture;
        public Vector4 BaseMapST;
        public int TargetIndex;
    }

    internal sealed class PathTracingRendererRecord
    {
        public PathTracingRendererRecord(Renderer renderer)
        {
            Renderer = renderer;
            InstanceId = renderer.GetInstanceID();
        }

        public Renderer Renderer { get; }

        public int InstanceId { get; }

        public Mesh Mesh;

        public EntityId MeshId;

        public uint ScatteringGroup;

        public uint RenderingLayers;

        public int Hash;

        public bool Dirty = true;

        public readonly List<Material> Materials = new();

        public readonly List<EntityId> MaterialIds = new();

        public readonly List<PathTracingCulledSubMesh> CulledSubMeshes = new();

        public readonly List<PathTracingMaterialSlot> MaterialSlots = new();

        public readonly List<PathTracingEmissiveSubMesh> EmissiveSubMeshes = new();
    }

    internal sealed class PathTracingRendererReader
    {
        private const int MaxCulledSubMeshCount = 0xFF;

        private static readonly int CullModeId = Shader.PropertyToID("_CullMode");

        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private static readonly HashSet<Shader> ReportedShaders = new();

        private readonly HashSet<int> _checkedMaterials = new();

        private readonly MaterialPropertyBlock _rendererBlock = new();

        private readonly MaterialPropertyBlock _slotBlock = new();

        public bool Read(PathTracingRendererRecord record)
        {
            var renderer = record.Renderer;
            var materials = record.Materials;
            renderer.GetSharedMaterials(materials);
            renderer.GetPropertyBlock(_rendererBlock);
            var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            var group = renderer.GetComponentInParent<PathTracingScatteringGroup>();
            record.Mesh = mesh;
            record.ScatteringGroup = unchecked((uint)(group ? group.GetInstanceID() : record.InstanceId));
            record.RenderingLayers = renderer.renderingLayerMask;
            record.CulledSubMeshes.Clear();
            record.MaterialSlots.Clear();
            record.EmissiveSubMeshes.Clear();

            bool hasBlock = renderer.HasPropertyBlock();
            int hash = HashCode.Combine(mesh ? mesh.GetInstanceID() : 0, record.ScatteringGroup, record.RenderingLayers,
                renderer.shadowCastingMode, materials.Count);
            for (int slot = 0; slot < materials.Count; slot++)
            {
                var material = materials[slot];
                hash = HashCode.Combine(hash, material ? material.GetInstanceID() : 0);
                if (!material)
                    continue;
                ReportMissingPass(material);
                if (!mesh)
                    continue;
                renderer.GetPropertyBlock(_slotBlock, slot);
                var block = _slotBlock.isEmpty ? _rendererBlock : _slotBlock;
                if (hasBlock)
                    hash = HashCode.Combine(hash, HashBlock(material, _rendererBlock), HashBlock(material, _slotBlock));
                if (slot < mesh.subMeshCount)
                {
                    uint indexStart = (uint)mesh.GetSubMesh(slot).indexStart;
                    var key = (material.GetInstanceID(), block.isEmpty ? 0 : record.InstanceId, _slotBlock.isEmpty ? -1 : slot);
                    var data = PathTracingMaterialTable.ReadMaterial(material, block);
                    record.MaterialSlots.Add(new PathTracingMaterialSlot { Key = key, IndexStart = indexStart, Data = data });
                    hash = HashCode.Combine(hash, key, indexStart, data.IoR, data.AttenuationColor, data.AttenuationDistance, data.Flags);
                    uint faces = slot < MaxCulledSubMeshCount ? CulledFaces(material) : 0u;
                    if (faces != 0u)
                    {
                        record.CulledSubMeshes.Add(new PathTracingCulledSubMesh { IndexStart = indexStart, Faces = faces });
                        hash = HashCode.Combine(hash, faces);
                    }
                }
                if (PathTracingEmissiveTable.TryReadSubMesh(renderer, mesh, material, block, slot, out var emissive))
                {
                    record.EmissiveSubMeshes.Add(emissive);
                    hash = HashCode.Combine(hash, emissive.SubMesh, emissive.IndexStart, emissive.IndexCount, emissive.BaseVertex);
                    hash = HashCode.Combine(hash, emissive.Emission, emissive.Texture ? emissive.Texture.GetInstanceID() : 0,
                        emissive.BaseMapST, emissive.TargetIndex);
                }
            }
            bool changed = hash != record.Hash;
            record.Hash = hash;
            record.Dirty = false;
            return changed;
        }

        private static uint CulledFaces(Material material)
        {
            float mode;
            if (material.HasProperty(CullModeId))
                mode = material.GetFloat(CullModeId);
            else if (material.HasProperty(CullId))
                mode = material.GetFloat(CullId);
            else
                return 0u;
            return (CullMode)(int)mode switch
            {
                CullMode.Back => 1u,
                CullMode.Front => 2u,
                _ => 0u
            };
        }

        private void ReportMissingPass(Material material)
        {
            if (!_checkedMaterials.Add(material.GetInstanceID()) || !material.shader
                || material.FindPass(PathTracingPass.MaterialPassName) >= 0 || !ReportedShaders.Add(material.shader))
                return;
            IllusionLog.Warning($"Path Tracing: Shader '{material.shader.name}' has no {PathTracingPass.MaterialPassName} pass and renders as a diagnostic surface.", material);
        }

        private static int HashBlock(Material material, MaterialPropertyBlock block)
        {
            if (block.isEmpty)
                return 0;

            var shader = material.shader;
            int hash = 0;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                int id = shader.GetPropertyNameId(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        hash = HashCode.Combine(hash, block.GetVector(id));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        hash = HashCode.Combine(hash, block.GetFloat(id));
                        break;
                    case ShaderPropertyType.Int:
                        hash = HashCode.Combine(hash, block.GetInteger(id));
                        break;
                    case ShaderPropertyType.Texture:
                        var texture = block.GetTexture(id);
                        hash = HashCode.Combine(hash, texture ? texture.GetInstanceID() : 0);
                        break;
                }
            }
            return hash;
        }
    }
}
