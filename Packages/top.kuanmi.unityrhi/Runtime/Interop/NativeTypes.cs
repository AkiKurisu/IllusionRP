using System;
using System.Runtime.InteropServices;

namespace UnityRhi.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiHeapDescNative
    {
        public ulong capacity;
        public HeapType type;

        public static RhiHeapDescNative FromManaged(HeapDesc d) => new RhiHeapDescNative
        {
            capacity = d.Capacity,
            type = d.Type,
        };
    }

    // Blittable mirrors of RenderingPlugin/Source/RhiTypes.h. Field order and
    // sizes must match exactly; all native bools/enums are 32-bit.

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiBufferDescNative
    {
        public ulong byteSize;
        public uint structStride;
        public uint maxVersions;
        public uint format;
        public uint cpuAccess;
        public uint canHaveUAVs;
        public uint canHaveTypedViews;
        public uint canHaveRawViews;
        public uint isVertexBuffer;
        public uint isIndexBuffer;
        public uint isConstantBuffer;
        public uint isDrawIndirectArgs;
        public uint isAccelStructBuildInput;
        public uint isAccelStructStorage;
        public uint isShaderBindingTable;
        public uint isVolatile;
        public uint isVirtual;
        public uint initialState;
        public uint keepInitialState;
        public uint sharedResourceFlags;

        public static RhiBufferDescNative FromManaged(BufferDesc d) => new RhiBufferDescNative
        {
            byteSize = d.ByteSize,
            structStride = d.StructStride,
            maxVersions = d.MaxVersions,
            format = (uint)d.Format,
            cpuAccess = (uint)d.CpuAccess,
            canHaveUAVs = d.CanHaveUAVs ? 1u : 0u,
            canHaveTypedViews = d.CanHaveTypedViews ? 1u : 0u,
            canHaveRawViews = d.CanHaveRawViews ? 1u : 0u,
            isVertexBuffer = d.IsVertexBuffer ? 1u : 0u,
            isIndexBuffer = d.IsIndexBuffer ? 1u : 0u,
            isConstantBuffer = d.IsConstantBuffer ? 1u : 0u,
            isDrawIndirectArgs = d.IsDrawIndirectArgs ? 1u : 0u,
            isAccelStructBuildInput = d.IsAccelStructBuildInput ? 1u : 0u,
            isAccelStructStorage = d.IsAccelStructStorage ? 1u : 0u,
            isShaderBindingTable = d.IsShaderBindingTable ? 1u : 0u,
            isVolatile = d.IsVolatile ? 1u : 0u,
            isVirtual = d.IsVirtual ? 1u : 0u,
            initialState = (uint)d.InitialState,
            keepInitialState = d.KeepInitialState ? 1u : 0u,
            sharedResourceFlags = (uint)d.SharedResourceFlags,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiTextureDescNative
    {
        public uint width;
        public uint height;
        public uint depth;
        public uint arraySize;
        public uint mipLevels;
        public uint sampleCount;
        public uint sampleQuality;
        public uint format;
        public uint dimension;
        public uint isShaderResource;
        public uint isRenderTarget;
        public uint isUAV;
        public uint isTypeless;
        public uint isShadingRateSurface;
        public uint sharedResourceFlags;
        public uint isVirtual;
        public uint isTiled;
        public float clearR, clearG, clearB, clearA;
        public uint useClearValue;
        public uint initialState;
        public uint keepInitialState;

        public static RhiTextureDescNative FromManaged(TextureDesc d) => new RhiTextureDescNative
        {
            width = d.Width,
            height = d.Height,
            depth = d.Depth,
            arraySize = d.ArraySize,
            mipLevels = d.MipLevels,
            sampleCount = d.SampleCount,
            sampleQuality = d.SampleQuality,
            format = (uint)d.Format,
            dimension = (uint)d.Dimension,
            isShaderResource = d.IsShaderResource ? 1u : 0u,
            isRenderTarget = d.IsRenderTarget ? 1u : 0u,
            isUAV = d.IsUAV ? 1u : 0u,
            isTypeless = d.IsTypeless ? 1u : 0u,
            isShadingRateSurface = d.IsShadingRateSurface ? 1u : 0u,
            sharedResourceFlags = (uint)d.SharedResourceFlags,
            isVirtual = d.IsVirtual ? 1u : 0u,
            isTiled = d.IsTiled ? 1u : 0u,
            clearR = d.ClearValue.r,
            clearG = d.ClearValue.g,
            clearB = d.ClearValue.b,
            clearA = d.ClearValue.a,
            useClearValue = d.UseClearValue ? 1u : 0u,
            initialState = (uint)d.InitialState,
            keepInitialState = d.KeepInitialState ? 1u : 0u,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiSamplerDescNative
    {
        public float borderR, borderG, borderB, borderA;
        public float maxAnisotropy;
        public float mipBias;
        public float minLod;
        public float maxLod;
        public uint minFilter;
        public uint magFilter;
        public uint mipFilter;
        public uint addressU;
        public uint addressV;
        public uint addressW;
        public uint reductionType;

        public static RhiSamplerDescNative FromManaged(SamplerDesc d) => new RhiSamplerDescNative
        {
            borderR = d.BorderColor.r,
            borderG = d.BorderColor.g,
            borderB = d.BorderColor.b,
            borderA = d.BorderColor.a,
            maxAnisotropy = d.MaxAnisotropy,
            mipBias = d.MipBias,
            minLod = d.MinLod,
            maxLod = d.MaxLod,
            minFilter = d.MinFilter ? 1u : 0u,
            magFilter = d.MagFilter ? 1u : 0u,
            mipFilter = d.MipFilter ? 1u : 0u,
            addressU = (uint)d.AddressU,
            addressV = (uint)d.AddressV,
            addressW = (uint)d.AddressW,
            reductionType = (uint)d.ReductionType,
        };
    }

    /// <summary>Mirrors unityrhi::CommandStreamDecodeInfo (CommandStream.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CommandStreamInfoNative
    {
        public const int MaxOpcode = 64;

        public uint Ok;
        public uint CommandCount;
        public uint ByteSize;
        public fixed uint OpcodeCounts[MaxOpcode];
        public fixed uint OpcodeBytes[MaxOpcode];
    }

    /// <summary>Live-object counters reported by the native device.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceStats
    {
        public uint LiveBuffers;
        public uint LiveTextures;
        public uint LiveSamplers;
        public uint LiveShaders;
        public uint LiveBindingLayouts;
        public uint LiveBindingSets;
        public uint LiveComputePipelines;
        public uint LiveShaderLibraries;
        public uint LiveAccelStructs;
        public uint LiveRayTracingPipelines;
        public uint LiveShaderTables;
        public uint LiveDescriptorTables;
        public uint LiveInputLayouts;
        public uint LiveFramebuffers;
        public uint LiveGraphicsPipelines;
        public uint LiveHeaps;
        public uint LiveStagingTextures;
        public uint LiveEventQueries;
        public uint LiveTimerQueries;
        public uint PendingReleases;
    }

    public enum ResourceKind : uint
    {
        Buffer, Texture, Sampler, Shader, BindingLayout, BindingSet, ComputePipeline,
        ShaderLibrary, AccelStruct, RayTracingPipeline, ShaderTable, DescriptorTable,
        InputLayout, Framebuffer, GraphicsPipeline, Heap, StagingTexture, EventQuery,
        TimerQuery
    }

    [Flags]
    public enum ResourceInfoFlags : uint
    {
        None = 0,
        PendingRelease = 1u << 0,
        UnityOwned = 1u << 1,
        HasNativeResource = 1u << 2,
        Placed = 1u << 3,
        Virtual = 1u << 4,
        Tiled = 1u << 5,
        CpuRead = 1u << 6,
        CpuWrite = 1u << 7,
        UnorderedAccess = 1u << 8,
        RenderTarget = 1u << 9,
        ShaderResource = 1u << 10,
        InternalBacking = 1u << 11,
        GpuUseCompleted = 1u << 31
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ResourceInfoNative
    {
        public ulong Handle;
        public ulong LogicalSize;
        public ulong AllocationSize;
        public ResourceKind Kind;
        public ResourceInfoFlags Flags;
        public uint LastUseInstance;
        public uint ReleaseInstance;
        public uint Width;
        public uint Height;
        public uint Depth;
        public uint ArraySize;
        public uint MipLevels;
        public uint SampleCount;
        public Format Format;
        public ResourceStates InitialState;
        public ResourceStates PermanentState;
        public uint Detail0;
        public uint Detail1;
        public fixed byte DebugName[128];

        public string GetDebugName()
        {
            fixed (byte* name = DebugName)
            {
                int length = 0;
                while (length < 128 && name[length] != 0)
                    ++length;
                return System.Text.Encoding.UTF8.GetString(name, length);
            }
        }

        public ResourceInfo ToManaged() => new ResourceInfo
        {
            Handle = Handle,
            LogicalSize = LogicalSize,
            AllocationSize = AllocationSize,
            Kind = Kind,
            Flags = Flags,
            LastUseInstance = LastUseInstance,
            ReleaseInstance = ReleaseInstance,
            Width = Width,
            Height = Height,
            Depth = Depth,
            ArraySize = ArraySize,
            MipLevels = MipLevels,
            SampleCount = SampleCount,
            Format = Format,
            InitialState = InitialState,
            PermanentState = PermanentState,
            Detail0 = Detail0,
            Detail1 = Detail1,
            DebugName = GetDebugName()
        };
    }

    public struct ResourceInfo
    {
        public ulong Handle;
        public ulong LogicalSize;
        public ulong AllocationSize;
        public ResourceKind Kind;
        public ResourceInfoFlags Flags;
        public uint LastUseInstance;
        public uint ReleaseInstance;
        public uint Width, Height, Depth, ArraySize, MipLevels, SampleCount;
        public Format Format;
        public ResourceStates InitialState, PermanentState;
        public uint Detail0, Detail1;
        public string DebugName;

        public bool IsPending => (Flags & ResourceInfoFlags.PendingRelease) != 0;
        public bool IsUnityOwned => (Flags & ResourceInfoFlags.UnityOwned) != 0;
        public bool IsPlaced => (Flags & ResourceInfoFlags.Placed) != 0;
        public bool HasInternalBacking => (Flags & ResourceInfoFlags.InternalBacking) != 0;
    }

    // Mirrors nvrhi::BindingLayoutItem { slot, type, size }; fields are
    // widened to uint32 instead of nvrhi's 8-byte bitfield packing.
    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiBindingLayoutItemNative
    {
        public uint slot;
        public uint type;
        public uint size;

        public static RhiBindingLayoutItemNative FromManaged(BindingLayoutItem item) => new RhiBindingLayoutItemNative
        {
            slot = item.Slot,
            type = (uint)item.Type,
            size = item.Size,
        };
    }

    // Layout-level parameters of nvrhi::BindingLayoutDesc (the items travel
    // as a separate array).
    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiBindingLayoutDescNative
    {
        public uint visibility;
        public uint registerSpace;
        public uint registerSpaceIsDescriptorSet;

        public static RhiBindingLayoutDescNative FromManaged(BindingLayoutDesc d) => new RhiBindingLayoutDescNative
        {
            visibility = (uint)d.Visibility,
            registerSpace = d.RegisterSpace,
            registerSpaceIsDescriptorSet = d.RegisterSpaceIsDescriptorSet ? 1u : 0u,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiBindlessLayoutDescNative
    {
        public uint visibility;
        public uint firstSlot;
        public uint maxCapacity;
        public uint layoutType;

        public static RhiBindlessLayoutDescNative FromManaged(BindlessLayoutDesc d) => new RhiBindlessLayoutDescNative
        {
            visibility = (uint)d.Visibility,
            firstSlot = d.FirstSlot,
            maxCapacity = d.MaxCapacity,
            layoutType = (uint)d.Type,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiVertexAttributeDescNative
    {
        public System.IntPtr name;
        public uint semanticIndex;
        public uint format;
        public uint bufferIndex;
        public uint offset;
        public uint elementStride;
        public uint isInstanced;

        public static RhiVertexAttributeDescNative FromManaged(VertexAttributeDesc d, System.IntPtr namePtr) =>
            new RhiVertexAttributeDescNative
            {
                name = namePtr,
                semanticIndex = d.SemanticIndex,
                format = (uint)d.Format,
                bufferIndex = d.BufferIndex,
                offset = d.Offset,
                elementStride = d.ElementStride,
                isInstanced = d.IsInstanced ? 1u : 0u,
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiGraphicsPipelineDescNative
    {
        public uint primType;
        public uint cullMode;
        public uint fillMode;
        public uint frontCounterClockwise;
        public uint depthClipEnable;
        public uint depthTestEnable;
        public uint depthWriteEnable;
        public uint depthFunc;
        public uint blendEnable;
        public uint srcBlend;
        public uint destBlend;
        public uint blendOp;
        public uint srcBlendAlpha;
        public uint destBlendAlpha;
        public uint blendOpAlpha;
        public uint colorWriteMask;

        public static RhiGraphicsPipelineDescNative FromManaged(GraphicsPipelineDesc d) =>
            new RhiGraphicsPipelineDescNative
            {
                primType = (uint)d.PrimType,
                cullMode = (uint)d.CullMode,
                fillMode = (uint)d.FillMode,
                frontCounterClockwise = d.FrontCounterClockwise ? 1u : 0u,
                depthClipEnable = d.DepthClipEnable ? 1u : 0u,
                depthTestEnable = d.DepthTestEnable ? 1u : 0u,
                depthWriteEnable = d.DepthWriteEnable ? 1u : 0u,
                depthFunc = d.DepthFunc,
                blendEnable = d.BlendEnable ? 1u : 0u,
                srcBlend = d.SrcBlend,
                destBlend = d.DestBlend,
                blendOp = d.BlendOp,
                srcBlendAlpha = d.SrcBlendAlpha,
                destBlendAlpha = d.DestBlendAlpha,
                blendOpAlpha = d.BlendOpAlpha,
                colorWriteMask = d.ColorWriteMask,
            };
    }

    // Mirrors nvrhi::BindingSetItem. rawData carries the union payload:
    // BufferRange {byteOffset, byteSize} for buffers, or the packed
    // TextureSubresourceSet for textures.
    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiBindingSetItemNative
    {
        public System.IntPtr resource;
        public uint slot;
        public uint arrayElement;
        public uint type;
        public uint dimension;
        public uint format;
        public uint unused;
        public ulong rawData0;
        public ulong rawData1;

        public static RhiBindingSetItemNative FromManaged(BindingSetItem item)
        {
            var result = new RhiBindingSetItemNative
            {
                resource = item.ResourceHandle?.Handle ?? System.IntPtr.Zero,
                slot = item.Slot,
                arrayElement = item.ArrayElement,
                type = (uint)item.Type,
                dimension = (uint)item.Dimension,
                format = (uint)item.Format,
            };

            switch (item.Type)
            {
                case ResourceType.Texture_SRV:
                case ResourceType.Texture_UAV:
                case ResourceType.SamplerFeedbackTexture_UAV:
                    result.rawData0 = item.Subresources.BaseMipLevel | ((ulong)item.Subresources.NumMipLevels << 32);
                    result.rawData1 = item.Subresources.BaseArraySlice | ((ulong)item.Subresources.NumArraySlices << 32);
                    break;
                default:
                    result.rawData0 = item.Range.ByteOffset;
                    result.rawData1 = item.Range.ByteSize;
                    break;
            }

            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RhiRtGeometryTrianglesNative
    {
        public ulong indexBuffer;
        public ulong vertexBuffer;
        public uint indexFormat;
        public uint vertexFormat;
        public ulong indexOffset;
        public ulong vertexOffset;
        public uint indexCount;
        public uint vertexCount;
        public uint vertexStride;
        public uint padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiRtGeometryAABBsNative
    {
        public ulong buffer;
        public ulong unused;
        public ulong offset;
        public uint count;
        public uint stride;
    }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct RhiRtGeometryDataNative
    {
        [FieldOffset(0)] public RhiRtGeometryTrianglesNative triangles;
        [FieldOffset(0)] public RhiRtGeometryAABBsNative aabbs;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RhiRtGeometryDescNative
    {
        public RhiRtGeometryDataNative geometryData;
        public uint useTransform;
        public fixed float transform[12];
        public uint flags;
        public uint geometryType;
        public uint padding;

        public static RhiRtGeometryDescNative FromManaged(Rt.GeometryDesc d)
        {
            var native = new RhiRtGeometryDescNative
            {
                useTransform = d.UseTransform ? 1u : 0u,
                flags = (uint)d.Flags,
                geometryType = (uint)d.GeometryType,
            };

            if (d.GeometryType == Rt.GeometryType.Triangles)
            {
                native.geometryData.triangles = new RhiRtGeometryTrianglesNative
                {
                    indexBuffer = d.Triangles.IndexBuffer != null ? unchecked((ulong)d.Triangles.IndexBuffer.Handle.ToInt64()) : 0ul,
                    vertexBuffer = d.Triangles.VertexBuffer != null ? unchecked((ulong)d.Triangles.VertexBuffer.Handle.ToInt64()) : 0ul,
                    indexFormat = (uint)d.Triangles.IndexFormat,
                    vertexFormat = (uint)d.Triangles.VertexFormat,
                    indexOffset = d.Triangles.IndexOffset,
                    vertexOffset = d.Triangles.VertexOffset,
                    indexCount = d.Triangles.IndexCount,
                    vertexCount = d.Triangles.VertexCount,
                    vertexStride = d.Triangles.VertexStride,
                };
            }
            else
            {
                native.geometryData.aabbs = new RhiRtGeometryAABBsNative
                {
                    buffer = d.AABBs.Buffer != null ? unchecked((ulong)d.AABBs.Buffer.Handle.ToInt64()) : 0ul,
                    offset = d.AABBs.Offset,
                    count = d.AABBs.Count,
                    stride = d.AABBs.Stride,
                };
            }

            for (int i = 0; i < 12; ++i)
                native.transform[i] = d.Transform != null && i < d.Transform.Length ? d.Transform[i] : (i % 5 == 0 ? 1f : 0f);

            return native;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RhiRtInstanceDescNative
    {
        public fixed float transform[12];
        public uint instanceIdAndMask;
        public uint instanceContributionAndFlags;
        public ulong bottomLevelAS;

        public static void WriteFromManaged(in Rt.InstanceDesc d, ref RhiRtInstanceDescNative native)
        {
            native.instanceIdAndMask = (d.InstanceID & 0x00FFFFFFu) | (d.InstanceMask << 24);
            native.instanceContributionAndFlags =
                (d.InstanceContributionToHitGroupIndex & 0x00FFFFFFu) | ((uint)d.Flags << 24);
            native.bottomLevelAS = d.BottomLevelAS != null
                ? unchecked((ulong)d.BottomLevelAS.Handle.ToInt64())
                : 0ul;

            bool isDefault = d.Transform.M00 == 0f && d.Transform.M01 == 0f && d.Transform.M02 == 0f && d.Transform.M03 == 0f &&
                d.Transform.M10 == 0f && d.Transform.M11 == 0f && d.Transform.M12 == 0f && d.Transform.M13 == 0f &&
                d.Transform.M20 == 0f && d.Transform.M21 == 0f && d.Transform.M22 == 0f && d.Transform.M23 == 0f;
            if (isDefault)
            {
                native.transform[0] = 1f; native.transform[1] = 0f; native.transform[2] = 0f; native.transform[3] = 0f;
                native.transform[4] = 0f; native.transform[5] = 1f; native.transform[6] = 0f; native.transform[7] = 0f;
                native.transform[8] = 0f; native.transform[9] = 0f; native.transform[10] = 1f; native.transform[11] = 0f;
                return;
            }

            native.transform[0] = d.Transform.M00; native.transform[1] = d.Transform.M01;
            native.transform[2] = d.Transform.M02; native.transform[3] = d.Transform.M03;
            native.transform[4] = d.Transform.M10; native.transform[5] = d.Transform.M11;
            native.transform[6] = d.Transform.M12; native.transform[7] = d.Transform.M13;
            native.transform[8] = d.Transform.M20; native.transform[9] = d.Transform.M21;
            native.transform[10] = d.Transform.M22; native.transform[11] = d.Transform.M23;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiRtAccelStructDescNative
    {
        public ulong topLevelMaxInstances;
        public uint buildFlags;
        public uint isTopLevel;
        public uint isVirtual;

        public static RhiRtAccelStructDescNative FromManaged(Rt.AccelStructDesc d) => new RhiRtAccelStructDescNative
        {
            topLevelMaxInstances = d.TopLevelMaxInstances,
            buildFlags = (uint)d.BuildFlags,
            isTopLevel = d.IsTopLevel ? 1u : 0u,
            isVirtual = d.IsVirtual ? 1u : 0u,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiRtPipelineShaderDescNative
    {
        public System.IntPtr exportName;
        public ulong shader;
        public ulong bindingLayout;

        public static RhiRtPipelineShaderDescNative FromManaged(Rt.PipelineShaderDesc d, System.IntPtr exportNamePtr) => new RhiRtPipelineShaderDescNative
        {
            exportName = exportNamePtr,
            shader = d.Shader != null ? unchecked((ulong)d.Shader.Handle.ToInt64()) : 0ul,
            bindingLayout = d.BindingLayout != null ? unchecked((ulong)d.BindingLayout.Handle.ToInt64()) : 0ul,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiRtPipelineHitGroupDescNative
    {
        public System.IntPtr exportName;
        public ulong closestHitShader;
        public ulong anyHitShader;
        public ulong intersectionShader;
        public ulong bindingLayout;
        public uint isProceduralPrimitive;

        public static RhiRtPipelineHitGroupDescNative FromManaged(Rt.PipelineHitGroupDesc d, System.IntPtr exportNamePtr) => new RhiRtPipelineHitGroupDescNative
        {
            exportName = exportNamePtr,
            closestHitShader = d.ClosestHitShader != null ? unchecked((ulong)d.ClosestHitShader.Handle.ToInt64()) : 0ul,
            anyHitShader = d.AnyHitShader != null ? unchecked((ulong)d.AnyHitShader.Handle.ToInt64()) : 0ul,
            intersectionShader = d.IntersectionShader != null ? unchecked((ulong)d.IntersectionShader.Handle.ToInt64()) : 0ul,
            bindingLayout = d.BindingLayout != null ? unchecked((ulong)d.BindingLayout.Handle.ToInt64()) : 0ul,
            isProceduralPrimitive = d.IsProceduralPrimitive ? 1u : 0u,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiRtPipelineDescNative
    {
        public uint maxPayloadSize;
        public uint maxAttributeSize;
        public uint maxRecursionDepth;
        public uint allowOpacityMicromaps;
        public int hlslExtensionsUAV;

        public static RhiRtPipelineDescNative FromManaged(Rt.PipelineDesc d) => new RhiRtPipelineDescNative
        {
            maxPayloadSize = d.MaxPayloadSize,
            maxAttributeSize = d.MaxAttributeSize,
            maxRecursionDepth = d.MaxRecursionDepth,
            allowOpacityMicromaps = d.AllowOpacityMicromaps ? 1u : 0u,
            hlslExtensionsUAV = d.HlslExtensionsUAV,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RhiRtShaderTableDescNative
    {
        public uint isCached;
        public uint maxEntries;

        public static RhiRtShaderTableDescNative FromManaged(Rt.ShaderTableDesc d) => new RhiRtShaderTableDescNative
        {
            isCached = d.IsCached ? 1u : 0u,
            maxEntries = d.MaxEntries,
        };
    }
}
