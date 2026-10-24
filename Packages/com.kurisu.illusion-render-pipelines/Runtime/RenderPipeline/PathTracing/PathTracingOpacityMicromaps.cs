using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal interface IPathTracingOpacityMicromapBackend
    {
        bool IsSupported { get; }

        ulong GpuAddress(IntPtr resource);

        IntPtr CreateArray(IntPtr data, IntPtr descs, uint count, uint level, uint format);

        void ReleaseArray(IntPtr array);

        void Attach(ulong indexAddress, uint indexCount, IntPtr array, IntPtr indices);

        void Detach(ulong indexAddress, uint indexCount);

        void Issue(CommandBuffer cmd);
    }

    internal static class PathTracingOpacityMicromapBackend
    {
        private static Func<IPathTracingOpacityMicromapBackend> _factory;

        internal static void Register(Func<IPathTracingOpacityMicromapBackend> factory) => _factory = factory;

        public static IPathTracingOpacityMicromapBackend Create() => _factory?.Invoke();
    }

    // Unity can reuse a renderer's bottom-level structures while the renderer lives, so a micromap lives as long as its renderers.
    internal sealed class PathTracingOpacityMicromaps : IDisposable
    {
        public static PathTracingOpacityMicromaps Shared { get; } = new();

        private const uint Level = 5;
        private const uint FourState = 2;
        private const uint TestFlag = 1u;
        private const uint BlendFlag = 2u;
        private const int MaxGroupsX = 65535;
        private const int Ineligible = 0;
        private const uint TextureSlotMask = 0x00FFFFFFu;

        private sealed class Micromap
        {
            public ulong IndexAddress;
            public uint IndexCount;
            public int Signature;
            public bool Lost;
            public int Geometry;
            public uint[] Coverage;
            public PathTracingInlineScene Owner;
            public uint Triangles;
            public IntPtr Array;
            public GraphicsBuffer Indices;
            public readonly HashSet<MeshRenderer> Renderers = new();
        }

        private struct Use
        {
            public (int Mesh, int SubMesh) Key;
            public PathTracingInlineScene Owner;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public int SubMesh;
            public int Geometry;
            public uint[] Coverage;
        }

        private readonly Dictionary<(int Mesh, int SubMesh), Micromap> _micromaps = new();
        private readonly Dictionary<(int Mesh, int SubMesh), int> _signatures = new();
        private readonly List<Use> _uses = new();
        private readonly List<Micromap> _pending = new();
        private readonly List<(int Mesh, int SubMesh)> _released = new();
        private readonly List<GraphicsBuffer> _bakeInputs = new();
        private readonly HashSet<MeshRenderer> _moved = new();
        private IPathTracingOpacityMicromapBackend _backend;
        private bool _backendResolved;

#if UNITY_EDITOR
        private PathTracingOpacityMicromaps()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }
