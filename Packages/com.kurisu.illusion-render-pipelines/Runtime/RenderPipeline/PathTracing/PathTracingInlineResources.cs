using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal static class PathTracingInlineResources
    {
        public const int DefaultTextureCount = 6;

        private sealed class TextureEntry
        {
            public Texture Texture;
            public int Slot;
            public uint UpdateCount;
            public IntPtr Resource;
            public int References;
        }

        private sealed class MeshEntry
        {
            public Mesh Mesh;
            public readonly int[] VertexSlots = { -1, -1, -1, -1 };
            public int IndexSlot = -1;
            public int References;
            public bool Valid;
        }

        private static IPathTracingInlineResourceTable s_Table;
        private static bool s_Created;
        private static readonly Stack<int> s_Free = new();
        private static readonly List<int> s_Released = new();
        private static readonly Queue<(int[] Slots, GraphicsFence Fence)> s_Pending = new();
        private static int s_PendingSlots;
        private static readonly Dictionary<int, TextureEntry> s_Textures = new();
        private static readonly Dictionary<int, int> s_SlotTextures = new();
        private static readonly Dictionary<int, MeshEntry> s_Meshes = new();
        private static readonly List<int> s_Changed = new();
        private static int s_NextSlot;
        private static int s_Capacity;
        private static bool s_OverflowReported;

        public static int Generation { get; private set; }

#if UNITY_EDITOR
        static PathTracingInlineResources()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => s_Table?.Dispose();
        }
