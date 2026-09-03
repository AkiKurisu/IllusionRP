using System;
using UnityEngine;

namespace UnityRhi
{
    /// <summary>Mirrors nvrhi::HeapDesc.</summary>
    public sealed class HeapDesc
    {
        public ulong Capacity;
        public HeapType Type = HeapType.DeviceLocal;
        public string DebugName = "";
    }

    public sealed class TextureTilesMapping
    {
        public TiledTextureCoordinate[] Coordinates = Array.Empty<TiledTextureCoordinate>();
        public TiledTextureRegion[] Regions = Array.Empty<TiledTextureRegion>();
        public ulong[] ByteOffsets = Array.Empty<ulong>();
        public Heap Heap;
    }

    // Desc structs mirror nvrhi (External/nvrhi/include/nvrhi/nvrhi.h)
    // field-for-field with the same defaults. Documented deviations:
    //  - nvrhi::Color maps to UnityEngine.Color;
    //  - DebugName exists on every desc (nvrhi has it only on buffers/textures);
    //    it feeds D3D12 object names and the native leak report;
    //  - VulkanBindingOffsets is omitted (D3D12-only backend).

    /// <summary>Mirrors nvrhi::BufferDesc (fields and defaults).</summary>
    public sealed class BufferDesc
    {
        public ulong ByteSize;
        public uint StructStride;            // if non-zero it's structured
        public uint MaxVersions;             // only used for volatile buffers on Vulkan; ignored here
        public string DebugName = "";
        public Format Format = Format.UNKNOWN; // for typed buffer views
        public bool CanHaveUAVs;
        public bool CanHaveTypedViews;
        public bool CanHaveRawViews;
        public bool IsVertexBuffer;
        public bool IsIndexBuffer;
        public bool IsConstantBuffer;
        public bool IsDrawIndirectArgs;
        public bool IsAccelStructBuildInput;
        public bool IsAccelStructStorage;
        public bool IsShaderBindingTable;

        // A dynamic/upload buffer whose contents only live in the current command
        // list. No D3D12 resource is created; write via CommandList.WriteBuffer.
        public bool IsVolatile;

        // Created with no backing memory; bound later (nvrhi bindBufferMemory).
        public bool IsVirtual;

        public ResourceStates InitialState = ResourceStates.Common;
        public bool KeepInitialState;
        public CpuAccessMode CpuAccess = CpuAccessMode.None;
        public SharedResourceFlags SharedResourceFlags = SharedResourceFlags.None;
    }

    /// <summary>Mirrors nvrhi::TextureDesc (fields and defaults).</summary>
    public sealed class TextureDesc
    {
        public uint Width = 1;
        public uint Height = 1;
        public uint Depth = 1;
        public uint ArraySize = 1;
        public uint MipLevels = 1;
        public uint SampleCount = 1;
        public uint SampleQuality;
        public Format Format = Format.UNKNOWN;
        public TextureDimension Dimension = TextureDimension.Texture2D;
        public string DebugName = "";

        public bool IsShaderResource = true;
        public bool IsRenderTarget;
        public bool IsUAV;
        public bool IsTypeless;
        public bool IsShadingRateSurface;

        public SharedResourceFlags SharedResourceFlags = SharedResourceFlags.None;

        // Created with no backing memory; bound later (nvrhi bindTextureMemory).
        public bool IsVirtual;
        public bool IsTiled;

        public Color ClearValue = Color.clear;
        public bool UseClearValue;

        public ResourceStates InitialState = ResourceStates.Unknown;
        public bool KeepInitialState;
    }

    /// <summary>Mirrors nvrhi::ShaderDesc (fields and defaults).</summary>
    public sealed class ShaderDesc
    {
        public ShaderType ShaderType = ShaderType.None;
        public string DebugName = "";
        public string EntryName = "main";

        // Register slot of the NVAPI HLSL extension UAV, or -1 (nvrhi's SER path).
        public int HlslExtensionsUAV = -1;

        // nvrhi's legacy NV extension fields (fastGSFlags, custom semantics,
        // coordinate swizzling) are intentionally omitted.
    }

    /// <summary>Mirrors nvrhi::SamplerDesc (fields and defaults).</summary>
    public sealed class SamplerDesc
    {
        public Color BorderColor = Color.white;
        public float MaxAnisotropy = 1f;
        public float MipBias;
        public float MinLod;
        public float MaxLod = float.MaxValue;

        public bool MinFilter = true;
        public bool MagFilter = true;
        public bool MipFilter = true;
        public SamplerAddressMode AddressU = SamplerAddressMode.Clamp;
        public SamplerAddressMode AddressV = SamplerAddressMode.Clamp;
        public SamplerAddressMode AddressW = SamplerAddressMode.Clamp;
        public SamplerReductionType ReductionType = SamplerReductionType.Standard;
        public string DebugName = "";
    }