#endif

        private IPathTracingOpacityMicromapBackend Backend
        {
            get
            {
                if (!_backendResolved)
                {
                    _backendResolved = true;
                    _backend = PathTracingOpacityMicromapBackend.Create();
                    if (_backend is { IsSupported: false })
                        _backend = null;
                }
                return _backend;
            }
        }

        public int Count => _micromaps.Count;

        public void BeginRebuild()
        {
            _uses.Clear();
            _signatures.Clear();
        }

        public void Collect(PathTracingInlineScene owner, MeshRenderer renderer, Mesh mesh, int subMesh, int geometry, uint[] coverage, uint record, List<uint> materialData)
        {
            if (!Application.isPlaying || Backend == null)
                return;
            var key = (mesh.GetInstanceID(), subMesh);
            int signature = Ineligible;
            int words = (int)record / 4;
            if (coverage.Length == 4 && (coverage[0] & TestFlag) != 0 && (coverage[0] & BlendFlag) == 0 &&
                PathTracingInlineResources.TryGetTextureContent((int)(materialData[words + (int)coverage[1]] & TextureSlotMask), out int texture))
            {
                uint scaleOffset = coverage[2] >> 16;
                signature = HashCode.Combine(texture, materialData[words + (int)coverage[1]] & ~TextureSlotMask, coverage[0],
                    materialData[words + (int)(coverage[2] & 0xFFFF) / 4 + 3], materialData[words + (int)coverage[3] / 4],
                    scaleOffset == 0xFFFF ? 0 : HashCode.Combine(materialData[words + (int)scaleOffset / 4], materialData[words + (int)scaleOffset / 4 + 1],
                        materialData[words + (int)scaleOffset / 4 + 2], materialData[words + (int)scaleOffset / 4 + 3]));
                if (signature == Ineligible)
                    signature = 1;
            }
            // Every use of a sub-mesh may share its bottom-level structure, so uses that disagree leave it without a micromap.
            if (_signatures.TryGetValue(key, out int seen) && seen != signature)
                signature = Ineligible;
            _signatures[key] = signature;
            _uses.Add(new Use { Key = key, Owner = owner, Renderer = renderer, Mesh = mesh, SubMesh = subMesh, Geometry = geometry, Coverage = coverage });
        }

        public void EndRebuild()
        {
            foreach (Use use in _uses)
            {
                int signature = _signatures[use.Key];
                if (_micromaps.TryGetValue(use.Key, out Micromap micromap))
                {
                    if (!micromap.Lost && micromap.Signature != signature)
                    {
                        if (micromap.Array != IntPtr.Zero)
                        {
                            Lose(micromap);
                        }
                        else if (signature == Ineligible)
                        {
                            _pending.Remove(micromap);
                            _micromaps.Remove(use.Key);
                            continue;
                        }
                        else
                        {
                            micromap.Signature = signature;
                            micromap.Coverage = use.Coverage;
                        }
                    }
                    if (micromap.Owner == use.Owner)
                        micromap.Geometry = use.Geometry;
                    if (micromap.Renderers.Add(use.Renderer) && micromap.Lost)
                        WithoutMicromaps(use.Renderer);
                    continue;
                }
                if (signature == Ineligible)
                    continue;
                SubMeshDescriptor descriptor = use.Mesh.GetSubMesh(use.SubMesh);
                ulong indexBuffer = Backend.GpuAddress(use.Mesh.GetNativeIndexBufferPtr());
                if (indexBuffer == 0 || descriptor.topology != MeshTopology.Triangles)
                    continue;
                micromap = new Micromap
                {
                    IndexAddress = indexBuffer + (ulong)descriptor.indexStart * (use.Mesh.indexFormat == IndexFormat.UInt16 ? 2u : 4u),
                    IndexCount = (uint)descriptor.indexCount,
                    Signature = signature,
                    Geometry = use.Geometry,
                    Owner = use.Owner,
                    Triangles = (uint)descriptor.indexCount / 3,
                    Coverage = use.Coverage
                };
                micromap.Renderers.Add(use.Renderer);
                _micromaps.Add(use.Key, micromap);
                _pending.Add(micromap);
            }
            _uses.Clear();

            _moved.RemoveWhere(renderer => !renderer);
            foreach (var pair in _micromaps)
            {
                pair.Value.Renderers.RemoveWhere(renderer => !renderer);
                if (pair.Value.Renderers.Count == 0)
                    _released.Add(pair.Key);
            }
            foreach (var key in _released)
            {
                Micromap micromap = _micromaps[key];
                Release(micromap);
                _pending.Remove(micromap);
                _micromaps.Remove(key);
            }
            _released.Clear();
        }

        public void Record(CommandBuffer cmd, ComputeShader bake, PathTracingInlineScene scene)
        {
            // Inputs of earlier bakes are no longer read once a later frame records.
            foreach (GraphicsBuffer buffer in _bakeInputs)
                buffer.Dispose();
            _bakeInputs.Clear();
            if (Backend == null || !bake || !_pending.Exists(m => m.Owner == scene))
                return;
            int kernel = bake.FindKernel("BakeOpacityMicromaps");
            scene.Bind(cmd, new PathTracingShaderTarget(bake, kernel));
            uint words = (1u << (int)(2 * Level)) / 16u;
            cmd.SetComputeIntParam(bake, ShaderIDs._OpacityMicromapLevel, (int)Level);
            foreach (Micromap micromap in _pending)
            {
                if (micromap.Owner != scene)
                    continue;
                var data = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int)(micromap.Triangles * words), 4);
                var descs = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int)micromap.Triangles * 2, 4);
                micromap.Indices = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int)micromap.Triangles, 4);
                int groups = (int)((micromap.Triangles * words + 63) / 64);
                int groupsX = Mathf.Min(groups, MaxGroupsX);
                cmd.SetComputeBufferParam(bake, kernel, ShaderIDs._OpacityMicromapData, data);
                cmd.SetComputeBufferParam(bake, kernel, ShaderIDs._OpacityMicromapDescs, descs);
                cmd.SetComputeBufferParam(bake, kernel, ShaderIDs._OpacityMicromapIndices, micromap.Indices);
                cmd.SetComputeIntParam(bake, ShaderIDs._OpacityMicromapGeometry, micromap.Geometry);
                cmd.SetComputeIntParam(bake, ShaderIDs._OpacityMicromapTriangles, (int)micromap.Triangles);
                cmd.SetComputeIntParam(bake, ShaderIDs._OpacityMicromapGroupsX, groupsX);
                cmd.SetComputeIntParams(bake, ShaderIDs._OpacityMicromapCoverage, (int)micromap.Coverage[0], (int)micromap.Coverage[1],
                    (int)micromap.Coverage[2], (int)micromap.Coverage[3]);
                cmd.DispatchCompute(bake, kernel, groupsX, (groups + groupsX - 1) / groupsX, 1);
                micromap.Array = Backend.CreateArray(data.GetNativeBufferPtr(), descs.GetNativeBufferPtr(), micromap.Triangles, Level, FourState);
                if (micromap.Array != IntPtr.Zero)
                    Backend.Attach(micromap.IndexAddress, micromap.IndexCount, micromap.Array, micromap.Indices.GetNativeBufferPtr());
                _bakeInputs.Add(data);
                _bakeInputs.Add(descs);
            }
            _pending.RemoveAll(m => m.Owner == scene);
            Backend.Issue(cmd);
        }

        public void Forget(PathTracingInlineScene scene)
        {
            foreach (Micromap micromap in _pending)
            {
                if (micromap.Owner != scene)
                    continue;
                foreach (var pair in _micromaps)
                {
                    if (pair.Value != micromap)
                        continue;
                    _micromaps.Remove(pair.Key);
                    break;
                }
            }
            _pending.RemoveAll(m => m.Owner == scene);
        }

        // Structures already built with the micromap keep referencing it.
        private void Lose(Micromap micromap)
        {
            micromap.Lost = true;
            Backend?.Detach(micromap.IndexAddress, micromap.IndexCount);
            micromap.Renderers.RemoveWhere(renderer => !renderer);
            foreach (MeshRenderer renderer in micromap.Renderers)
                WithoutMicromaps(renderer);
        }

        // Unity rebuilds a renderer's structure only when its build settings change, so the renderer moves to settings it has not used.
        private void WithoutMicromaps(MeshRenderer renderer)
        {
            if (!_moved.Add(renderer))
                return;
            const RayTracingAccelerationStructureBuildFlags minimizeMemory = RayTracingAccelerationStructureBuildFlags.MinimizeMemory;
            renderer.rayTracingAccelerationStructureBuildFlags = renderer.rayTracingAccelerationStructureBuildFlagsOverride
                ? renderer.rayTracingAccelerationStructureBuildFlags ^ minimizeMemory
                : RayTracingAccelerationStructureBuildFlags.PreferFastTrace | minimizeMemory;
            renderer.rayTracingAccelerationStructureBuildFlagsOverride = true;
        }

        private void Release(Micromap micromap)
        {
            if (micromap.Array != IntPtr.Zero)
            {
                if (!micromap.Lost)
                    Backend?.Detach(micromap.IndexAddress, micromap.IndexCount);
                Backend?.ReleaseArray(micromap.Array);
                micromap.Array = IntPtr.Zero;
            }
            micromap.Indices?.Dispose();
            micromap.Indices = null;
        }

        public void Dispose()
        {
            foreach (Micromap micromap in _micromaps.Values)
                Release(micromap);
            _micromaps.Clear();
            _signatures.Clear();
            _uses.Clear();
            _pending.Clear();
            _moved.Clear();
            foreach (GraphicsBuffer buffer in _bakeInputs)
                buffer.Dispose();
            _bakeInputs.Clear();
        }

        private static class ShaderIDs
        {
            public static readonly int _OpacityMicromapGeometry = Shader.PropertyToID("_OpacityMicromapGeometry");
            public static readonly int _OpacityMicromapCoverage = Shader.PropertyToID("_OpacityMicromapCoverage");
            public static readonly int _OpacityMicromapTriangles = Shader.PropertyToID("_OpacityMicromapTriangles");
            public static readonly int _OpacityMicromapLevel = Shader.PropertyToID("_OpacityMicromapLevel");
            public static readonly int _OpacityMicromapGroupsX = Shader.PropertyToID("_OpacityMicromapGroupsX");
            public static readonly int _OpacityMicromapData = Shader.PropertyToID("_OpacityMicromapData");
            public static readonly int _OpacityMicromapDescs = Shader.PropertyToID("_OpacityMicromapDescs");
            public static readonly int _OpacityMicromapIndices = Shader.PropertyToID("_OpacityMicromapIndices");
        }
    }
}
