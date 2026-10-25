using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    // Must match PathTracingInlineGeometry in PathTracingInline.hlsl.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PathTracingInlineGeometry
    {
        public fixed uint Attributes[12];
        public fixed uint VertexSize[4];
        public fixed uint VertexBuffers[4];
        public uint IndexBuffer;
        public uint IndexSize;
        public uint IndexStart;
        public uint BaseVertex;
        public uint VertexStart;
        public uint MaterialData;
        public uint Evaluator;
        public uint Flags;
    }

    internal sealed class PathTracingInlineScene : IDisposable
    {
        public const uint NoGeometry = 0xFFFFFFFF;

        private const int AttributeCount = 12;

        private readonly List<PathTracingInlineGeometry> _geometries = new();
        private readonly List<uint> _instanceGeometries = new();
        private readonly List<uint> _materialData = new();
        private readonly Dictionary<(int Material, int Block, int Evaluator), uint> _materialRecords = new();
        private readonly HashSet<int> _textures = new();
        private readonly HashSet<int> _meshes = new();
        private readonly HashSet<int> _previousTextures = new();
        private readonly HashSet<int> _previousMeshes = new();
        private readonly List<VertexAttributeDescriptor> _attributes = new();
        private readonly MaterialPropertyBlock _rendererBlock = new();
        private readonly MaterialPropertyBlock _slotBlock = new();
        private readonly List<DeformingRenderer> _deforming = new();
        private readonly Dictionary<int, DeformingRenderer> _previousDeforming = new();
        private readonly PathTracingOpacityMicromaps _micromaps = PathTracingOpacityMicromaps.Shared;

        private GraphicsBuffer _geometryBuffer;
        private GraphicsBuffer _instanceBuffer;
        private GraphicsBuffer _materialBuffer;
        private GraphicsBuffer _placeholderRaw;
        private GraphicsBuffer _placeholderStructured;
        private int _version = -1;
        private int _generation = -1;
        private int _deformFrame = -1;
        private int _firstDeformingGeometry;
        private readonly List<GraphicsBuffer> _deformedSources = new();
        private PathTracingInlineRegistry _registry;
        private string _modulesHash;

        public int UncoveredInstances { get; private set; }

        public int EvaluatedGeometries { get; private set; }

        public int ExceptionGeometries { get; private set; }

        public bool IsReady => _geometryBuffer != null && _registry;

        public void Update(PathTracingInstanceTable instances, int frame)
        {
            PathTracingInlineRegistry registry = PathTracingInlineRegistry.Instance;
            if (!registry || !PathTracingInlineResources.IsAvailable)
                return;
            PathTracingInlineResources.BeginFrame(frame);
            _micromaps.CheckCoverageTextures();
            if (_version != instances.Version || _generation != PathTracingInlineResources.Generation || _registry != registry || _modulesHash != registry.ModulesHash)
            {
                _version = instances.Version;
                _registry = registry;
                _modulesHash = registry.ModulesHash;
                _micromaps.BeginRebuild();
                Rebuild(instances, registry, frame);
                _micromaps.EndRebuild();
                _generation = PathTracingInlineResources.Generation;
                Upload();
            }
        }

        public unsafe void CopyDeformedVertices(CommandBuffer cmd, int frame, ComputeShader copyShader)
        {
            if (_deformFrame == frame || _geometryBuffer == null)
                return;
            _deformFrame = frame;
            foreach (GraphicsBuffer source in _deformedSources)
                source.Dispose();
            _deformedSources.Clear();

            bool changed = false;
            foreach (DeformingRenderer deforming in _deforming)
            {
                GraphicsBuffer source = null;
                if (deforming.Renderer && deforming.Vertices != null)
                {
                    if ((deforming.Renderer.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                        deforming.Renderer.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                    else
                        source = deforming.Renderer.GetVertexBuffer();
                }
                bool deformed = source != null && source.count * source.stride == deforming.Vertices.count * deforming.Vertices.stride;
                if (deformed)
                {
                    int words = deforming.Vertices.count;
                    int kernel = copyShader.FindKernel("CopyVertices");
                    cmd.SetComputeBufferParam(copyShader, kernel, ShaderIDs.t_Vertices, source);
                    cmd.SetComputeBufferParam(copyShader, kernel, ShaderIDs.u_Positions, deforming.Vertices);
                    cmd.SetComputeIntParam(copyShader, ShaderIDs._WordCount, words);
                    cmd.DispatchCompute(copyShader, kernel, (words + 255) / 256, 1, 1);
                    _deformedSources.Add(source);
                }
                else
                {
                    source?.Dispose();
                }
                if (deformed == deforming.Deformed)
                    continue;
                deforming.Deformed = deformed;
                uint slot = deformed ? (uint)deforming.Slot : deforming.MeshSlot;
                for (int i = deforming.FirstGeometry; i < deforming.FirstGeometry + deforming.GeometryCount; i++)
                {
                    PathTracingInlineGeometry geometry = _geometries[i];
                    geometry.VertexBuffers[0] = slot;
                    _geometries[i] = geometry;
                }
                changed = true;
            }
            if (changed)
                _geometryBuffer.SetData(_geometries, _firstDeformingGeometry, _firstDeformingGeometry, _geometries.Count - _firstDeformingGeometry);
        }

        private void Rebuild(PathTracingInstanceTable instances, PathTracingInlineRegistry registry, int frame)
        {
            _geometries.Clear();
            _instanceGeometries.Clear();
            _materialData.Clear();
            _materialRecords.Clear();
            _previousTextures.Clear();
            _previousTextures.UnionWith(_textures);
            _previousMeshes.Clear();
            _previousMeshes.UnionWith(_meshes);
            _textures.Clear();
            _meshes.Clear();
            _previousDeforming.Clear();
            foreach (DeformingRenderer deforming in _deforming)
                _previousDeforming[deforming.InstanceId] = deforming;
            _deforming.Clear();
            UncoveredInstances = 0;
            EvaluatedGeometries = 0;
            ExceptionGeometries = 0;

            // Deforming geometry follows the static geometry so that its per-frame updates upload one range.
            IReadOnlyList<PathTracingRendererRecord> records = instances.Records;
            for (int i = 0; i < records.Count; i++)
                _instanceGeometries.Add(records[i].Renderer is SkinnedMeshRenderer ? NoGeometry : AddRenderer(records[i], registry, frame));
            _firstDeformingGeometry = _geometries.Count;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Renderer is not SkinnedMeshRenderer skinned)
                    continue;
                int first = _geometries.Count;
                _instanceGeometries[i] = AddRenderer(records[i], registry, frame);
                if (_geometries.Count == first)
                    continue;
                int id = skinned.GetInstanceID();
                if (!_previousDeforming.Remove(id, out DeformingRenderer deforming))
                    deforming = new DeformingRenderer { InstanceId = id };
                deforming.Renderer = skinned;
                deforming.Mesh = records[i].Mesh;
                deforming.FirstGeometry = first;
                deforming.GeometryCount = _geometries.Count - first;
                deforming.MeshSlot = Stream0(_geometries[first]);
                deforming.Deformed = false;
                if (deforming.Ensure(records[i].Mesh, frame))
                    _deforming.Add(deforming);
            }

            foreach (DeformingRenderer deforming in _previousDeforming.Values)
                deforming.Release(frame);
            _previousDeforming.Clear();
            foreach (int texture in _previousTextures)
                PathTracingInlineResources.ReleaseTexture(texture, frame);
            foreach (int mesh in _previousMeshes)
                PathTracingInlineResources.ReleaseMesh(mesh, frame);
        }

        private uint AddRenderer(PathTracingRendererRecord record, PathTracingInlineRegistry registry, int frame)
        {
            Renderer renderer = record.Renderer;
            Mesh mesh = record.Mesh;
            if (!renderer || !mesh)
            {
                UncoveredInstances++;
                return NoGeometry;
            }
            int meshId = mesh.GetInstanceID();
            bool readable = PathTracingInlineResources.AcquireMesh(mesh, frame, out int[] vertexSlots, out int indexSlot);
            if (!_meshes.Add(meshId))
                PathTracingInlineResources.ReleaseMesh(meshId, frame);
            if (!readable)
            {
                UncoveredInstances++;
                return NoGeometry;
            }

            uint first = (uint)_geometries.Count;
            var template = Template(mesh, vertexSlots, indexSlot);
            bool hasBlock = renderer.HasPropertyBlock();
            if (hasBlock)
                renderer.GetPropertyBlock(_rendererBlock);
            int subMeshes = Mathf.Min(mesh.subMeshCount, record.Materials.Count);
            for (int slot = 0; slot < subMeshes; slot++)
            {
                Material material = record.Materials[slot];
                if (!material)
                    continue;
                SubMeshDescriptor subMesh = mesh.GetSubMesh(slot);
                var geometry = template;
                geometry.IndexStart = (uint)subMesh.indexStart;
                geometry.BaseVertex = (uint)subMesh.baseVertex;
                geometry.VertexStart = (uint)subMesh.firstVertex;

                int evaluator = registry.FindEvaluator(material);
                if (evaluator == PathTracingInlineRegistry.NoEvaluator)
                {
                    geometry.Evaluator = NoGeometry;
                    ExceptionGeometries++;
                    ReportException(material);
                }
                else
                {
                    MaterialPropertyBlock block = null;
                    int blockKey = 0;
                    if (hasBlock)
                    {
                        renderer.GetPropertyBlock(_slotBlock, slot);
                        block = _slotBlock.isEmpty ? _rendererBlock : _slotBlock;
                        blockKey = _slotBlock.isEmpty ? record.InstanceId : record.InstanceId * 31 + slot + 1;
                    }
                    geometry.Evaluator = (uint)evaluator;
                    geometry.MaterialData = MaterialRecord(registry.Evaluators[evaluator], evaluator, material, block, blockKey, frame);
                    EvaluatedGeometries++;
                    if (renderer is MeshRenderer meshRenderer)
                        _micromaps.Collect(this, meshRenderer, mesh, slot, _geometries.Count, registry.Evaluators[evaluator].StandardCoverage, geometry.MaterialData, _materialData);
                }
                _geometries.Add(geometry);
            }
            return first;
        }

        private static unsafe uint Stream0(PathTracingInlineGeometry geometry) => geometry.VertexBuffers[0];

        private sealed class DeformingRenderer
        {
            public int InstanceId;
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
            public int FirstGeometry;
            public int GeometryCount;
            public uint MeshSlot;
            public bool Deformed;
            public GraphicsBuffer Vertices;
            public int Slot = -1;

            public bool Ensure(Mesh mesh, int frame)
            {
                int size = mesh.vertexCount * mesh.GetVertexBufferStride(0);
                if (Vertices != null && Vertices.count * Vertices.stride == size)
                    return Slot >= 0;
                Release(frame);
                if (size <= 0 || size % sizeof(uint) != 0)
                    return false;
                Vertices = new GraphicsBuffer(GraphicsBuffer.Target.Raw, size / sizeof(uint), sizeof(uint))
                {
                    name = "PathTracingInlineDeformedVertices"
                };
                Slot = PathTracingInlineResources.AcquireBuffer(Vertices.GetNativeBufferPtr(), (uint)size, frame);
                return Slot >= 0;
            }

            public void Release(int frame)
            {
                if (Slot >= 0)
                    PathTracingInlineResources.ReleaseBuffer(Slot, frame);
                Slot = -1;
                Vertices?.Release();
                Vertices = null;
            }
        }

        private static readonly HashSet<(int, string)> s_ReportedExceptions = new();

        private static void ReportException(Material material)
        {
            if (!material.shader || material.FindPass(PathTracingPass.MaterialPassName) < 0)
                return;
            string keywords = string.Join(" ", material.shaderKeywords);
            if (!s_ReportedExceptions.Add((material.shader.GetInstanceID(), keywords)))
                return;
            IllusionLog.Warning($"Inline Ray Tracing: '{material.shader.name}' ({keywords}) has no inline evaluator and renders as the diagnostic surface in inline ray tracing.", material);
        }

        private unsafe PathTracingInlineGeometry Template(Mesh mesh, int[] vertexSlots, int indexSlot)
        {
            var geometry = new PathTracingInlineGeometry
            {
                IndexBuffer = (uint)Mathf.Max(0, indexSlot),
                IndexSize = mesh.indexFormat == IndexFormat.UInt16 ? 2u : 4u
            };
            for (int stream = 0; stream < 4; stream++)
            {
                geometry.VertexBuffers[stream] = (uint)Mathf.Max(0, vertexSlots[stream]);
                geometry.VertexSize[stream] = stream < mesh.vertexBufferCount ? (uint)mesh.GetVertexBufferStride(stream) : 0u;
            }
            mesh.GetVertexAttributes(_attributes);
            foreach (VertexAttributeDescriptor attribute in _attributes)
            {
                int index = (int)attribute.attribute;
                if (index >= AttributeCount || attribute.stream > 3 || attribute.dimension > 4)
                    continue;
                uint offset = (uint)mesh.GetVertexAttributeOffset(attribute.attribute);
                geometry.Attributes[index] = (uint)attribute.dimension | (uint)attribute.format << 3 | (uint)attribute.stream << 7 | offset << 9;
            }
            return geometry;
        }

        private uint MaterialRecord(PathTracingInlineRegistry.Evaluator evaluator, int evaluatorIndex, Material material,
            MaterialPropertyBlock block, int blockKey, int frame)
        {
            var key = (material.GetInstanceID(), blockKey, evaluatorIndex);
            if (_materialRecords.TryGetValue(key, out uint offset))
                return offset;

            offset = (uint)_materialData.Count * 4u;
            _materialRecords.Add(key, offset);
            int words = (evaluator.RecordSize + 3) / 4;
            int start = _materialData.Count;
            for (int i = 0; i < words; i++)
                _materialData.Add(0u);

            Shader shader = material.shader;
            for (int t = 0; t < evaluator.Textures.Length; t++)
            {
                PathTracingInlineRegistry.Texture texture = evaluator.Textures[t];
                int id = Shader.PropertyToID(texture.Name);
                Texture value = null;
                if (shader.FindPropertyIndex(texture.Name) >= 0)
                    value = block != null && block.HasTexture(id) ? block.GetTexture(id) : material.GetTexture(id);
                int slot = PathTracingInlineResources.AcquireTexture(value, frame);
                if (slot >= 0)
                {
                    if (!_textures.Add(value.GetInstanceID()))
                        PathTracingInlineResources.ReleaseTexture(value, frame);
                }
                else
                {
                    slot = PathTracingInlineResources.DefaultSlot(texture.Default);
                }
                _materialData[start + t] = (uint)slot | SamplerState(value) << 24;
            }

            foreach (PathTracingInlineRegistry.Constant constant in evaluator.Constants)
            {
                if (constant.Components == 0)
                    continue;
                int count = Mathf.Max(1, (int)constant.Count);
                for (int element = 0; element < count; element++)
                {
                    Vector4 value = ReadConstant(material, block, constant.Name, shader);
                    int word = start + (constant.Offset + element * 16) / 4;
                    for (int c = 0; c < constant.Components; c++)
                        _materialData[word + c] = Pack(value[c], constant.Kind);
                }
            }
            return offset;
        }

        private static uint SamplerState(Texture texture)
        {
            if (!texture)
                return 2u;
            bool anisotropic = QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable
                || QualitySettings.anisotropicFiltering == AnisotropicFiltering.Enable && texture.anisoLevel > 1;
            uint filter = texture.filterMode switch
            {
                FilterMode.Point => 0u,
                _ when anisotropic => 3u,
                FilterMode.Bilinear => 1u,
                _ => 2u
            };
            uint wrap = texture.wrapModeU switch
            {
                TextureWrapMode.Clamp => 1u,
                TextureWrapMode.Mirror or TextureWrapMode.MirrorOnce => 2u,
                _ => 0u
            };
            return filter | wrap << 2;
        }

        private static uint Pack(float value, byte kind)
        {
            return kind switch
            {
                1 => unchecked((uint)Mathf.RoundToInt(value)),
                2 => (uint)Mathf.Max(0, Mathf.RoundToInt(value)),
                3 => value != 0f ? 1u : 0u,
                _ => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0)
            };
        }

        private Vector4 ReadConstant(Material material, MaterialPropertyBlock block, string name, Shader shader)
        {
            int id = Shader.PropertyToID(name);
            int index = shader.FindPropertyIndex(name);
            if (index >= 0)
            {
                switch (shader.GetPropertyType(index))
                {
                    case ShaderPropertyType.Color:
                    {
                        Color color = block != null && block.HasColor(id) ? block.GetColor(id) : material.GetColor(id);
                        bool hdr = (shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0;
                        return QualitySettings.activeColorSpace == ColorSpace.Linear && !hdr ? color.linear : color;
                    }
                    case ShaderPropertyType.Vector:
                        return block != null && block.HasVector(id) ? block.GetVector(id) : material.GetVector(id);
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        return new Vector4(block != null && block.HasFloat(id) ? block.GetFloat(id) : material.GetFloat(id), 0, 0, 0);
                    case ShaderPropertyType.Int:
                        return new Vector4(block != null && block.HasInteger(id) ? block.GetInteger(id) : material.GetInteger(id), 0, 0, 0);
                }
            }

            if (name.EndsWith("_ST", StringComparison.Ordinal))
            {
                if (block != null && block.HasVector(id))
                    return block.GetVector(id);
                string textureName = name.Substring(0, name.Length - 3);
                if (shader.FindPropertyIndex(textureName) >= 0)
                {
                    Vector2 scale = material.GetTextureScale(textureName);
                    Vector2 offset = material.GetTextureOffset(textureName);
                    return new Vector4(scale.x, scale.y, offset.x, offset.y);
                }
                return new Vector4(1, 1, 0, 0);
            }
            if (name.EndsWith("_TexelSize", StringComparison.Ordinal))
            {
                string textureName = name.Substring(0, name.Length - "_TexelSize".Length);
                Texture texture = shader.FindPropertyIndex(textureName) >= 0 ? material.GetTexture(textureName) : null;
                return texture ? new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height) : new Vector4(1, 1, 1, 1);
            }
            if (name.EndsWith("_HDR", StringComparison.Ordinal))
                return new Vector4(1, 1, 0, 0);
            if (material.HasVector(id))
                return material.GetVector(id);
            if (material.HasFloat(id))
                return new Vector4(material.GetFloat(id), 0, 0, 0);
            if (material.HasInteger(id))
                return new Vector4(material.GetInteger(id), 0, 0, 0);
            return Vector4.zero;
        }

        private void Upload()
        {
            Ensure(ref _geometryBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, _geometries.Count), Marshal.SizeOf<PathTracingInlineGeometry>(), "PathTracingInlineGeometries");
            Ensure(ref _instanceBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, _instanceGeometries.Count), 4, "PathTracingInlineInstanceGeometries");
            Ensure(ref _materialBuffer, GraphicsBuffer.Target.Raw, Mathf.Max(4, _materialData.Count), 4, "PathTracingInlineMaterialData");
            if (_geometries.Count > 0)
                _geometryBuffer.SetData(_geometries);
            if (_instanceGeometries.Count > 0)
                _instanceBuffer.SetData(_instanceGeometries);
            if (_materialData.Count > 0)
                _materialBuffer.SetData(_materialData);
            _placeholderRaw ??= new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, 4) { name = "PathTracingInlinePlaceholderRaw" };
            _placeholderStructured ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16) { name = "PathTracingInlinePlaceholder" };
        }

        private static void Ensure(ref GraphicsBuffer buffer, GraphicsBuffer.Target target, int count, int stride, string name)
        {
            if (buffer != null && buffer.count >= count)
                return;
            buffer?.Release();
            buffer = new GraphicsBuffer(target, Mathf.NextPowerOfTwo(count), stride) { name = name };
        }

        public void Bind(CommandBuffer cmd, PathTracingShaderTarget shader)
        {
            for (int i = 0; i < _registry.Buffers.Length; i++)
                shader.SetBuffer(cmd, Shader.PropertyToID(_registry.Buffers[i]), _registry.RawBuffers[i] ? _placeholderRaw : _placeholderStructured);
            shader.SetBuffer(cmd, ShaderIDs._PathTracingInlineGeometries, _geometryBuffer);
            shader.SetBuffer(cmd, ShaderIDs._PathTracingInlineInstanceGeometries, _instanceBuffer);
            shader.SetBuffer(cmd, ShaderIDs._PathTracingInlineMaterialData, _materialBuffer);
            shader.SetTexture(cmd, ShaderIDs._PathTracingInlineTextures, Texture2D.blackTexture);
            shader.SetBuffer(cmd, ShaderIDs._PathTracingInlineBuffers, _placeholderRaw);
        }

        public void RecordOpacityMicromaps(CommandBuffer cmd, ComputeShader bake)
        {
            if (IsReady)
                _micromaps.Record(cmd, bake, this);
        }

        public void Dispose()
        {
            _micromaps.Forget(this);
            int frame = PathTracingFrame.Index;
            foreach (int texture in _textures)
                PathTracingInlineResources.ReleaseTexture(texture, frame);
            foreach (int mesh in _meshes)
                PathTracingInlineResources.ReleaseMesh(mesh, frame);
            foreach (DeformingRenderer deforming in _deforming)
                deforming.Release(frame);
            foreach (GraphicsBuffer source in _deformedSources)
                source.Dispose();
            _deformedSources.Clear();
            _textures.Clear();
            _meshes.Clear();
            _deforming.Clear();
            _geometryBuffer?.Release();
            _instanceBuffer?.Release();
            _materialBuffer?.Release();
            _placeholderRaw?.Release();
            _placeholderStructured?.Release();
            _geometryBuffer = _instanceBuffer = _materialBuffer = _placeholderRaw = _placeholderStructured = null;
        }

        private static class ShaderIDs
        {
            public static readonly int _PathTracingInlineGeometries = Shader.PropertyToID("_PathTracingInlineGeometries");
            public static readonly int _PathTracingInlineInstanceGeometries = Shader.PropertyToID("_PathTracingInlineInstanceGeometries");
            public static readonly int _PathTracingInlineMaterialData = Shader.PropertyToID("_PathTracingInlineMaterialData");
            public static readonly int _PathTracingInlineTextures = Shader.PropertyToID("_PathTracingInlineTextures");
            public static readonly int _PathTracingInlineBuffers = Shader.PropertyToID("_PathTracingInlineBuffers");
            public static readonly int t_Vertices = Shader.PropertyToID("t_Vertices");
            public static readonly int u_Positions = Shader.PropertyToID("u_Positions");
            public static readonly int _WordCount = Shader.PropertyToID("_WordCount");
        }
    }
}
