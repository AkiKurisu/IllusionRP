using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingMotionHistory : IDisposable
    {
        private const int PositionStride = 12;

        private readonly ComputeShader _shader;

        private readonly int _kernel;

        private readonly GraphicsBuffer _empty = new(GraphicsBuffer.Target.Raw, 3, sizeof(uint)) { name = "PathTracingNoPositionHistory" };

        private readonly List<SkinnedMeshRenderer> _gathered = new();

        private readonly List<uint> _gatheredBases = new();

        private readonly List<GraphicsBuffer> _vertexBuffers = new();

        private Dictionary<int, uint> _bases = new();

        private Dictionary<int, uint> _previousBases = new();

        private GraphicsBuffer _positions;

        private GraphicsBuffer _previousPositions;

        public PathTracingMotionHistory(ComputeShader shader)
        {
            _shader = shader;
            _kernel = shader.FindKernel("GatherPositions");
        }

        public GraphicsBuffer PreviousPositions => _previousPositions ?? _empty;

        public void Advance(PathTracingInstanceTable instances)
        {
            ReleaseVertexBuffers();
            (_previousPositions, _positions) = (_positions, _previousPositions);
            (_previousBases, _bases) = (_bases, _previousBases);
            _bases.Clear();
            _gathered.Clear();
            _gatheredBases.Clear();

            var data = instances.Data;
            uint vertexCount = 0;
            for (int i = 0; i < data.Count; i++)
            {
                var entry = data[i];
                entry.PreviousPositionBase = PathTracingInstanceData.NoPositionHistory;
                if (instances.Renderers[i] is SkinnedMeshRenderer skinned && skinned.sharedMesh)
                {
                    int id = skinned.GetInstanceID();
                    if (_previousBases.TryGetValue(id, out uint previousBase))
                        entry.PreviousPositionBase = previousBase;
                    if ((skinned.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                        skinned.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                    else
                    {
                        _bases[id] = vertexCount;
                        _gathered.Add(skinned);
                        _gatheredBases.Add(vertexCount);
                        vertexCount += (uint)skinned.sharedMesh.vertexCount;
                    }
                }
                data[i] = entry;
            }

            int required = Mathf.Max(1, (int)vertexCount) * PositionStride / sizeof(uint);
            if (_positions == null || _positions.count < required)
            {
                _positions?.Release();
                _positions = new GraphicsBuffer(GraphicsBuffer.Target.Raw, Mathf.NextPowerOfTwo(required), sizeof(uint)) { name = "PathTracingPositionHistory" };
            }
        }

        public void Gather(CommandBuffer cmd)
        {
            cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.u_Positions, _positions);
            for (int i = 0; i < _gathered.Count; i++)
            {
                var skinned = _gathered[i];
                if (!skinned)
                    continue;
                var vertices = skinned.GetVertexBuffer();
                if (vertices == null)
                    continue;
                _vertexBuffers.Add(vertices);
                int vertexCount = Mathf.Min(skinned.sharedMesh.vertexCount, vertices.count);
                cmd.SetComputeBufferParam(_shader, _kernel, ShaderIDs.t_Vertices, vertices);
                cmd.SetComputeIntParam(_shader, ShaderIDs._VertexStride, vertices.stride);
                cmd.SetComputeIntParam(_shader, ShaderIDs._PositionOffset, 0);
                cmd.SetComputeIntParam(_shader, ShaderIDs._VertexCount, vertexCount);
                cmd.SetComputeIntParam(_shader, ShaderIDs._DestinationBase, (int)_gatheredBases[i]);
                cmd.DispatchCompute(_shader, _kernel, (vertexCount + 63) / 64, 1, 1);
            }
        }

        public void Assign(PathTracingInstanceTable instances)
        {
            var data = instances.Data;
            for (int i = 0; i < data.Count; i++)
            {
                if (instances.Renderers[i] is not SkinnedMeshRenderer skinned || !_previousBases.TryGetValue(skinned.GetInstanceID(), out uint previousBase))
                    continue;
                var entry = data[i];
                entry.PreviousPositionBase = previousBase;
                data[i] = entry;
            }
        }

        private void ReleaseVertexBuffers()
        {
            foreach (var buffer in _vertexBuffers)
                buffer.Dispose();
            _vertexBuffers.Clear();
        }

        public void Dispose()
        {
            ReleaseVertexBuffers();
            _positions?.Release();
            _previousPositions?.Release();
            _empty.Release();
        }

        private static class ShaderIDs
        {
            public static readonly int t_Vertices = Shader.PropertyToID("t_Vertices");
            public static readonly int u_Positions = Shader.PropertyToID("u_Positions");
            public static readonly int _VertexStride = Shader.PropertyToID("_VertexStride");
            public static readonly int _PositionOffset = Shader.PropertyToID("_PositionOffset");
            public static readonly int _VertexCount = Shader.PropertyToID("_VertexCount");
            public static readonly int _DestinationBase = Shader.PropertyToID("_DestinationBase");
        }
    }
}