    /// <summary>Mirrors nvrhi::BindingLayoutItem: { slot, type, size }.</summary>
    public struct BindingLayoutItem
    {
        public uint Slot;
        public ResourceType Type;

        // Push constant byte size when (Type == PushConstants);
        // descriptor array size (1 or more) for all other resource types.
        // Must be 1 for VolatileConstantBuffer.
        public ushort Size;

        public uint GetArraySize() => Type == ResourceType.PushConstants ? 1u : Size;

        private static BindingLayoutItem Make(ResourceType type, uint slot) => new BindingLayoutItem
        {
            Slot = slot,
            Type = type,
            Size = 1,
        };

        public static BindingLayoutItem Texture_SRV(uint slot) => Make(ResourceType.Texture_SRV, slot);
        public static BindingLayoutItem Texture_UAV(uint slot) => Make(ResourceType.Texture_UAV, slot);
        public static BindingLayoutItem TypedBuffer_SRV(uint slot) => Make(ResourceType.TypedBuffer_SRV, slot);
        public static BindingLayoutItem TypedBuffer_UAV(uint slot) => Make(ResourceType.TypedBuffer_UAV, slot);
        public static BindingLayoutItem StructuredBuffer_SRV(uint slot) => Make(ResourceType.StructuredBuffer_SRV, slot);
        public static BindingLayoutItem StructuredBuffer_UAV(uint slot) => Make(ResourceType.StructuredBuffer_UAV, slot);
        public static BindingLayoutItem RawBuffer_SRV(uint slot) => Make(ResourceType.RawBuffer_SRV, slot);
        public static BindingLayoutItem RawBuffer_UAV(uint slot) => Make(ResourceType.RawBuffer_UAV, slot);
        public static BindingLayoutItem ConstantBuffer(uint slot) => Make(ResourceType.ConstantBuffer, slot);
        public static BindingLayoutItem VolatileConstantBuffer(uint slot) => Make(ResourceType.VolatileConstantBuffer, slot);
        public static BindingLayoutItem Sampler(uint slot) => Make(ResourceType.Sampler, slot);
        public static BindingLayoutItem RayTracingAccelStruct(uint slot) => Make(ResourceType.RayTracingAccelStruct, slot);
        public static BindingLayoutItem SamplerFeedbackTexture_UAV(uint slot) => Make(ResourceType.SamplerFeedbackTexture_UAV, slot);

        public static BindingLayoutItem PushConstants(uint slot, uint byteSize) => new BindingLayoutItem
        {
            Slot = slot,
            Type = ResourceType.PushConstants,
            Size = (ushort)byteSize,
        };

        public BindingLayoutItem SetArraySize(uint size)
        {
            Size = (ushort)size;
            return this;
        }
    }

    /// <summary>Mirrors nvrhi::BindingLayoutDesc.</summary>
    public sealed class BindingLayoutDesc
    {
        public ShaderType Visibility = ShaderType.None;

        // Controls the register space of the bindings (D3D12).
        public uint RegisterSpace;

        // Vulkan-only semantics in nvrhi (register space == descriptor set);
        // kept for API parity, ignored by the D3D12 backend.
        public bool RegisterSpaceIsDescriptorSet;

        public BindingLayoutItem[] Bindings = System.Array.Empty<BindingLayoutItem>();

        public string DebugName = ""; // UnityRHI extension
    }

    /// <summary>
    /// Mirrors nvrhi::BindlessLayoutDesc: attaches a descriptor table to
    /// unbounded resource arrays. Consumed by bindless resource tables.
    /// </summary>
    public sealed class BindlessLayoutDesc
    {
        public enum LayoutType : uint
        {
            Immutable = 0,
            MutableSrvUavCbv,
            MutableCounters,
            MutableSampler,
        }

        public ShaderType Visibility = ShaderType.None;
        public uint FirstSlot;
        public uint MaxCapacity;

        // Each item's Slot is the register space the table is bound to; the
        // item's Type selects SRV/UAV/CBV/Sampler for that space.
        public BindingLayoutItem[] RegisterSpaces = System.Array.Empty<BindingLayoutItem>();

        public LayoutType Type = LayoutType.Immutable;

        public string DebugName = ""; // UnityRHI extension
    }

    /// <summary>
    /// Mirrors nvrhi::BindingSetItem: { resourceHandle, slot, arrayElement,
    /// type, dimension, format, subresources|range }. The C# struct keeps
    /// Range and Subresources as separate fields instead of a union.
    /// </summary>
    public struct BindingSetItem
    {
        public Resource ResourceHandle;
        public uint Slot;

        // Index in a binding array; must be less than the Size of the matching
        // BindingLayoutItem. Effective slot = Slot + ArrayElement (flattened).
        public uint ArrayElement;

