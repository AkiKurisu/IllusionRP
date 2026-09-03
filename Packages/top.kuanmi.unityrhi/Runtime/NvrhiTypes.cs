using System;

namespace UnityRhi
{
    // Enums mirror nvrhi (External/nvrhi/include/nvrhi/nvrhi.h) name-for-name and
    // value-for-value; the native side widens them to uint32 (RhiTypes.h).

    public enum Format : uint
    {
        UNKNOWN,

        R8_UINT, R8_SINT, R8_UNORM, R8_SNORM,
        RG8_UINT, RG8_SINT, RG8_UNORM, RG8_SNORM,
        R16_UINT, R16_SINT, R16_UNORM, R16_SNORM, R16_FLOAT,
        BGRA4_UNORM, B5G6R5_UNORM, B5G5R5A1_UNORM,
        RGBA8_UINT, RGBA8_SINT, RGBA8_UNORM, RGBA8_SNORM,
        BGRA8_UNORM, BGRX8_UNORM,
        SRGBA8_UNORM, SBGRA8_UNORM, SBGRX8_UNORM,
        R10G10B10A2_UNORM, R11G11B10_FLOAT,
        RG16_UINT, RG16_SINT, RG16_UNORM, RG16_SNORM, RG16_FLOAT,
        R32_UINT, R32_SINT, R32_FLOAT,
        RGBA16_UINT, RGBA16_SINT, RGBA16_FLOAT, RGBA16_UNORM, RGBA16_SNORM,
        RG32_UINT, RG32_SINT, RG32_FLOAT,
        RGB32_UINT, RGB32_SINT, RGB32_FLOAT,
        RGBA32_UINT, RGBA32_SINT, RGBA32_FLOAT,

        D16, D24S8, X24G8_UINT, D32, D32S8, X32G8_UINT,

        BC1_UNORM, BC1_UNORM_SRGB,
        BC2_UNORM, BC2_UNORM_SRGB,
        BC3_UNORM, BC3_UNORM_SRGB,
        BC4_UNORM, BC4_SNORM,
        BC5_UNORM, BC5_SNORM,
        BC6H_UFLOAT, BC6H_SFLOAT,
        BC7_UNORM, BC7_UNORM_SRGB,

        // Unity TextureFormat.Alpha8 / GraphicsFormat.A8_UNorm extension.
        A8_UNORM,

        COUNT,
    }

    public enum TextureDimension : uint
    {
        Unknown,
        Texture1D,
        Texture1DArray,
        Texture2D,
        Texture2DArray,
        TextureCube,
        TextureCubeArray,
        Texture2DMS,
        Texture2DMSArray,
        Texture3D
    }

    [Flags]
    public enum FormatSupport : uint
    {
        None = 0,
        Buffer = 0x00000001,
        IndexBuffer = 0x00000002,
        VertexBuffer = 0x00000004,
        Texture = 0x00000008,
        DepthStencil = 0x00000010,
        RenderTarget = 0x00000020,
        Blendable = 0x00000040,
        ShaderLoad = 0x00000080,
        ShaderSample = 0x00000100,
        ShaderUavLoad = 0x00000200,
        ShaderUavStore = 0x00000400,
        ShaderAtomic = 0x00000800,
    }

    public enum CpuAccessMode : uint
    {
        None,
        Read,
        Write
    }

    public enum HeapType : uint
    {
        DeviceLocal,
        Upload,
        Readback,
    }

