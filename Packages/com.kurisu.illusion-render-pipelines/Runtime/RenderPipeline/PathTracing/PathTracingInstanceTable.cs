using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Illusion.Rendering.PathTracing
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingInstanceData
    {
        public const uint NoPositionHistory = 0xFFFFFFFF;

        public uint PreviousPositionBase;
        public uint CulledSubMeshes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingCulledSubMesh
    {
        public uint IndexStart;
        public uint Faces;
    }

    internal sealed class PathTracingInstanceTable : IDisposable
    {
        public const uint SceneMask = 0x01;

        private const int MaxCulledSubMeshCount = 0xFF;

        private static readonly int CullModeId = Shader.PropertyToID("_CullMode");

        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private readonly List<Renderer> _renderers = new();

        private static readonly List<Renderer> UnsavedRenderers = new();

        private readonly List<PathTracingInstanceData> _data = new();

        private readonly List<PathTracingCulledSubMesh> _culledSubMeshes = new();

        private readonly List<Material> _materials = new();

        private GraphicsBuffer _buffer;

        private GraphicsBuffer _culledSubMeshBuffer;

        public IReadOnlyList<Renderer> Renderers => _renderers;

        public List<PathTracingInstanceData> Data => _data;

        public GraphicsBuffer Buffer => _buffer;

        public GraphicsBuffer CulledSubMeshBuffer => _culledSubMeshBuffer;

        public PathTracingMaterialTable MaterialTable { get; } = new();

        public int Hash { get; private set; }

        internal static void RegisterUnsavedRenderer(Renderer renderer)
        {
            if (!UnsavedRenderers.Contains(renderer))
                UnsavedRenderers.Add(renderer);
        }

        internal static void UnregisterUnsavedRenderer(Renderer renderer) => UnsavedRenderers.Remove(renderer);

        public void Update(RayTracingAccelerationStructure accelerationStructure, int layerMask)
        {
            _renderers.Clear();
            _data.Clear();
            _culledSubMeshes.Clear();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                if (IsTraced(renderer, layerMask))
                    _renderers.Add(renderer);
            }
            UnsavedRenderers.RemoveAll(renderer => !renderer);
            foreach (var renderer in UnsavedRenderers)
            {
                if (renderer.gameObject.activeInHierarchy && IsTraced(renderer, layerMask))
                    _renderers.Add(renderer);
            }

            int hash = _renderers.Count;
            for (int i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                renderer.GetSharedMaterials(_materials);
                accelerationStructure.UpdateInstanceID(renderer, (uint)i);

                int culledStart = _culledSubMeshes.Count;
                AddCulledSubMeshes(renderer, _materials);
                int culledCount = _culledSubMeshes.Count - culledStart;
                _data.Add(new PathTracingInstanceData
                {
                    PreviousPositionBase = PathTracingInstanceData.NoPositionHistory,
                    CulledSubMeshes = culledCount > 0 ? (uint)culledStart << 8 | (uint)culledCount : 0u
                });
                hash = HashCode.Combine(hash, culledCount);
                for (int j = culledStart; j < _culledSubMeshes.Count; j++)
                    hash = HashCode.Combine(hash, _culledSubMeshes[j].IndexStart, _culledSubMeshes[j].Faces);
                foreach (var material in _materials)
                    hash = HashCode.Combine(hash, material ? material.GetInstanceID() : 0);
            }
            MaterialTable.Update(_renderers);
            Hash = HashCode.Combine(hash, MaterialTable.Hash);
        }

        public void Upload()
        {
            MaterialTable.Upload();
            int count = Mathf.Max(1, _data.Count);
            if (_buffer == null || _buffer.count < count)
            {
                _buffer?.Release();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(count), Marshal.SizeOf<PathTracingInstanceData>())
                {
                    name = "PathTracingInstances"
                };
            }
            if (_data.Count > 0)
                _buffer.SetData(_data);

            int culledCount = Mathf.Max(1, _culledSubMeshes.Count);
            if (_culledSubMeshBuffer == null || _culledSubMeshBuffer.count < culledCount)
            {
                _culledSubMeshBuffer?.Release();
                _culledSubMeshBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(culledCount), Marshal.SizeOf<PathTracingCulledSubMesh>())
                {
                    name = "PathTracingCulledSubMeshes"
                };
            }
            if (_culledSubMeshes.Count > 0)
                _culledSubMeshBuffer.SetData(_culledSubMeshes);
        }

        private void AddCulledSubMeshes(Renderer renderer, List<Material> materials)
        {
            var mesh = renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                _ => renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null
            };
            if (!mesh)
                return;
            int count = Mathf.Min(Mathf.Min(materials.Count, mesh.subMeshCount), MaxCulledSubMeshCount);
            for (int i = 0; i < count; i++)
            {
                var material = materials[i];
                if (!material)
                    continue;
                float mode;
                if (material.HasProperty(CullModeId))
                    mode = material.GetFloat(CullModeId);
                else if (material.HasProperty(CullId))
                    mode = material.GetFloat(CullId);
                else
                    continue;
                uint faces = (CullMode)(int)mode switch
                {
                    CullMode.Back => 1u,
                    CullMode.Front => 2u,
                    _ => 0u
                };
                if (faces != 0u)
                    _culledSubMeshes.Add(new PathTracingCulledSubMesh { IndexStart = (uint)mesh.GetSubMesh(i).indexStart, Faces = faces });
            }
        }

        private static bool IsTraced(Renderer renderer, int layerMask)
        {
            return renderer is (MeshRenderer or SkinnedMeshRenderer) && renderer.enabled
                && (layerMask & (1 << renderer.gameObject.layer)) != 0 && renderer.rayTracingMode != UnityEngine.Experimental.Rendering.RayTracingMode.Off;
        }

        public void Dispose()
        {
            MaterialTable.Dispose();
            _buffer?.Release();
            _buffer = null;
            _culledSubMeshBuffer?.Release();
            _culledSubMeshBuffer = null;
        }
    }
}
