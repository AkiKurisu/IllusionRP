using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingMaterialData
    {
        public Vector3 AttenuationColor;
        public float AttenuationDistance;
        public float IoR;
        public uint Flags;
        public uint Padding0;
        public uint Padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingMaterialRange
    {
        public uint Start;
        public uint Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTracingSubMeshMaterial
    {
        public uint IndexStart;
        public uint MaterialIndex;
    }

    internal sealed class PathTracingMaterialTable : IDisposable
    {
        private const int MaxMaterials = 32768;
        private const uint DefaultFlags = 0x00000400u | (14u << 28); // @IllusionRP: PSDExclude=true, NestedPriority=14.
        private static readonly int MaterialBufferId = Shader.PropertyToID("_PathTracingMaterials");
        private static readonly int MaterialCountId = Shader.PropertyToID("_PathTracingMaterialCount");
        private readonly List<PathTracingMaterialData> _data = new();
        private readonly List<PathTracingMaterialRange> _ranges = new();
        private readonly List<PathTracingSubMeshMaterial> _subMeshes = new();
        private readonly Dictionary<(int Material, int Renderer, int Slot), uint> _indices = new();
        private GraphicsBuffer _buffer;
        private GraphicsBuffer _rangeBuffer;
        private GraphicsBuffer _subMeshBuffer;

        public GraphicsBuffer RangeBuffer => _rangeBuffer;
        public GraphicsBuffer SubMeshBuffer => _subMeshBuffer;
        public int Hash { get; private set; }

        public void Pack(IReadOnlyList<PathTracingRendererRecord> records)
        {
            _data.Clear();
            _ranges.Clear();
            _subMeshes.Clear();
            _indices.Clear();
            int hash = records.Count;
            foreach (var record in records)
            {
                uint start = (uint)_subMeshes.Count;
                foreach (var slot in record.MaterialSlots)
                {
                    if (!_indices.TryGetValue(slot.Key, out uint index))
                    {
                        if (_data.Count == MaxMaterials)
                            throw new InvalidOperationException("Path tracing material count exceeds the 32768-material limit.");
                        index = (uint)_data.Count;
                        _indices.Add(slot.Key, index);
                        _data.Add(slot.Data);
                        hash = HashCode.Combine(hash, slot.Key, slot.Data.IoR, slot.Data.AttenuationColor, slot.Data.AttenuationDistance, slot.Data.Flags);
                    }
                    _subMeshes.Add(new PathTracingSubMeshMaterial { IndexStart = slot.IndexStart, MaterialIndex = index });
                    hash = HashCode.Combine(hash, slot.IndexStart, index);
                }
                _ranges.Add(new PathTracingMaterialRange { Start = start, Count = (uint)_subMeshes.Count - start });
            }
            Hash = hash;
        }

        internal static PathTracingMaterialData ReadMaterial(Material material, MaterialPropertyBlock block)
        {
            float ior = ReadFloat(material, block, "_Ior", ReadFloat(material, block, "_IOR", ReadFloat(material, block, "_RefractionIndex", 1.5f)));
            Vector3 attenuation = ReadColor(material, block, "_TransmittanceColor", ReadColor(material, block, "_RefractionColor", Vector3.one));
            float distance = ReadFloat(material, block, "_TransmittanceDistance", 1.0f);
            if (float.IsNaN(ior) || float.IsInfinity(ior) || ior <= 0f)
                throw new InvalidOperationException("Path tracing requires a finite positive material IoR: " + material.name);
            return new PathTracingMaterialData { AttenuationColor = attenuation, AttenuationDistance = distance, IoR = ior, Flags = DefaultFlags };
        }

        private static float ReadFloat(Material material, MaterialPropertyBlock block, string name, float fallback)
        {
            int id = Shader.PropertyToID(name);
            if (!material.HasProperty(id))
                return fallback;
            return block.HasFloat(id) ? block.GetFloat(id) : material.GetFloat(id);
        }

        private static Vector3 ReadColor(Material material, MaterialPropertyBlock block, string name, Vector3 fallback)
        {
            int id = Shader.PropertyToID(name);
            if (!material.HasProperty(id))
                return fallback;
            Vector4 value = block.HasVector(id) || block.HasColor(id) ? block.GetVector(id) : material.GetVector(id);
            int property = material.shader.FindPropertyIndex(name);
            if (property >= 0 && material.shader.GetPropertyType(property) == ShaderPropertyType.Color
                && (material.shader.GetPropertyFlags(property) & ShaderPropertyFlags.HDR) == 0)
            {
                var linear = new Color(value.x, value.y, value.z, 1f).linear;
                return new Vector3(linear.r, linear.g, linear.b);
            }
            return new Vector3(value.x, value.y, value.z);
        }

        public void Upload()
        {
            Upload(ref _buffer, _data, "PathTracingMaterials");
            Upload(ref _rangeBuffer, _ranges, "PathTracingMaterialRanges");
            Upload(ref _subMeshBuffer, _subMeshes, "PathTracingSubMeshMaterials");
        }

        private static void Upload<T>(ref GraphicsBuffer buffer, List<T> data, string name) where T : struct
        {
            int count = Mathf.Max(1, data.Count);
            if (buffer == null || buffer.count < count)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(count), Marshal.SizeOf<T>()) { name = name };
            }
            if (data.Count > 0)
                buffer.SetData(data);
        }

        public void Bind(CommandBuffer cmd, RayTracingShader shader)
        {
            cmd.SetRayTracingBufferParam(shader, MaterialBufferId, _buffer);
            cmd.SetRayTracingIntParam(shader, MaterialCountId, _data.Count);
        }

        public void BindHitShaderGlobals(CommandBuffer cmd)
        {
            cmd.SetGlobalBuffer(MaterialBufferId, _buffer);
            cmd.SetGlobalInt(MaterialCountId, _data.Count);
        }

        public void Dispose()
        {
            _buffer?.Release(); _buffer = null;
            _rangeBuffer?.Release(); _rangeBuffer = null;
            _subMeshBuffer?.Release(); _subMeshBuffer = null;
        }
    }
}
