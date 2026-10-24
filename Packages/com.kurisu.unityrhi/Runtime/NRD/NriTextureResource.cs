using System;
using System.Runtime.InteropServices;

namespace UnityRhi.Nri
{
    [Flags]
    public enum AccessBits : uint
    {
        NONE = 0,
        SHADER_RESOURCE = 1u << 13,
        SHADER_RESOURCE_STORAGE = 1u << 14,
    }

    public enum Layout : uint
    {
        UNDEFINED,
        GENERAL,
        PRESENT,
        COLOR_ATTACHMENT,
        SHADING_RATE_ATTACHMENT,
        DEPTH_STENCIL_ATTACHMENT,
        DEPTH_STENCIL_READONLY,
        SHADER_RESOURCE,
        SHADER_RESOURCE_STORAGE,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NriResourceState
    {
        public AccessBits accessBits;
        public Layout layout;
        public uint stageBits;
    }

    /// <summary>Non-owning NRI view of a UnityRHI D3D12 texture.</summary>
    public sealed class NriTextureResource : IDisposable
    {
        [DllImport("NRIPlugin")]
        private static extern IntPtr WrapD3D12Texture(IntPtr resource, uint format);

        [DllImport("NRIPlugin")]
        private static extern void ReleaseTexture(IntPtr nriTexture);

        public IntPtr NriPtr { get; private set; }
        public NriResourceState ResourceState { get; }

        public NriTextureResource(UnityRhi.Texture texture, NriResourceState state)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            IntPtr resource = texture.NativeResource;
            if (resource == IntPtr.Zero)
                throw new InvalidOperationException($"Texture '{texture.Desc.DebugName}' has no D3D12 resource.");
            NriPtr = WrapD3D12Texture(resource, ToDxgiFormat(texture.Desc.Format));
            if (NriPtr == IntPtr.Zero)
                throw new InvalidOperationException($"NRI failed to wrap texture '{texture.Desc.DebugName}'.");
            ResourceState = state;
        }

        public void Dispose()
        {
            if (NriPtr == IntPtr.Zero)
                return;
            ReleaseTexture(NriPtr);
            NriPtr = IntPtr.Zero;
        }

        private static uint ToDxgiFormat(UnityRhi.Format format)
        {
            // WrapD3D12Texture takes DXGI_FORMAT, then NRI derives its own
            // format from the native D3D12 descriptor.
            return format switch
            {
                UnityRhi.Format.R8_UNORM => 61,
                UnityRhi.Format.RG8_UNORM => 49,
                UnityRhi.Format.RGBA8_UNORM => 28,
                UnityRhi.Format.SRGBA8_UNORM => 29,
                UnityRhi.Format.R16_FLOAT => 54,
                UnityRhi.Format.RG16_FLOAT => 34,
                UnityRhi.Format.RGBA16_FLOAT => 10,
                UnityRhi.Format.R32_FLOAT => 41,
                UnityRhi.Format.R10G10B10A2_UNORM => 24,
                UnityRhi.Format.R11G11B10_FLOAT => 26,
                _ => throw new NotSupportedException($"No DXGI format mapping for {format}.")
            };
        }
    }
}