        public ResourceType Type;
        public TextureDimension Dimension; // valid for Texture_SRV, Texture_UAV
        public Format Format;              // valid for Texture_SRV/UAV, Buffer_SRV/UAV

        public BufferRange Range;                  // valid for Buffer_SRV/UAV, ConstantBuffer
        public TextureSubresourceSet Subresources; // valid for Texture_SRV, Texture_UAV

        public static BindingSetItem None(uint slot = 0) => new BindingSetItem
        {
            Slot = slot,
            Type = ResourceType.None,
        };

        public static BindingSetItem Texture_SRV(uint slot, Texture texture, Format format = Format.UNKNOWN,
            TextureSubresourceSet? subresources = null, TextureDimension dimension = TextureDimension.Unknown)
            => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.Texture_SRV,
                ResourceHandle = texture,
                Format = format,
                Dimension = dimension,
                Subresources = subresources ?? TextureSubresourceSet.AllSubresources,
            };

        public static BindingSetItem Texture_UAV(uint slot, Texture texture, Format format = Format.UNKNOWN,
            TextureSubresourceSet? subresources = null, TextureDimension dimension = TextureDimension.Unknown)
            => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.Texture_UAV,
                ResourceHandle = texture,
                Format = format,
                Dimension = dimension,
                Subresources = subresources ?? TextureSubresourceSet.FirstMipAllSlices,
            };

        public static BindingSetItem TypedBuffer_SRV(uint slot, Buffer buffer, Format format = Format.UNKNOWN,
            BufferRange? range = null) => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.TypedBuffer_SRV,
                ResourceHandle = buffer,
                Format = format,
                Range = range ?? BufferRange.EntireBuffer,
            };

        public static BindingSetItem TypedBuffer_UAV(uint slot, Buffer buffer, Format format = Format.UNKNOWN,
            BufferRange? range = null) => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.TypedBuffer_UAV,
                ResourceHandle = buffer,
                Format = format,
                Range = range ?? BufferRange.EntireBuffer,
            };

        public static BindingSetItem ConstantBuffer(uint slot, Buffer buffer, BufferRange? range = null)
        {
            bool isVolatile = buffer != null && buffer.Desc.IsVolatile;
            return new BindingSetItem
            {
                Slot = slot,
                Type = isVolatile ? ResourceType.VolatileConstantBuffer : ResourceType.ConstantBuffer,
                ResourceHandle = buffer,
                Range = range ?? BufferRange.EntireBuffer,
            };
        }

        public static BindingSetItem Sampler(uint slot, Sampler sampler) => new BindingSetItem
        {
            Slot = slot,
            Type = ResourceType.Sampler,
            ResourceHandle = sampler,
        };

        public static BindingSetItem RayTracingAccelStruct(uint slot, AccelStruct accelStruct) => new BindingSetItem
        {
            Slot = slot,
            Type = ResourceType.RayTracingAccelStruct,
            ResourceHandle = accelStruct,
        };

        public static BindingSetItem StructuredBuffer_SRV(uint slot, Buffer buffer, Format format = Format.UNKNOWN,
            BufferRange? range = null) => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.StructuredBuffer_SRV,
                ResourceHandle = buffer,
                Format = format,
                Range = range ?? BufferRange.EntireBuffer,
            };

        public static BindingSetItem StructuredBuffer_UAV(uint slot, Buffer buffer, Format format = Format.UNKNOWN,
            BufferRange? range = null) => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.StructuredBuffer_UAV,
                ResourceHandle = buffer,
                Format = format,
                Range = range ?? BufferRange.EntireBuffer,
            };

        public static BindingSetItem RawBuffer_SRV(uint slot, Buffer buffer, BufferRange? range = null)
            => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.RawBuffer_SRV,
                ResourceHandle = buffer,
                Range = range ?? BufferRange.EntireBuffer,
            };

        public static BindingSetItem RawBuffer_UAV(uint slot, Buffer buffer, BufferRange? range = null)
            => new BindingSetItem
            {
                Slot = slot,
                Type = ResourceType.RawBuffer_UAV,
                ResourceHandle = buffer,
                Range = range ?? BufferRange.EntireBuffer,
            };

        public static BindingSetItem PushConstants(uint slot, uint byteSize) => new BindingSetItem
        {
            Slot = slot,
            Type = ResourceType.PushConstants,
            Range = new BufferRange(0, byteSize),
        };

        public BindingSetItem SetArrayElement(uint value) { ArrayElement = value; return this; }
        public BindingSetItem SetFormat(Format value) { Format = value; return this; }
        public BindingSetItem SetDimension(TextureDimension value) { Dimension = value; return this; }
        public BindingSetItem SetSubresources(TextureSubresourceSet value) { Subresources = value; return this; }
        public BindingSetItem SetRange(BufferRange value) { Range = value; return this; }
    }

    /// <summary>Mirrors nvrhi::BindingSetDesc.</summary>
    public sealed class BindingSetDesc
    {
        public BindingSetItem[] Bindings = System.Array.Empty<BindingSetItem>();

        // Enables automatic liveness tracking of this binding set by command
        // lists. With false, the caller must keep it alive until all commands
        // using it have finished.
        public bool TrackLiveness = true;

        public string DebugName = ""; // UnityRHI extension
    }

    /// <summary>Mirrors nvrhi::ComputePipelineDesc.</summary>
    public sealed class ComputePipelineDesc
    {
        public Shader CS;
        public BindingLayout[] BindingLayouts = System.Array.Empty<BindingLayout>();
        public string DebugName = ""; // UnityRHI extension
    }

    /// <summary>Mirrors nvrhi::ComputeState.</summary>
    public struct ComputeState
    {
        public ComputePipeline Pipeline;
        public Resource[] Bindings;
        public Buffer IndirectParams;
    }

    /// <summary>Mirrors nvrhi::VertexAttributeDesc.</summary>
    public struct VertexAttributeDesc
    {
        public string Name;
        public uint SemanticIndex;
        public Format Format;
        public uint BufferIndex;
        public uint Offset;
        public uint ElementStride;
        public bool IsInstanced;
    }

    /// <summary>Mirrors nvrhi::FramebufferDesc for whole-attachment bindings.</summary>
    public sealed class FramebufferDesc
    {
        public Texture[] ColorAttachments = System.Array.Empty<Texture>();
        public Texture DepthAttachment;
        public string DebugName = "";
    }

    /// <summary>Mirrors the reduced UnityRHI graphics pipeline desc.</summary>
    public sealed class GraphicsPipelineDesc
    {
        public PrimitiveType PrimType = PrimitiveType.TriangleList;
        public RasterCullMode CullMode = RasterCullMode.Back;
        public RasterFillMode FillMode = RasterFillMode.Solid;
        public bool FrontCounterClockwise;
        public bool DepthClipEnable = true;

        public bool DepthTestEnable;
        public bool DepthWriteEnable;

        // D3D12_COMPARISON_FUNC, 0 means native default LESS.
        public uint DepthFunc;

        public bool BlendEnable;

        // D3D12_BLEND / D3D12_BLEND_OP values, 0 means native defaults
        // ONE/ZERO/ADD, matching the native flattened RhiGraphicsPipelineDesc.
        public uint SrcBlend;
        public uint DestBlend;
        public uint BlendOp;
        public uint SrcBlendAlpha;
        public uint DestBlendAlpha;
        public uint BlendOpAlpha;
        public uint ColorWriteMask;

        public Shader VS;
        public Shader PS;
        public InputLayout InputLayout;
        public BindingLayout[] BindingLayouts = System.Array.Empty<BindingLayout>();
        public string DebugName = "";
    }

    /// <summary>Mirrors nvrhi::Viewport.</summary>
    public struct Viewport
    {
        public float MinX;
        public float MaxX;
        public float MinY;
        public float MaxY;
        public float MinZ;
        public float MaxZ;
    }

    /// <summary>Mirrors nvrhi::VertexBufferBinding.</summary>
    public struct VertexBufferBinding
    {
        public Buffer Buffer;
        public uint Slot;
        public ulong Offset;
    }

    /// <summary>Mirrors nvrhi::GraphicsState.</summary>
    public struct GraphicsState
    {
        public GraphicsPipeline Pipeline;
        public Framebuffer Framebuffer;
        public Viewport Viewport;
        // Mirrors nvrhi::GraphicsState::blendConstantColor. The original
        // Donut BloomPass uses it for ConstantColor/InvConstantColor apply.
        public Color BlendConstantColor;
        public Buffer IndexBuffer;
        public Format IndexBufferFormat;
        public ulong IndexBufferOffset;
        public VertexBufferBinding[] VertexBuffers;
        public Resource[] Bindings;
        public Buffer IndirectParams;
        public Buffer IndirectCountBuffer;
    }

    /// <summary>Mirrors nvrhi::DrawArguments.</summary>
    public struct DrawArguments
    {
        public uint VertexCount;
        public uint InstanceCount;
        public uint StartVertexLocation;
        public uint StartInstanceLocation;
    }

    /// <summary>Mirrors nvrhi::DrawArguments for indexed draws.</summary>
    public struct DrawIndexedArguments
    {
        public uint IndexCount;
        public uint InstanceCount;
        public uint StartIndexLocation;
        public uint BaseVertexLocation;
        public uint StartInstanceLocation;
    }
}
