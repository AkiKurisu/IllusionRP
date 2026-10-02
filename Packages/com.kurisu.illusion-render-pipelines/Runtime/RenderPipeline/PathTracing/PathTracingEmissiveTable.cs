using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingEmissiveTable : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceRange
        {
            public uint Offset;
            public uint Count;
        }

        private sealed class Batch
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public int RendererID;
            public int MeshID;
            public int InstanceIndex;
            public int SubMesh;
            public int IndexStart;
            public int IndexCount;
            public int BaseVertex;
            public int RecordBase;
            public int TriangleCount;
            public Vector4 Emission;
            public int TargetIndex;
            public Texture Texture;
            public Vector4 BaseMapST;
        }

        private readonly ComputeShader _shader;
        private readonly int _kernel;
        private readonly List<Material> _materials = new();
        private readonly MaterialPropertyBlock _rendererBlock = new();
        private readonly MaterialPropertyBlock _materialBlock = new();
        private readonly HashSet<int> _reportedContracts = new();
        private readonly List<GraphicsBuffer> _meshBuffers = new();
        private readonly GraphicsBuffer _emptyRaw = new(GraphicsBuffer.Target.Raw, 4, sizeof(uint));
        private List<Batch> _batches = new();
        private InstanceRange[] _ranges = Array.Empty<InstanceRange>();
        private uint[] _triangleToRecord = Array.Empty<uint>();
        private GraphicsBuffer _instanceBuffer;
        private GraphicsBuffer _lookupBuffer;
        private bool _uploadPending;
        private bool _initialized;

        public int Generation { get; private set; }
        public int TriangleCount { get; private set; }

        public PathTracingEmissiveTable(ComputeShader shader)
        {
            _shader = shader;
            _kernel = shader.FindKernel("BakeTriangleLights");
            _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
            _instanceBuffer.SetData(new InstanceRange[1]);
            _lookupBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            _lookupBuffer.SetData(new[] { uint.MaxValue });
        }

        public void Update(PathTracingInstanceTable instances)
        {
            ReleaseMeshBuffers();
            var next = new List<Batch>();
            int recordCount = 0;
            for (int instance = 0; instance < instances.Renderers.Count; instance++)
            {
                var renderer = instances.Renderers[instance];
                var mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (!mesh)
                    continue;
                renderer.GetSharedMaterials(_materials);
                renderer.GetPropertyBlock(_rendererBlock);
                for (int subMesh = 0; subMesh < _materials.Count; subMesh++)
                {
                    var material = _materials[subMesh];
                    if (!material)
                        continue;
                    renderer.GetPropertyBlock(_materialBlock, subMesh);
                    var block = _materialBlock.isEmpty ? _rendererBlock : _materialBlock;
                    if (!TryReadEmission(material, block, out var emission, out var texture, out var st))
                        continue;
                    if (subMesh >= mesh.subMeshCount)
                        throw new NotSupportedException($"[PathTracing] Emissive renderer '{renderer.name}' has more material slots than submeshes.");
                    var sub = mesh.GetSubMesh(subMesh);
                    if (sub.topology != MeshTopology.Triangles || sub.indexCount % 3 != 0 || sub.indexStart % 3 != 0)
                        throw new NotSupportedException($"[PathTracing] Emissive submesh '{renderer.name}/{subMesh}' requires triangle topology and triangle-aligned indices.");
                    ValidateLayout(renderer, mesh, texture != null);
                    if (sub.indexCount == 0)
                        continue;
                    next.Add(new Batch
                    {
                        Renderer = renderer, Mesh = mesh, RendererID = renderer.GetInstanceID(), MeshID = mesh.GetInstanceID(),
                        InstanceIndex = instance, SubMesh = subMesh, IndexStart = sub.indexStart, IndexCount = sub.indexCount,
                        BaseVertex = sub.baseVertex, RecordBase = recordCount, TriangleCount = sub.indexCount / 3,
                        Emission = emission, Texture = texture, BaseMapST = st,
                        TargetIndex = material.HasProperty(ShaderIDs.LightTarget) ? material.GetInt(ShaderIDs.LightTarget) : 0
                    });
                    recordCount = checked(recordCount + sub.indexCount / 3);
                    if (recordCount > PathTracingLightingConfig.MaxLights)
                        throw new InvalidOperationException("[PathTracing] Emissive triangles exceed RTXPT's light capacity.");
                }
            }

            bool sameTopology = _initialized && SameTopology(_batches, next);
            if (!sameTopology)
            {
                Generation++;
                _initialized = true;
            }
            _batches = next;
            TriangleCount = recordCount;
            BuildLookup(instances.Renderers.Count);
        }

        private bool TryReadEmission(Material material, MaterialPropertyBlock block,
            out Vector4 emission, out Texture texture, out Vector4 st)
        {
            emission = Vector4.zero;
            texture = null;
            st = new Vector4(1, 1, 0, 0);
            string contract = material.GetTag("PathTracingEmission", false, string.Empty);
            if (contract != "PBR" && contract != "Constant")
            {
                if (material.HasProperty(ShaderIDs.EmissionColor))
                {
                    Vector4 value = block.HasProperty(ShaderIDs.EmissionColor)
                        ? block.GetVector(ShaderIDs.EmissionColor) : material.GetVector(ShaderIDs.EmissionColor);
                    if ((value.x != 0 || value.y != 0 || value.z != 0) && _reportedContracts.Add(material.shader.GetInstanceID()))
                        Debug.LogWarning($"[PathTracing] Shader '{material.shader.name}' has a nonzero _EmissionColor but no reference emission contract; it is not included in triangle light sampling.", material);
                }
                return false;
            }
            if (contract == "PBR" && !material.IsKeywordEnabled("_EMISSION"))
                return false;
            if (!material.HasProperty(ShaderIDs.EmissionColor))
                throw new InvalidOperationException($"[PathTracing] Shader '{material.shader.name}' declares an emission contract without _EmissionColor.");
            emission = block.HasProperty(ShaderIDs.EmissionColor)
                ? block.GetVector(ShaderIDs.EmissionColor) : material.GetVector(ShaderIDs.EmissionColor);
            if (!float.IsFinite(emission.x) || !float.IsFinite(emission.y) || !float.IsFinite(emission.z))
                throw new InvalidOperationException($"[PathTracing] Non-finite emission on '{material.name}'.");
            if (Mathf.Max(emission.x, Mathf.Max(emission.y, emission.z)) <= 0)
                return false;
            if (contract == "Constant")
                return true;
            if (material.IsKeywordEnabled("_PARALLAXMAP"))
                throw new NotSupportedException($"[PathTracing] Emissive material '{material.name}' uses parallax UVs outside the reference emission contract.");
            texture = block.HasProperty(ShaderIDs.EmissionMap) ? block.GetTexture(ShaderIDs.EmissionMap)
                : material.HasProperty(ShaderIDs.EmissionMap) ? material.GetTexture(ShaderIDs.EmissionMap) : null;
            if (texture && texture.dimension != TextureDimension.Tex2D)
                throw new NotSupportedException($"[PathTracing] Emission map '{texture.name}' must be a 2D texture.");
            if (block.HasProperty(ShaderIDs.BaseMapST))
                st = block.GetVector(ShaderIDs.BaseMapST);
            else if (material.HasProperty(ShaderIDs.BaseMap))
            {
                Vector2 scale = material.GetTextureScale(ShaderIDs.BaseMap);
                Vector2 offset = material.GetTextureOffset(ShaderIDs.BaseMap);
                st = new Vector4(scale.x, scale.y, offset.x, offset.y);
            }
            if (!float.IsFinite(st.x) || !float.IsFinite(st.y) || !float.IsFinite(st.z) || !float.IsFinite(st.w))
                throw new InvalidOperationException($"[PathTracing] Non-finite emission UV transform on '{material.name}'.");
            return true;
        }

        private static void ValidateLayout(Renderer renderer, Mesh mesh, bool textured)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.Position)
                || mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                || mesh.GetVertexAttributeDimension(VertexAttribute.Position) < 3)
                throw new NotSupportedException($"[PathTracing] Emissive mesh '{mesh.name}' requires Float32 positions.");
            if (textured)
            {
                if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0)
                    || mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) < 2)
                    throw new NotSupportedException($"[PathTracing] Textured emissive mesh '{mesh.name}' requires UV0.");
                var format = mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0);
                if (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16)
                    throw new NotSupportedException($"[PathTracing] Emissive mesh '{mesh.name}' has unsupported UV0 format {format}.");
            }
            if ((mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            if ((mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            if (renderer is SkinnedMeshRenderer skinned && (skinned.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                skinned.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        }

        private static bool SameTopology(List<Batch> previous, List<Batch> current)
        {
            if (previous.Count != current.Count)
                return false;
            for (int i = 0; i < current.Count; i++)
            {
                var a = previous[i];
                var b = current[i];
                if (a.RendererID != b.RendererID || a.MeshID != b.MeshID || a.SubMesh != b.SubMesh
                    || a.IndexStart != b.IndexStart || a.IndexCount != b.IndexCount || a.BaseVertex != b.BaseVertex)
                    return false;
            }
            return true;
        }

        private void BuildLookup(int instanceCount)
        {
            var ranges = new InstanceRange[instanceCount];
            int total = 0;
            foreach (var batch in _batches)
            {
                if (ranges[batch.InstanceIndex].Count != 0)
                    continue;
                int span = 0;
                var used = new List<Vector2Int>();
                for (int s = 0; s < batch.Mesh.subMeshCount; s++)
                {
                    var sub = batch.Mesh.GetSubMesh(s);
                    if (sub.topology != MeshTopology.Triangles || sub.indexCount == 0)
                        continue;
                    if (sub.indexStart % 3 != 0 || sub.indexCount % 3 != 0)
                        throw new NotSupportedException($"[PathTracing] Mesh '{batch.Mesh.name}' has non-aligned triangle ranges.");
                    int start = sub.indexStart / 3;
                    int end = checked(start + sub.indexCount / 3);
                    foreach (var range in used)
                        if (start < range.y && end > range.x)
                            throw new NotSupportedException($"[PathTracing] Mesh '{batch.Mesh.name}' has overlapping submesh triangle identities.");
                    used.Add(new Vector2Int(start, end));
                    span = Mathf.Max(span, end);
                }
                ranges[batch.InstanceIndex] = new InstanceRange { Offset = (uint)total, Count = (uint)span };
                total = checked(total + span);
            }
            var lookup = new uint[total];
            Array.Fill(lookup, uint.MaxValue);
            foreach (var batch in _batches)
            {
                int start = checked((int)ranges[batch.InstanceIndex].Offset + batch.IndexStart / 3);
                for (int t = 0; t < batch.TriangleCount; t++)
                    lookup[start + t] = (uint)(batch.RecordBase + t);
            }
            bool changed = ranges.Length != _ranges.Length || lookup.Length != _triangleToRecord.Length;
            if (!changed)
            {
                for (int i = 0; i < ranges.Length && !changed; i++)
                    changed = ranges[i].Offset != _ranges[i].Offset || ranges[i].Count != _ranges[i].Count;
                for (int i = 0; i < lookup.Length && !changed; i++)
                    changed = lookup[i] != _triangleToRecord[i];
            }
            if (changed)
            {
                _ranges = ranges;
                _triangleToRecord = lookup;
                _uploadPending = true;
            }
        }

        public void Prepare(CommandBuffer cmd)
        {
            if (!_uploadPending)
                return;
            EnsureBuffer(ref _instanceBuffer, _ranges.Length, 8, "PathTracingEmissiveInstances");
            EnsureBuffer(ref _lookupBuffer, _triangleToRecord.Length, sizeof(uint), "PathTracingTriangleToEmissive");
            if (_ranges.Length > 0)
                cmd.SetBufferData(_instanceBuffer, _ranges);
            if (_triangleToRecord.Length > 0)
                cmd.SetBufferData(_lookupBuffer, _triangleToRecord);
            _uploadPending = false;
        }

        private static void EnsureBuffer(ref GraphicsBuffer buffer, int count, int stride, string name)
        {
            count = Mathf.Max(1, count);
            if (buffer != null && buffer.count >= count)
                return;
            buffer?.Release();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(count), stride) { name = name };
        }

        public void WriteLights(CommandBuffer cmd, GraphicsBuffer lights, GraphicsBuffer lightsEx, int baseIndex, int count)
        {
            if (count != TriangleCount)
                throw new InvalidOperationException($"[PathTracing] Light buffer can hold {count} of {TriangleCount} emissive triangles; refusing a truncated reference table.");
            cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.Lights, lights);
            cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.LightsEx, lightsEx);
            foreach (var batch in _batches)
            {
                var mesh = batch.Mesh;
                GraphicsBuffer positions;
                int positionStride, positionOffset;
                if (batch.Renderer is SkinnedMeshRenderer skinned)
                {
                    positions = skinned.GetVertexBuffer();
                    if (positions == null || positions.stride < 12 || positions.count < mesh.vertexCount)
                    {
                        positions?.Dispose();
                        throw new InvalidOperationException($"[PathTracing] Current skinned GPU positions are unavailable for emissive renderer '{skinned.name}'.");
                    }
                    positionStride = positions.stride;
                    positionOffset = 0;
                }
                else
                {
                    int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                    positions = mesh.GetVertexBuffer(stream);
                    positionStride = mesh.GetVertexBufferStride(stream);
                    positionOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                }
                if (positions == null)
                    throw new InvalidOperationException($"[PathTracing] GPU positions are unavailable for emissive mesh '{mesh.name}'.");
                _meshBuffers.Add(positions);
                var indices = mesh.GetIndexBuffer();
                if (indices == null)
                    throw new InvalidOperationException($"[PathTracing] GPU indices are unavailable for emissive mesh '{mesh.name}'.");
                _meshBuffers.Add(indices);
                var uvs = _emptyRaw;
                int uvStride = 0, uvOffset = 0, uvHalf = 0;
                if (batch.Texture)
                {
                    int stream = mesh.GetVertexAttributeStream(VertexAttribute.TexCoord0);
                    uvs = mesh.GetVertexBuffer(stream);
                    if (uvs == null)
                        throw new InvalidOperationException($"[PathTracing] GPU UV0 is unavailable for emissive mesh '{mesh.name}'.");
                    _meshBuffers.Add(uvs);
                    uvStride = mesh.GetVertexBufferStride(stream);
                    uvOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
                    uvHalf = mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0) == VertexAttributeFormat.Float16 ? 1 : 0;
                }
                if ((positionStride & 3) != 0 || (positionOffset & 3) != 0 || (uvStride & 3) != 0 || (uvOffset & 3) != 0)
                    throw new NotSupportedException($"[PathTracing] Mesh '{mesh.name}' has unaligned raw vertex attributes.");
                cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.Indices, indices);
                cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.Positions, positions);
                cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.UVs, uvs);
                cmd.SetComputeIntParam(_shader, ShaderIDs.IndexSize, mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2);
                cmd.SetComputeIntParam(_shader, ShaderIDs.IndexStart, batch.IndexStart);
                cmd.SetComputeIntParam(_shader, ShaderIDs.BaseVertex, batch.BaseVertex);
                cmd.SetComputeIntParam(_shader, ShaderIDs.PositionStride, positionStride);
                cmd.SetComputeIntParam(_shader, ShaderIDs.PositionOffset, positionOffset);
                cmd.SetComputeIntParam(_shader, ShaderIDs.UVStride, uvStride);
                cmd.SetComputeIntParam(_shader, ShaderIDs.UVOffset, uvOffset);
                cmd.SetComputeIntParam(_shader, ShaderIDs.UVHalf, uvHalf);
                cmd.SetComputeIntParam(_shader, ShaderIDs.LightTarget, batch.TargetIndex);
                cmd.SetComputeIntParam(_shader, ShaderIDs.UseTexture, batch.Texture ? 1 : 0);
                cmd.SetComputeTextureParam(_shader, _kernel, ShaderIDs.EmissionMap, batch.Texture ? batch.Texture : Texture2D.whiteTexture);
                cmd.SetComputeVectorParam(_shader, ShaderIDs.EmissiveColor, batch.Emission);
                cmd.SetComputeVectorParam(_shader, ShaderIDs.EmissiveST, batch.BaseMapST);
                var matrix = batch.Renderer.localToWorldMatrix;
                cmd.SetComputeVectorParam(_shader, ShaderIDs.Transform0, matrix.GetRow(0));
                cmd.SetComputeVectorParam(_shader, ShaderIDs.Transform1, matrix.GetRow(1));
                cmd.SetComputeVectorParam(_shader, ShaderIDs.Transform2, matrix.GetRow(2));
                cmd.SetComputeIntParam(_shader, ShaderIDs.TriangleCount, batch.TriangleCount);
                cmd.SetComputeIntParam(_shader, ShaderIDs.DestinationBase, baseIndex + batch.RecordBase);
                cmd.SetComputeIntParam(_shader, ShaderIDs.InstanceIndex, batch.InstanceIndex);
                cmd.SetComputeIntParam(_shader, ShaderIDs.GeometryIndex, batch.SubMesh);
                cmd.DispatchCompute(_shader, _kernel, (batch.TriangleCount + 255) / 256, 1, 1);
            }
        }

        public void Bind(CommandBuffer cmd, RayTracingShader shader)
        {
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.Instances, _instanceBuffer);
            cmd.SetRayTracingBufferParam(shader, ShaderIDs.TriangleToRecord, _lookupBuffer);
            cmd.SetRayTracingIntParam(shader, ShaderIDs.InstanceCount, _ranges.Length);
        }

        private void ReleaseMeshBuffers()
        {
            foreach (var buffer in _meshBuffers)
                buffer.Dispose();
            _meshBuffers.Clear();
        }

        public void Dispose()
        {
            ReleaseMeshBuffers();
            _instanceBuffer?.Release();
            _lookupBuffer?.Release();
            _emptyRaw.Release();
        }

        private static class ShaderIDs
        {
            public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
            public static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
            public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
            public static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
            public static readonly int Instances = Shader.PropertyToID("t_IllusionEmissiveInstances");
            public static readonly int TriangleToRecord = Shader.PropertyToID("t_IllusionEmissiveTriangleToRecord");
            public static readonly int InstanceCount = Shader.PropertyToID("_IllusionEmissiveInstanceCount");
            public static readonly int Indices = Shader.PropertyToID("t_EmissiveIndices");
            public static readonly int Positions = Shader.PropertyToID("t_EmissivePositions");
            public static readonly int UVs = Shader.PropertyToID("t_EmissiveUVs");
            public static readonly int IndexSize = Shader.PropertyToID("_EmissiveIndexSize");
            public static readonly int IndexStart = Shader.PropertyToID("_EmissiveIndexStart");
            public static readonly int BaseVertex = Shader.PropertyToID("_EmissiveBaseVertex");
            public static readonly int PositionStride = Shader.PropertyToID("_EmissivePositionStride");
            public static readonly int PositionOffset = Shader.PropertyToID("_EmissivePositionOffset");
            public static readonly int UVStride = Shader.PropertyToID("_EmissiveUVStride");
            public static readonly int UVOffset = Shader.PropertyToID("_EmissiveUVOffset");
            public static readonly int UVHalf = Shader.PropertyToID("_EmissiveUVHalf");
            public static readonly int UseTexture = Shader.PropertyToID("_EmissiveUseTexture");
            public static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
            public static readonly int EmissiveST = Shader.PropertyToID("_EmissiveBaseMapST");
            public static readonly int Transform0 = Shader.PropertyToID("_EmissiveTransform0");
            public static readonly int Transform1 = Shader.PropertyToID("_EmissiveTransform1");
            public static readonly int Transform2 = Shader.PropertyToID("_EmissiveTransform2");
            public static readonly int TriangleCount = Shader.PropertyToID("_EmissiveTriangleCount");
            public static readonly int DestinationBase = Shader.PropertyToID("_EmissiveDestinationBase");
            public static readonly int LightTarget = Shader.PropertyToID("_PathTracingLightTarget");
            public static readonly int InstanceIndex = Shader.PropertyToID("_EmissiveInstanceIndex");
            public static readonly int GeometryIndex = Shader.PropertyToID("_EmissiveGeometryIndex");
            public static readonly int Lights = Shader.PropertyToID("u_lightsBuffer");
            public static readonly int LightsEx = Shader.PropertyToID("u_lightsExBuffer");
        }
    }
}
