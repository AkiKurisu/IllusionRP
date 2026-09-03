using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityRhi.Nri;

namespace UnityRhi.Nrd
{
    // ===================================================================================
    // FRAME DATA (Packed)
    // ===================================================================================

    // Keep in sync with C++ side (FrameData.h).
    public static class NrdLayout
    {
        public const int MaxDenoisersPerInstance = 4;
        public const int MaxDenoiserSettingsSize = 256;
    }

    // Per-denoiser settings blob for a single frame.
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct DenoiserSettingsEntry
    {
        public       uint     identifier;
        public       Denoiser denoiser;
        public fixed byte     settings[NrdLayout.MaxDenoiserSettingsSize];

        /// <summary>
        /// Overwrite the settings blob with an unmanaged struct (must be a valid NRD settings type).
        /// Size must not exceed <see cref="NrdLayout.MaxDenoiserSettingsSize"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write<T>(T value) where T : unmanaged
        {
            if (sizeof(T) > NrdLayout.MaxDenoiserSettingsSize)
                throw new InvalidOperationException($"settings size {sizeof(T)} exceeds blob capacity {NrdLayout.MaxDenoiserSettingsSize}");

            fixed (byte* p = settings)
            {
                *(T*)p = value;
            }
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct NrdFrameData
    {
        public CommonSettings commonSettings;

        public uint                  denoiserCount;
        public DenoiserSettingsEntry entry0;
        public DenoiserSettingsEntry entry1;
        public DenoiserSettingsEntry entry2;
        public DenoiserSettingsEntry entry3;

        public ushort width;
        public ushort height;

        public int instanceId;

        public static NrdFrameData _default = CreateDefault();

        /// <summary>
        /// Indexed access to the inline entries[] array. Must be called on a ref to a storage
        /// location (not a readonly/temp copy), since struct members can't return ref to this.
        /// </summary>
        public static ref DenoiserSettingsEntry GetEntry(ref NrdFrameData data, int index)
        {
            switch (index)
            {
                case 0: return ref data.entry0;
                case 1: return ref data.entry1;
                case 2: return ref data.entry2;
                case 3: return ref data.entry3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        // -----------------------------------------------------------------------
        // Factory Method for C++ Defaults
        // -----------------------------------------------------------------------
        private static NrdFrameData CreateDefault()
        {
            return new NrdFrameData
            {
                commonSettings = CommonSettings._default,
                denoiserCount  = 0,
                width          = 0,
                height         = 0,
                instanceId     = 0,
            };
        }
    }


    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NrdResourceInput
    {
        public ResourceType     type;
        public IntPtr           texture;
        public NriResourceState state;
    }
}