    /// <summary>Exact nvrhi::ObjectTypes identifiers used by GetNativeObject.</summary>
    public enum NativeObjectType : uint
    {
        SharedHandle = 0x00000001,
        D3D12_Device = 0x00020001,
        D3D12_CommandQueue = 0x00020002,
        D3D12_GraphicsCommandList = 0x00020003,
        D3D12_Resource = 0x00020004,
        D3D12_RenderTargetViewDescriptor = 0x00020005,
        D3D12_DepthStencilViewDescriptor = 0x00020006,
        D3D12_ShaderResourceViewGpuDescriptor = 0x00020007,
        D3D12_UnorderedAccessViewGpuDescriptor = 0x00020008,
        D3D12_RootSignature = 0x00020009,
        D3D12_PipelineState = 0x0002000a,
        D3D12_CommandAllocator = 0x0002000b,
        Nvrhi_D3D12_Device = 0x00020101,
        Nvrhi_D3D12_CommandList = 0x00020102,
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MemoryRequirements
    {
        public ulong Size;
        public ulong Alignment;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TiledTextureCoordinate
    {
        public ushort MipLevel;
        public ushort ArrayLevel;
        public uint X, Y, Z;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TiledTextureRegion
    {
        public uint TilesNum;
        public uint Width, Height, Depth;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PackedMipDesc
    {
        public uint NumStandardMips;
        public uint NumPackedMips;
        public uint NumTilesForPackedMips;
        public uint StartTileIndexInOverallResource;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TileShape
    {
        public uint WidthInTexels, HeightInTexels, DepthInTexels;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SubresourceTiling
    {
        public uint WidthInTiles, HeightInTiles, DepthInTiles;
        public uint StartTileIndexInOverallResource;
    }

    /// <summary>
    /// Mirrors nvrhi::ResourceStates. The values must match the native enum in
    /// RhiTypes.h - raw state bits travel in the command stream.
    /// </summary>
    [Flags]
    public enum ResourceStates : uint
    {
        Unknown                    = 0,
        Common                     = 0x00000001,
        ConstantBuffer             = 0x00000002,
        VertexBuffer               = 0x00000004,
        IndexBuffer                = 0x00000008,
        IndirectArgument           = 0x00000010,
        PixelShaderResource        = 0x00000020,
        NonPixelShaderResource     = 0x00000040,
        ShaderResource             = PixelShaderResource | NonPixelShaderResource,
        UnorderedAccess            = 0x00000080,
        RenderTarget               = 0x00000100,
        DepthWrite                 = 0x00000200,
        DepthRead                  = 0x00000400,
        StreamOut                  = 0x00000800,
        CopyDest                   = 0x00001000,
        CopySource                 = 0x00002000,
        ResolveDest                = 0x00004000,
        ResolveSource              = 0x00008000,
        Present                    = 0x00010000,
        AccelStructRead            = 0x00020000,
        AccelStructWrite           = 0x00040000,
        AccelStructBuildInput      = 0x00080000,
        AccelStructBuildBlas       = 0x00100000,
        ShadingRateSurface         = 0x00200000,
        OpacityMicromapWrite       = 0x00400000,
        OpacityMicromapBuildInput  = 0x00800000,
        ConvertCoopVecMatrixInput  = 0x01000000,
        ConvertCoopVecMatrixOutput = 0x02000000,
    }

    /// <summary>Mirrors nvrhi::SharedResourceFlags.</summary>
    [Flags]
    public enum SharedResourceFlags : uint
    {
        None                = 0,
        Shared              = 0x01,
        Shared_NTHandle     = 0x02,
        Shared_CrossAdapter = 0x04,
    }

    public enum SamplerAddressMode : uint
    {
        Clamp,
        Wrap,
        Border,
        Mirror,
        MirrorOnce,

        // Vulkan-style aliases, as in NVRHI
        ClampToEdge = Clamp,
        Repeat = Wrap,
        ClampToBorder = Border,
        MirroredRepeat = Mirror,
        MirrorClampToEdge = MirrorOnce
    }

    public enum SamplerReductionType : uint
    {
        Standard,
        Comparison,
        Minimum,
        Maximum
    }

    // Mirrors nvrhi::ShaderType.
    [Flags]
    public enum ShaderType : uint
    {
        None          = 0x0000,

        Compute       = 0x0020,

        Vertex        = 0x0001,
        Hull          = 0x0002,
        Domain        = 0x0004,
        Geometry      = 0x0008,
        Pixel         = 0x0010,
        Amplification = 0x0040,
        Mesh          = 0x0080,
        AllGraphics   = 0x00DF,

        RayGeneration = 0x0100,
        AnyHit        = 0x0200,
        ClosestHit    = 0x0400,
        Miss          = 0x0800,
        Intersection  = 0x1000,
        Callable      = 0x2000,
        AllRayTracing = 0x3F00,

        All           = 0x3FFF,
    }

    // Identifies the underlying resource type in a binding. Order and values
    // match nvrhi::ResourceType exactly (they cross the C ABI).
    public enum ResourceType : uint
    {
        None = 0,
        Texture_SRV,
        Texture_UAV,
        TypedBuffer_SRV,
        TypedBuffer_UAV,
        StructuredBuffer_SRV,
        StructuredBuffer_UAV,
        RawBuffer_SRV,
        RawBuffer_UAV,
        ConstantBuffer,
        VolatileConstantBuffer,
        Sampler,
        RayTracingAccelStruct,
        PushConstants,
        SamplerFeedbackTexture_UAV,

        Count
    }

    /// <summary>Mirrors nvrhi::Feature (names and values).</summary>
    public enum Feature : uint
    {
        ComputeQueue,
        ConservativeRasterization,
        ConstantBufferRanges,
        CopyQueue,
        DeferredCommandLists,
        FastGeometryShader,
        HeapDirectlyIndexed,
        HlslExtensionUAV,
        LinearSweptSpheres,
        Meshlets,
        RayQuery,
        RayTracingAccelStruct,
        RayTracingClusters,
        RayTracingOpacityMicromap,
        RayTracingPipeline,
        SamplerFeedback,
        ShaderExecutionReordering,
        ShaderSpecializations,
        SinglePassStereo,
        Spheres,
        VariableRateShading,
        VirtualResources,
        WaveLaneCountMinMax,
        CooperativeVectorInferencing,
        CooperativeVectorTraining,
        EnhancedBarriers,
    }

    public enum PrimitiveType : uint
    {
        PointList,
        LineList,
        LineStrip,
        TriangleList,
        TriangleStrip,
        TriangleFan,
        TriangleListWithAdjacency,
        TriangleStripWithAdjacency,
        PatchList,
    }

    public enum RasterCullMode : uint
    {
        Back,
        Front,
        None,
    }

    public enum RasterFillMode : uint
    {
        Solid,
        Wireframe,
    }

    /// <summary>Mirrors nvrhi::BufferRange.</summary>
    public struct BufferRange : IEquatable<BufferRange>
    {
        public ulong ByteOffset;
        public ulong ByteSize;

        public BufferRange(ulong byteOffset, ulong byteSize)
        {
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        /// <summary>Mirrors nvrhi::EntireBuffer.</summary>
        public static readonly BufferRange EntireBuffer = new BufferRange(0, ~0ul);

        public BufferRange Resolve(BufferDesc desc)
        {
            BufferRange result;
            result.ByteOffset = Math.Min(ByteOffset, desc.ByteSize);
            if (ByteSize == 0)
                result.ByteSize = desc.ByteSize - result.ByteOffset;
            else
                result.ByteSize = Math.Min(ByteSize, desc.ByteSize - result.ByteOffset);
            return result;
        }

        public bool IsEntireBuffer(BufferDesc desc) =>
            ByteOffset == 0 && (ByteSize == ~0ul || ByteSize == desc.ByteSize);

        public bool Equals(BufferRange other) => ByteOffset == other.ByteOffset && ByteSize == other.ByteSize;
    }

    /// <summary>Mirrors nvrhi::TextureSubresourceSet.</summary>
    public struct TextureSubresourceSet : IEquatable<TextureSubresourceSet>
    {
        public const uint AllMipLevels = ~0u;
        public const uint AllArraySlices = ~0u;

        public uint BaseMipLevel;
        public uint NumMipLevels;
        public uint BaseArraySlice;
        public uint NumArraySlices;

        public TextureSubresourceSet(uint baseMipLevel, uint numMipLevels, uint baseArraySlice, uint numArraySlices)
        {
            BaseMipLevel = baseMipLevel;
            NumMipLevels = numMipLevels;
            BaseArraySlice = baseArraySlice;
            NumArraySlices = numArraySlices;
        }

        /// <summary>Mirrors nvrhi::AllSubresources.</summary>
        public static readonly TextureSubresourceSet AllSubresources =
            new TextureSubresourceSet(0, AllMipLevels, 0, AllArraySlices);

        /// <summary>Default for single-mip views (nvrhi's BindingSetItem::Texture_UAV default).</summary>
        public static readonly TextureSubresourceSet FirstMipAllSlices =
            new TextureSubresourceSet(0, 1, 0, AllArraySlices);

        public TextureSubresourceSet Resolve(TextureDesc desc, bool singleMipLevel)
        {
            TextureSubresourceSet ret;
            ret.BaseMipLevel = BaseMipLevel;
            if (singleMipLevel)
            {
                ret.NumMipLevels = 1;
            }
            else
            {
                uint lastMipLevelPlusOne = Math.Min(unchecked(BaseMipLevel + NumMipLevels), desc.MipLevels);
                ret.NumMipLevels = lastMipLevelPlusOne > BaseMipLevel ? lastMipLevelPlusOne - BaseMipLevel : 0;
            }

            switch (desc.Dimension)
            {
                case TextureDimension.Texture1DArray:
                case TextureDimension.Texture2DArray:
                case TextureDimension.TextureCube:
                case TextureDimension.TextureCubeArray:
                case TextureDimension.Texture2DMSArray:
                    ret.BaseArraySlice = BaseArraySlice;
                    uint lastArraySlicePlusOne = Math.Min(unchecked(BaseArraySlice + NumArraySlices), desc.ArraySize);
                    ret.NumArraySlices = lastArraySlicePlusOne > BaseArraySlice ? lastArraySlicePlusOne - BaseArraySlice : 0;
                    break;
                default:
                    ret.BaseArraySlice = 0;
                    ret.NumArraySlices = 1;
                    break;
            }

            return ret;
        }

        public bool IsEntireTexture(TextureDesc desc) =>
            BaseMipLevel == 0 && (NumMipLevels == AllMipLevels || NumMipLevels >= desc.MipLevels) &&
            BaseArraySlice == 0 && (NumArraySlices == AllArraySlices || NumArraySlices >= desc.ArraySize);

        public bool Equals(TextureSubresourceSet other) =>
            BaseMipLevel == other.BaseMipLevel && NumMipLevels == other.NumMipLevels &&
            BaseArraySlice == other.BaseArraySlice && NumArraySlices == other.NumArraySlices;
    }

    /// <summary>Mirrors nvrhi::TextureSlice (a section of one mip/array slice).</summary>
    public struct TextureSlice
    {
        public uint X;
        public uint Y;
        public uint Z;
        // ~0u means the entire dimension is part of the region.
        public uint Width;
        public uint Height;
        public uint Depth;
        public uint MipLevel;
        public uint ArraySlice;

        public static TextureSlice Default => new TextureSlice
        {
            Width = ~0u,
            Height = ~0u,
            Depth = ~0u,
        };

        public TextureSlice Resolve(TextureDesc desc)
        {
            TextureSlice ret = this;
            uint mipWidth = Math.Max(desc.Width >> (int)MipLevel, 1u);
            uint mipHeight = Math.Max(desc.Height >> (int)MipLevel, 1u);
            uint mipDepth = Math.Max(desc.Depth >> (int)MipLevel, 1u);
            if (Width == ~0u) ret.Width = mipWidth - X;
            if (Height == ~0u) ret.Height = mipHeight - Y;
            if (Depth == ~0u) ret.Depth = mipDepth - Z;
            return ret;
        }
    }
}
