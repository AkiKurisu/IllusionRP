using System.Runtime.InteropServices;

namespace UnityRhi
{
    // Canonical packed structs for the Version 14 wire ABI. Keep these in
    // exact lockstep with RenderingPlugin/Source/CommandStreamWire.h.
    internal static class CommandWire
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct StreamHeader { internal uint Magic, Version, ByteSize, CommandCount; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UInt32Payload { internal uint Value; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HandlePayload { internal ulong Handle; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HandleUInt32Payload { internal ulong Handle; internal uint Value; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CopyBufferPayload
        {
            internal ulong Dest, DestOffset, Src, SrcOffset, ByteSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CopyTextureToBufferPayload
        {
            internal ulong Dest, DestOffset, Src;
            internal uint ArraySlice, MipLevel;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct MarkerPayload { internal uint ByteSize; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TextureSubresources
        {
            internal uint BaseMipLevel, NumMipLevels, BaseArraySlice, NumArraySlices;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TextureSlice
        {
            internal uint X, Y, Z, Width, Height, Depth, MipLevel, ArraySlice;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TextureCopyPayload
        {
            internal ulong Dest;
            internal TextureSlice DestSlice;
            internal ulong Src;
            internal TextureSlice SrcSlice;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ResolveTexturePayload
        {
            internal ulong Dest;
            internal TextureSubresources DestSubresources;
            internal ulong Src;
            internal TextureSubresources SrcSubresources;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TextureSubresourceStatePayload
        {
            internal ulong Texture;
            internal TextureSubresources Subresources;
            internal uint State;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct WriteBufferPayload
        {
            internal ulong Buffer, DestOffset, UploadTicket;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct WriteTexturePayload
        {
            internal ulong Texture;
            internal uint ArraySlice, MipLevel;
            internal ulong UploadTicket;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct StatePayload
        {
            internal ulong Object, IndirectParams;
            internal uint BindingCount, Flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BindingStatePayload
        {
            internal ulong Object;
            internal uint BindingCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct AccelStructBuildPayload
        {
            internal ulong AccelStruct;
            internal uint ElementCount, BuildFlags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TlasFromBufferPayload
        {
            internal ulong AccelStruct, InstanceBuffer, InstanceBufferOffset;
            internal uint NumInstances, BuildFlags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct DispatchPayload { internal uint X, Y, Z; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct DrawPayload { internal uint A, B, C, D; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct DrawIndexedPayload { internal uint A, B, C, D, E; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct DrawIndirectPayload { internal ulong Offset; internal uint Count; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct DrawIndirectCountPayload
        {
            internal ulong ParamsOffset;
            internal uint MaxDrawCount;
            internal ulong CountOffset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ClearTextureFloatPayload
        {
            internal ulong Texture;
            internal float R, G, B, A;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ClearTextureFloatSubresourcesPayload
        {
            internal ulong Texture;
            internal TextureSubresources Subresources;
            internal float R, G, B, A;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ClearDepthStencilPayload
        {
            internal ulong Texture;
            internal uint ClearDepth;
            internal float Depth;
            internal uint ClearStencil, Stencil;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ClearDepthStencilSubresourcesPayload
        {
            internal ulong Texture;
            internal TextureSubresources Subresources;
            internal uint ClearDepth;
            internal float Depth;
            internal uint ClearStencil, Stencil;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ClearTextureUIntPayload
        {
            internal ulong Texture;
            internal TextureSubresources Subresources;
            internal uint Value;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct Viewport { internal float MinX, MaxX, MinY, MaxY, MinZ, MaxZ; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct GraphicsStatePayload
        {
            internal ulong Pipeline, Framebuffer;
            internal Viewport Viewport;
            internal float BlendR, BlendG, BlendB, BlendA;
            internal ulong IndexBuffer;
            internal uint IndexFormat;
            internal ulong IndexOffset, IndirectParams, IndirectCountBuffer;
            internal uint VertexBufferCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct VertexBufferBinding { internal ulong Buffer; internal uint Slot; internal ulong Offset; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal unsafe struct DlrrDispatchPayload
        {
            internal ulong Input, Output, MotionVectors, Depth;
            internal ulong DiffuseAlbedo, SpecularAlbedo, NormalRoughness, SpecularMotion;
            internal fixed float WorldToViewMatrix[16];
            internal fixed float ViewToClipMatrix[16];
            internal ushort OutputWidth, OutputHeight, CurrentWidth, CurrentHeight;
            internal float CameraJitterX, CameraJitterY;
            internal int InstanceId;
            internal byte UseSpecularMotionVector, UpscalerMode, Preset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct DlssNrDispatchPayload
        {
            internal ulong Color, Output, MotionVectors, Depth;
            internal ushort InputWidth, InputHeight, OutputWidth, OutputHeight;
            internal float MotionVectorScaleX, MotionVectorScaleY;
            internal float Intensity, LocalToneStrength, LocalStructureStrength, SkinStructureStrength;
            internal int InstanceId;
            internal byte DepthInverted, Reset, UseAutoMask, UiCorrection;
            internal byte Upscaling, Preset, Style;
        }
    }
}
