using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        public uint ScatteringGroup;
        public uint RenderingLayers;
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

        public const uint ShadowMask = 0x02;

        // A longer gap between updates may outlast the engine's change history, so the table scans the scene again.
        private const int RescanFrameGap = 32;

        private static readonly List<Renderer> UnsavedRenderers = new();

        private static int _unsavedVersion;

        private readonly ObjectChangeTracker _tracker = new();

        private readonly PathTracingRendererReader _reader = new();

        private readonly Dictionary<EntityId, PathTracingRendererRecord> _records = new();

        private readonly List<PathTracingRendererRecord> _ordered = new();

        private readonly Dictionary<EntityId, int> _materialReferences = new();

        private readonly Dictionary<EntityId, int> _meshReferences = new();

        private readonly Dictionary<EntityId, int> _materialCRCs = new();

        private readonly HashSet<EntityId> _changedMaterials = new();

        private readonly HashSet<EntityId> _changedMeshes = new();

        private readonly List<Object> _changed = new();

        private readonly List<EntityId> _destroyed = new();

        private readonly List<Renderer> _reevaluate = new();

        private readonly List<Renderer> _renderers = new();

        private readonly List<PathTracingInstanceData> _data = new();

        private readonly List<PathTracingCulledSubMesh> _culledSubMeshes = new();

        private GraphicsBuffer _buffer;

        private GraphicsBuffer _culledSubMeshBuffer;

        private bool _scanned;

        private bool _membershipChanged;

        private int _layerMask;

        private int _updatedFrame;

        private int _scatteringVersion;

        private int _seenUnsavedVersion;

        public PathTracingInstanceTable()
        {
            _tracker.TrackSceneObjects<MeshRenderer>();
            _tracker.TrackSceneObjects<SkinnedMeshRenderer>();
            _tracker.TrackSceneObjects<MeshFilter>();
            _tracker.TrackAllObjects<Material>();
            _tracker.TrackAllObjects<Mesh>();
            _tracker.TrackHierarchy<MeshRenderer>();
            _tracker.TrackHierarchy<SkinnedMeshRenderer>();
        }

        public IReadOnlyList<Renderer> Renderers => _renderers;

        public IReadOnlyList<PathTracingRendererRecord> Records => _ordered;

        public List<PathTracingInstanceData> Data => _data;

        public GraphicsBuffer Buffer => _buffer;

        public GraphicsBuffer CulledSubMeshBuffer => _culledSubMeshBuffer;

        public PathTracingMaterialTable MaterialTable { get; } = new();

        public int Hash { get; private set; }

        public int Version { get; private set; }

        public bool Changed { get; private set; }

        internal static void RegisterUnsavedRenderer(Renderer renderer)
        {
            if (UnsavedRenderers.Contains(renderer))
                return;
            UnsavedRenderers.Add(renderer);
            _unsavedVersion++;
        }

        internal static void UnregisterUnsavedRenderer(Renderer renderer)
        {
            if (UnsavedRenderers.Remove(renderer))
                _unsavedVersion++;
        }

        public void Update(RayTracingAccelerationStructure accelerationStructure, int layerMask)
        {
            bool rescan = !_scanned || layerMask != _layerMask || PathTracingFrame.Index - _updatedFrame > RescanFrameGap;
            _scanned = true;
            _layerMask = layerMask;
            _updatedFrame = PathTracingFrame.Index;
            bool contentChanged = rescan ? Rescan() : ApplyChanges();
            if (_scatteringVersion != PathTracingScatteringGroup.Version)
            {
                _scatteringVersion = PathTracingScatteringGroup.Version;
                foreach (var record in _records.Values)
                    record.Dirty = true;
            }

            bool tablesChanged = _membershipChanged;
            foreach (var record in _records.Values)
            {
                if (!record.Dirty)
                    continue;
                Unreference(record);
                tablesChanged |= _reader.Read(record);
                Reference(record);
            }
            if (_membershipChanged)
            {
                _membershipChanged = false;
                _ordered.Clear();
                _ordered.AddRange(_records.Values);
                _ordered.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            }
            Changed = tablesChanged;
            if (tablesChanged)
                Pack();
            if (tablesChanged || contentChanged)
                Version++;

            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i])
                    accelerationStructure.UpdateInstanceID(_renderers[i], (uint)i);
            }
        }

        private bool Rescan()
        {
            ConsumeChanges();
            foreach (var record in _records.Values)
                Unreference(record);
            _records.Clear();
            _changedMaterials.Clear();
            _changedMeshes.Clear();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Evaluate(renderer);
            UnsavedRenderers.RemoveAll(renderer => !renderer);
            foreach (var renderer in UnsavedRenderers)
                Evaluate(renderer);
            _seenUnsavedVersion = _unsavedVersion;
            _membershipChanged = true;
            return true;
        }

        private bool ApplyChanges()
        {
            ConsumeChanges();
            if (_seenUnsavedVersion != _unsavedVersion)
            {
                _seenUnsavedVersion = _unsavedVersion;
                UnsavedRenderers.RemoveAll(renderer => !renderer);
                _reevaluate.Clear();
                _reevaluate.AddRange(UnsavedRenderers);
                foreach (var record in _records.Values)
                {
                    if (IsUnsaved(record.Renderer))
                        _reevaluate.Add(record.Renderer);
                }
                foreach (var renderer in _reevaluate)
                    Evaluate(renderer);
            }

            // Mesh data alone, such as cloth written every frame, re-reads its renderers but advances the version only through their tables.
            bool materialsChanged = _changedMaterials.Count > 0;
            if (!materialsChanged && _changedMeshes.Count == 0)
                return false;
            foreach (var record in _records.Values)
            {
                if (_changedMeshes.Contains(record.MeshId))
                {
                    record.Dirty = true;
                    continue;
                }
                foreach (var id in record.MaterialIds)
                {
                    if (!_changedMaterials.Contains(id))
                        continue;
                    record.Dirty = true;
                    break;
                }
            }
            _changedMaterials.Clear();
            _changedMeshes.Clear();
            return materialsChanged;
        }

        private void ConsumeChanges()
        {
            _destroyed.Clear();
            _tracker.ConsumeChanges<MeshRenderer>(_changed, _destroyed);
            foreach (var changed in _changed)
                Evaluate(changed as Renderer);
            _tracker.ConsumeChanges<SkinnedMeshRenderer>(_changed, _destroyed);
            foreach (var changed in _changed)
                Evaluate(changed as Renderer);
            foreach (var id in _destroyed)
            {
                if (_records.Remove(id, out var record))
                {
                    Unreference(record);
                    _membershipChanged = true;
                }
            }

            _tracker.ConsumeChanges<MeshFilter>(_changed, _destroyed);
            foreach (var changed in _changed)
            {
                if (changed is MeshFilter filter && filter.TryGetComponent(out MeshRenderer renderer))
                    Evaluate(renderer);
            }
            _tracker.ConsumeHierarchyChanges<MeshRenderer>(_changed);
            MarkDirty(_changed);
            _tracker.ConsumeHierarchyChanges<SkinnedMeshRenderer>(_changed);
            MarkDirty(_changed);

            _destroyed.Clear();
            _tracker.ConsumeChanges<Material>(_changed, _destroyed);
            foreach (var changed in _changed)
            {
                if (changed is not Material material || !material || !_materialReferences.ContainsKey(material.GetEntityId()))
                    continue;
                var id = material.GetEntityId();
                int crc = material.ComputeCRC();
                if (_materialCRCs.TryGetValue(id, out int previous) && previous == crc)
                    continue;
                _materialCRCs[id] = crc;
                _changedMaterials.Add(id);
            }
            foreach (var id in _destroyed)
                _materialCRCs.Remove(id);

            _tracker.ConsumeChanges<Mesh>(_changed, _destroyed);
            foreach (var changed in _changed)
            {
                if (changed && _meshReferences.ContainsKey(changed.GetEntityId()))
                    _changedMeshes.Add(changed.GetEntityId());
            }
        }

        private void Evaluate(Renderer renderer)
        {
            if (!renderer)
                return;
            var id = renderer.GetEntityId();
            bool participates = Participates(renderer, _layerMask);
            if (_records.TryGetValue(id, out var record))
            {
                if (participates)
                    record.Dirty = true;
                else
                {
                    _records.Remove(id);
                    Unreference(record);
                    _membershipChanged = true;
                }
            }
            else if (participates)
            {
                _records.Add(id, new PathTracingRendererRecord(renderer));
                _membershipChanged = true;
            }
        }

        private void MarkDirty(List<Object> renderers)
        {
            foreach (var changed in renderers)
            {
                if (changed && _records.TryGetValue(changed.GetEntityId(), out var record))
                    record.Dirty = true;
            }
        }

        private static bool Participates(Renderer renderer, int layerMask)
        {
            return renderer is MeshRenderer or SkinnedMeshRenderer && renderer.enabled && renderer.gameObject.activeInHierarchy
                && (layerMask & (1 << renderer.gameObject.layer)) != 0 && renderer.rayTracingMode != RayTracingMode.Off
                && (!IsUnsaved(renderer) || UnsavedRenderers.Contains(renderer));
        }

        // Change notifications include hidden objects, which scene scans skip; the pipeline registers the ones it creates itself.
        private static bool IsUnsaved(Renderer renderer)
        {
            return ((renderer.hideFlags | renderer.gameObject.hideFlags) & HideFlags.DontSave) != 0;
        }

        private void Reference(PathTracingRendererRecord record)
        {
            record.MaterialIds.Clear();
            foreach (var material in record.Materials)
            {
                if (!material)
                    continue;
                var id = material.GetEntityId();
                record.MaterialIds.Add(id);
                _materialReferences[id] = _materialReferences.GetValueOrDefault(id) + 1;
            }
            record.MeshId = record.Mesh ? record.Mesh.GetEntityId() : EntityId.None;
            if (record.Mesh)
                _meshReferences[record.MeshId] = _meshReferences.GetValueOrDefault(record.MeshId) + 1;
        }

        private void Unreference(PathTracingRendererRecord record)
        {
            foreach (var id in record.MaterialIds)
                Release(_materialReferences, id);
            record.MaterialIds.Clear();
            if (record.MeshId.IsValid())
                Release(_meshReferences, record.MeshId);
            record.MeshId = EntityId.None;
        }

        private static void Release(Dictionary<EntityId, int> references, EntityId id)
        {
            if (!references.TryGetValue(id, out int count))
                return;
            if (count > 1)
                references[id] = count - 1;
            else
                references.Remove(id);
        }

        private void Pack()
        {
            _renderers.Clear();
            _data.Clear();
            _culledSubMeshes.Clear();
            int hash = _ordered.Count;
            foreach (var record in _ordered)
            {
                _renderers.Add(record.Renderer);
                int culledStart = _culledSubMeshes.Count;
                int culledCount = record.CulledSubMeshes.Count;
                _culledSubMeshes.AddRange(record.CulledSubMeshes);
                _data.Add(new PathTracingInstanceData
                {
                    PreviousPositionBase = PathTracingInstanceData.NoPositionHistory,
                    CulledSubMeshes = culledCount > 0 ? (uint)culledStart << 8 | (uint)culledCount : 0u,
                    ScatteringGroup = record.ScatteringGroup,
                    RenderingLayers = record.RenderingLayers
                });
                hash = HashCode.Combine(hash, record.Hash);
            }
            MaterialTable.Pack(_ordered);
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

        public void Dispose()
        {
            _tracker.Dispose();
            MaterialTable.Dispose();
            _buffer?.Release();
            _buffer = null;
            _culledSubMeshBuffer?.Release();
            _culledSubMeshBuffer = null;
        }
    }
}