#endif

        public static bool IsAvailable
        {
            get
            {
                EnsureCreated();
                return s_Capacity > 0;
            }
        }

        private static void EnsureCreated()
        {
            if (s_Created)
                return;
            s_Table = PathTracingInlineBackend.Create();
            if (s_Table == null)
                return;
            s_Created = true;
            s_Capacity = s_Table?.Capacity ?? 0;
            if (s_Capacity <= DefaultTextureCount)
            {
                s_Capacity = 0;
                return;
            }
            s_NextSlot = DefaultTextureCount;
            SetTexture(0, Texture2D.whiteTexture);
            SetTexture(1, Texture2D.blackTexture);
            SetTexture(2, Texture2D.grayTexture);
            SetTexture(3, Texture2D.normalTexture);
            SetTexture(4, Texture2D.redTexture);
            SetTexture(5, Texture2D.linearGrayTexture);
        }

        public static int DefaultSlot(PathTracingInlineRegistry.TextureDefault value) => (int)value;

        public static void BeginFrame(int frame)
        {
            if (!IsAvailable)
                return;
            s_Changed.Clear();
            foreach ((int id, TextureEntry entry) in s_Textures)
            {
                // Render textures change with every render and the table holds their resources while registered.
                if (!entry.Texture || entry.Texture is RenderTexture)
                    continue;
                if (entry.Texture.updateCount != entry.UpdateCount)
                    s_Changed.Add(id);
            }
            foreach (int id in s_Changed)
            {
                TextureEntry entry = s_Textures[id];
                entry.UpdateCount = entry.Texture.updateCount;
                IntPtr resource = entry.Texture.GetNativeTexturePtr();
                if (resource == entry.Resource)
                    continue;
                int slot = Allocate(frame);
                if (slot < 0)
                    continue;
                Release(entry.Slot, frame);
                s_SlotTextures.Remove(entry.Slot);
                s_SlotTextures[slot] = id;
                entry.Slot = slot;
                entry.Resource = resource;
                SetTexture(slot, entry.Texture);
                Generation++;
            }
        }

        public static int AcquireTexture(Texture texture, int frame)
        {
            if (!texture || texture.dimension != TextureDimension.Tex2D || !IsAvailable)
                return -1;
            int id = texture.GetInstanceID();
            if (s_Textures.TryGetValue(id, out TextureEntry entry))
            {
                entry.References++;
                return entry.Slot;
            }
            int slot = Allocate(frame);
            if (slot < 0)
                return -1;
            if (!SetTexture(slot, texture))
            {
                Release(slot, frame);
                return -1;
            }
            s_Textures.Add(id, new TextureEntry { Texture = texture, Slot = slot, UpdateCount = texture.updateCount, Resource = texture.GetNativeTexturePtr(), References = 1 });
            s_SlotTextures[slot] = id;
            return slot;
        }

        public static void ReleaseTexture(Texture texture, int frame)
        {
            if (!texture || !s_Textures.TryGetValue(texture.GetInstanceID(), out TextureEntry entry))
                return;
            if (--entry.References > 0)
                return;
            Release(entry.Slot, frame);
            s_SlotTextures.Remove(entry.Slot);
            s_Textures.Remove(texture.GetInstanceID());
        }

        public static void ReleaseTexture(int textureId, int frame)
        {
            if (!s_Textures.TryGetValue(textureId, out TextureEntry entry) || --entry.References > 0)
                return;
            Release(entry.Slot, frame);
            s_SlotTextures.Remove(entry.Slot);
            s_Textures.Remove(textureId);
        }

        public static bool TryGetTextureContent(int slot, out int content)
        {
            content = slot;
            if (slot < DefaultTextureCount)
                return true;
            if (!s_SlotTextures.TryGetValue(slot, out int id) || !s_Textures.TryGetValue(id, out TextureEntry entry) || !entry.Texture ||
                entry.Texture is RenderTexture)
                return false;
            content = HashCode.Combine(id, entry.UpdateCount, entry.Resource);
            return true;
        }

        public static bool AcquireMesh(Mesh mesh, int frame, out int[] vertexSlots, out int indexSlot)
        {
            vertexSlots = null;
            indexSlot = -1;
            if (!mesh || !IsAvailable)
                return false;
            int id = mesh.GetInstanceID();
            if (!s_Meshes.TryGetValue(id, out MeshEntry entry))
            {
                entry = new MeshEntry { Mesh = mesh };
                entry.Valid = Register(entry, frame);
                s_Meshes.Add(id, entry);
            }
            entry.References++;
            vertexSlots = entry.VertexSlots;
            indexSlot = entry.IndexSlot;
            return entry.Valid;
        }

        public static void ReleaseMesh(int meshId, int frame)
        {
            if (!s_Meshes.TryGetValue(meshId, out MeshEntry entry) || --entry.References > 0)
                return;
            ReleaseEntries(entry, frame);
            s_Meshes.Remove(meshId);
        }

        public static int AcquireBuffer(IntPtr buffer, uint size, int frame)
        {
            if (buffer == IntPtr.Zero || !IsAvailable)
                return -1;
            int slot = Allocate(frame);
            if (slot >= 0)
                s_Table.SetBuffer(slot, buffer, size);
            return slot;
        }

        public static void ReleaseBuffer(int slot, int frame) => Release(slot, frame);

        private static bool Register(MeshEntry entry, int frame)
        {
            Mesh mesh = entry.Mesh;
            int streams = mesh.vertexBufferCount;
            if (streams > entry.VertexSlots.Length)
                return false;
            for (int stream = 0; stream < streams; stream++)
            {
                IntPtr buffer = mesh.GetNativeVertexBufferPtr(stream);
                if (buffer == IntPtr.Zero)
                    return false;
                int slot = Allocate(frame);
                if (slot < 0)
                    return false;
                entry.VertexSlots[stream] = slot;
                s_Table.SetBuffer(slot, buffer, (uint)(mesh.vertexCount * mesh.GetVertexBufferStride(stream)));
            }
            IntPtr indices = mesh.GetNativeIndexBufferPtr();
            if (indices == IntPtr.Zero)
                return false;
            int indexSlot = Allocate(frame);
            if (indexSlot < 0)
                return false;
            entry.IndexSlot = indexSlot;
            s_Table.SetBuffer(indexSlot, indices, 0u);
            return true;
        }

        private static void ReleaseEntries(MeshEntry entry, int frame)
        {
            for (int i = 0; i < entry.VertexSlots.Length; i++)
            {
                if (entry.VertexSlots[i] >= 0)
                    Release(entry.VertexSlots[i], frame);
                entry.VertexSlots[i] = -1;
            }
            if (entry.IndexSlot >= 0)
                Release(entry.IndexSlot, frame);
            entry.IndexSlot = -1;
        }

        private static int Allocate(int frame)
        {
            if (s_Free.Count > 0)
                return s_Free.Pop();
            if (s_NextSlot < s_Capacity)
                return s_NextSlot++;
            if (!s_OverflowReported)
            {
                s_OverflowReported = true;
                Debug.LogWarning($"[PathTracing] The scene resource table is full ({s_Capacity} entries); further resources fall back to the diagnostic surface inline.");
            }
            return -1;
        }

        private static void Release(int slot, int frame)
        {
            if (slot < DefaultTextureCount)
                return;
            if (s_PendingSlots++ == 0)
                RenderPipelineManager.endContextRendering += Reclaim;
            s_Released.Add(slot);
        }

        private static void Reclaim(ScriptableRenderContext context, List<Camera> cameras)
        {
            while (s_Pending.Count > 0 && s_Pending.Peek().Fence.passed)
            {
                foreach (int slot in s_Pending.Dequeue().Slots)
                {
                    s_Table.Clear(slot);
                    s_Free.Push(slot);
                    s_PendingSlots--;
                }
            }
            if (s_Released.Count > 0)
            {
                var fence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                s_Pending.Enqueue((s_Released.ToArray(), fence));
                s_Released.Clear();
            }
            if (s_PendingSlots == 0)
                RenderPipelineManager.endContextRendering -= Reclaim;
        }

        private static bool SetTexture(int slot, Texture texture)
        {
            IntPtr resource = texture.GetNativeTexturePtr();
            if (resource == IntPtr.Zero)
                return false;
            s_Table.SetTexture(slot, resource, DxgiFormat(texture.graphicsFormat));
            return true;
        }

        private static int DxgiFormat(GraphicsFormat format)
        {
            return format switch
            {
                GraphicsFormat.R8G8B8A8_UNorm => 28,
                GraphicsFormat.R8G8B8A8_SRGB => 29,
                GraphicsFormat.B8G8R8A8_UNorm => 87,
                GraphicsFormat.B8G8R8A8_SRGB => 91,
                GraphicsFormat.RGBA_DXT1_UNorm => 71,
                GraphicsFormat.RGBA_DXT1_SRGB => 72,
                GraphicsFormat.RGBA_DXT3_UNorm => 74,
                GraphicsFormat.RGBA_DXT3_SRGB => 75,
                GraphicsFormat.RGBA_DXT5_UNorm => 77,
                GraphicsFormat.RGBA_DXT5_SRGB => 78,
                GraphicsFormat.R_BC4_UNorm => 80,
                GraphicsFormat.R_BC4_SNorm => 81,
                GraphicsFormat.RG_BC5_UNorm => 83,
                GraphicsFormat.RG_BC5_SNorm => 84,
                GraphicsFormat.RGB_BC6H_UFloat => 95,
                GraphicsFormat.RGB_BC6H_SFloat => 96,
                GraphicsFormat.RGBA_BC7_UNorm => 98,
                GraphicsFormat.RGBA_BC7_SRGB => 99,
                GraphicsFormat.R16G16B16A16_SFloat => 10,
                GraphicsFormat.R16G16B16A16_UNorm => 11,
                GraphicsFormat.R32G32B32A32_SFloat => 2,
                GraphicsFormat.R8_UNorm => 61,
                GraphicsFormat.R8G8_UNorm => 49,
                GraphicsFormat.R16_UNorm => 56,
                GraphicsFormat.R16_SFloat => 54,
                GraphicsFormat.R16G16_SFloat => 34,
                GraphicsFormat.R16G16_UNorm => 35,
                GraphicsFormat.R32_SFloat => 41,
                GraphicsFormat.R32G32_SFloat => 16,
                GraphicsFormat.B10G11R11_UFloatPack32 => 26,
                GraphicsFormat.A2B10G10R10_UNormPack32 => 24,
                GraphicsFormat.E5B9G9R9_UFloatPack32 => 67,
                _ => 0
            };
        }

        public static int TextureCount => s_Textures.Count;

        public static int MeshCount => s_Meshes.Count;

        public static int UsedSlots => s_NextSlot - s_Free.Count - s_PendingSlots;
    }
}
