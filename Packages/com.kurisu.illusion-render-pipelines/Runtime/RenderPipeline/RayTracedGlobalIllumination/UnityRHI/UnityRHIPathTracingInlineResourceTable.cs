using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.RayTracedGlobalIllumination.UnityRHI
{
    internal sealed class UnityRHIPathTracingInlineResourceTable : IPathTracingInlineResourceTable
    {
        private const string PluginName = "GfxPluginIllusionRHI";

        [StructLayout(LayoutKind.Sequential)]
        private struct Entry
        {
            public IntPtr Resource;
            public byte Type;
            public byte MinMip;
            public byte MaxMip;
            public byte Format;
            public uint ByteSize;
        }

        private const byte TextureEntry = 1;
        private const byte BufferEntry = 3;

        [DllImport(PluginName)]
        private static extern int MeetemBindless_GetCapacity();

        [DllImport(PluginName)]
        private static extern int MeetemBindless_SetBindlessTextures(int offset, int count, [In] Entry[] entries);

        private readonly Entry[] _entry = new Entry[1];
        private int _capacity = -1;

        public int Capacity
        {
            get
            {
                if (_capacity < 0)
                {
                    try
                    {
                        _capacity = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ? MeetemBindless_GetCapacity() : 0;
                    }
                    catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                    {
                        _capacity = 0;
                    }
                }
                return _capacity;
            }
        }

        public void SetTexture(int slot, IntPtr resource, int dxgiFormat)
        {
            _entry[0] = new Entry { Resource = resource, Type = TextureEntry, MaxMip = 255, Format = (byte)dxgiFormat };
            MeetemBindless_SetBindlessTextures(slot, 1, _entry);
        }

        public void SetBuffer(int slot, IntPtr resource, uint byteSize)
        {
            _entry[0] = new Entry { Resource = resource, Type = BufferEntry, MaxMip = 255, ByteSize = byteSize };
            MeetemBindless_SetBindlessTextures(slot, 1, _entry);
        }
    }
}
