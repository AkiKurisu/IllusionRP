using System;
using UnityRhi.Interop;
using UnityEngine;

namespace UnityRhi
{
    public sealed class Heap : Resource
    {
        public HeapDesc Desc { get; }

        internal Heap(IntPtr handle, HeapDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }
    }

    public sealed class EventQuery : Resource
    {
        internal EventQuery(IntPtr handle) : base(handle, "EventQuery") { }
    }

    public sealed class TimerQuery : Resource
    {
        internal TimerQuery(IntPtr handle) : base(handle, "TimerQuery") { }
    }

    /// <summary>
    /// Base of all RHI objects; wraps an opaque native handle (nvrhi::IResource
    /// analog). Dispose releases the native reference; actual destruction is
    /// deferred until the GPU-carried lifetime marker passes the resource's last use.
    /// </summary>
    public abstract class Resource : IDisposable
    {
        internal IntPtr Handle { get; private set; }

        /// <summary>
        /// Stable opaque identity for cache keys and extension descriptors.
        /// This is not a D3D12 native object and must not be dereferenced.
        /// </summary>
        public ulong OpaqueIdentity => unchecked((ulong)Handle.ToInt64());
        private string _debugName;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private string _disposeStack;
#endif

        internal string DebugName => _debugName;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal string DisposeStack => _disposeStack;
#else
        internal string DisposeStack => null;
#endif

        private protected Resource(IntPtr handle, string debugName)
        {
            Handle = handle;
            _debugName = debugName;
        }

        public bool IsValid => Handle != IntPtr.Zero;

        /// <summary>Returns the raw nvrhi Object value without adding a native reference.</summary>
        public ulong GetNativeObject(NativeObjectType objectType)
        {
            if (Handle == IntPtr.Zero)
                throw new ObjectDisposedException(GetType().Name);
            return NativeMethods.UnityRhiGetNativeObject(Handle, objectType);
        }

        public void Dispose()
        {
            ReleaseHandle(captureDisposeStack: true);
            GC.SuppressFinalize(this);
        }

        ~Resource()
        {
            if (Handle != IntPtr.Zero)
            {
                Debug.LogWarning($"UnityRHI: {GetType().Name} '{_debugName}' was garbage-collected " +
                                 "without Dispose(); releasing from the finalizer.");
                ReleaseHandle(captureDisposeStack: false);
            }
        }

        private void ReleaseHandle(bool captureDisposeStack)
        {
            if (Handle == IntPtr.Zero)
                return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Retained command streams keep the managed Resource object alive. If
            // submission later discovers that its native handle was released, this
            // stack identifies the exact premature Dispose call. Keep this out of
            // non-development players because Environment.StackTrace is relatively
            // expensive during bulk resource retirement.
            if (captureDisposeStack)
                _disposeStack = Environment.StackTrace;
#endif
            NativeMethods.UnityRhiReleaseResource(Handle);
            Handle = IntPtr.Zero;
        }
    }

    /// <summary>Mirrors nvrhi::IBuffer.</summary>
    public sealed class Buffer : Resource
    {
        public BufferDesc Desc { get; }

        internal Buffer(IntPtr handle, BufferDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }

        public IntPtr NativeResource => NativeMethods.UnityRhiGetBufferNativeResource(Handle);
        public ulong GpuVirtualAddress => NativeMethods.UnityRhiGetBufferGpuVirtualAddress(Handle);

        public unsafe void Write<T>(T[] data) where T : unmanaged
        {
            if (Desc.CpuAccess != CpuAccessMode.Write)
                throw new InvalidOperationException("Buffer.Write requires CpuAccessMode.Write.");
            int byteCount = data.Length * sizeof(T);
            if ((ulong)byteCount > Desc.ByteSize)
                throw new ArgumentOutOfRangeException(nameof(data));
            IntPtr ptr = NativeMethods.UnityRhiMapBuffer(Handle);
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException("UnityRHI: MapBuffer failed.");
            try
            {
                fixed (T* src = data)
                    System.Buffer.MemoryCopy(src, (void*)ptr, (long)Desc.ByteSize, byteCount);
            }
            finally
            {
                NativeMethods.UnityRhiUnmapBuffer(Handle);
            }
        }

        public unsafe T[] Read<T>(int count) where T : unmanaged
        {
            var result = new T[count];
            Read(result, count);
            return result;
        }

        public unsafe void Read<T>(T[] destination, int count) where T : unmanaged
        {
            if (Desc.CpuAccess != CpuAccessMode.Read)
                throw new InvalidOperationException("Buffer.Read requires CpuAccessMode.Read.");
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (count < 0 || count > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            int byteCount = count * sizeof(T);
            if ((ulong)byteCount > Desc.ByteSize)
                throw new ArgumentOutOfRangeException(nameof(count));
            IntPtr ptr = NativeMethods.UnityRhiMapBuffer(Handle);
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException("UnityRHI: MapBuffer failed.");
            try
            {
                fixed (T* dst = destination)
                    System.Buffer.MemoryCopy((void*)ptr, dst, byteCount, byteCount);
            }
            finally
            {
                NativeMethods.UnityRhiUnmapBuffer(Handle);
            }
        }
    }

    /// <summary>Mirrors nvrhi::ITexture.</summary>
    public sealed class Texture : Resource
    {
        public TextureDesc Desc { get; }

        internal IntPtr NativeResource => NativeMethods.UnityRhiGetTextureNativeResource(Handle);

        internal Texture(IntPtr handle, TextureDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }

        public ulong GetNativeView(NativeObjectType objectType, Format format = Format.UNKNOWN,
            TextureSubresourceSet subresources = default,
            TextureDimension dimension = TextureDimension.Unknown, bool isReadOnlyDSV = false)
        {
            if (subresources.NumMipLevels == 0 && subresources.NumArraySlices == 0)
                subresources = TextureSubresourceSet.AllSubresources;
            return NativeMethods.UnityRhiGetTextureNativeView(
                Handle, objectType, format, ref subresources, dimension, isReadOnlyDSV ? 1u : 0u);
        }
    }

    /// <summary>CPU-visible texture footprint storage, mirroring nvrhi::IStagingTexture.</summary>
    public sealed class StagingTexture : Resource
    {
        public TextureDesc Desc { get; }
        public CpuAccessMode CpuAccess { get; }

        internal StagingTexture(IntPtr handle, TextureDesc desc, CpuAccessMode cpuAccess)
            : base(handle, desc.DebugName)
        {
            Desc = desc;
            CpuAccess = cpuAccess;
        }
    }

    /// <summary>Mirrors nvrhi::ISampler.</summary>
    public sealed class Sampler : Resource
    {
        public SamplerDesc Desc { get; }

        internal Sampler(IntPtr handle, SamplerDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }
    }

    /// <summary>Mirrors nvrhi::IShader (immutable DXIL bytecode + metadata).</summary>
    public sealed class Shader : Resource
    {
        public ShaderDesc Desc { get; }

        internal Shader(IntPtr handle, ShaderDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }
    }

    public sealed class ShaderLibrary : Resource
    {
        internal ShaderLibrary(IntPtr handle, string debugName) : base(handle, debugName) { }

        public Shader GetShader(string entryName, ShaderType shaderType, string debugName = "")
        {
            IntPtr handle = NativeMethods.UnityRhiCreateLibraryShader(Handle, (uint)shaderType, entryName, debugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: ShaderLibrary.GetShader '{entryName}' failed (see console).");
            return new Shader(handle, new ShaderDesc
            {
                EntryName = entryName,
                ShaderType = shaderType,
                DebugName = debugName,
            });
        }
    }

    public sealed class BindingLayout : Resource
    {
        public BindingLayoutDesc Desc { get; }

        internal BindingLayout(IntPtr handle, BindingLayoutDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }
    }

    public sealed class DescriptorTable : Resource
    {
        private readonly BindingLayout _layout;

        internal DescriptorTable(IntPtr handle, BindingLayout layout, string debugName) : base(handle, debugName)
        {
            _layout = layout;
        }

        public uint FirstDescriptorIndexInHeap =>
            NativeMethods.UnityRhiGetDescriptorTableFirstDescriptorIndexInHeap(Handle);

        public void Resize(uint newSize, bool keepContents = true) =>
            NativeMethods.UnityRhiResizeDescriptorTable(Handle, newSize, keepContents ? 1u : 0u);

        public void Write(BindingSetItem binding)
        {
            var native = RhiBindingSetItemNative.FromManaged(binding);
            if (NativeMethods.UnityRhiWriteDescriptorTable(Handle, ref native) == 0)
                throw new InvalidOperationException("UnityRHI: WriteDescriptorTable failed (see console).");
        }
    }

    public sealed class BindingSet : Resource
    {
        public BindingSetDesc Desc { get; }
        private readonly BindingLayout _layout;
        private readonly Resource[] _resources;

        internal BindingSet(IntPtr handle, BindingSetDesc desc, BindingLayout layout) : base(handle, desc.DebugName)
        {
            Desc = desc;
            _layout = layout;
            _resources = new Resource[desc.Bindings.Length];
            for (int i = 0; i < desc.Bindings.Length; ++i)
                _resources[i] = desc.Bindings[i].ResourceHandle;
        }
    }

    public sealed class ComputePipeline : Resource
    {
        public ComputePipelineDesc Desc { get; }
        private readonly Shader _shader;
        private readonly BindingLayout[] _layouts;

        internal ComputePipeline(IntPtr handle, ComputePipelineDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
            _shader = desc.CS;
            _layouts = desc.BindingLayouts;
        }
    }

    public sealed class InputLayout : Resource
    {
        public VertexAttributeDesc[] Attributes { get; }

        internal InputLayout(IntPtr handle, VertexAttributeDesc[] attributes, string debugName)
            : base(handle, debugName)
        {
            Attributes = attributes;
        }
    }

    public sealed class Framebuffer : Resource
    {
        public FramebufferDesc Desc { get; }
        private readonly Texture[] _colorAttachments;
        private readonly Texture _depthAttachment;

        internal Framebuffer(IntPtr handle, FramebufferDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
            _colorAttachments = desc.ColorAttachments;
            _depthAttachment = desc.DepthAttachment;
        }
    }

    public sealed class GraphicsPipeline : Resource
    {
        public GraphicsPipelineDesc Desc { get; }
        private readonly Shader _vs;
        private readonly Shader _ps;
        private readonly InputLayout _inputLayout;
        private readonly BindingLayout[] _layouts;
        private readonly Framebuffer _framebuffer;

        internal GraphicsPipeline(IntPtr handle, GraphicsPipelineDesc desc, Framebuffer framebuffer) : base(handle, desc.DebugName)
        {
            Desc = desc;
            _vs = desc.VS;
            _ps = desc.PS;
            _inputLayout = desc.InputLayout;
            _layouts = desc.BindingLayouts;
            _framebuffer = framebuffer;
        }
    }

    public sealed class AccelStruct : Resource
    {
        public Rt.AccelStructDesc Desc { get; }

        /// <summary>
        /// D3D12 GPU virtual address of the acceleration structure storage, as
        /// written into GPU-authored instance descriptors for
        /// <see cref="CommandList.BuildTopLevelAccelStructFromBuffer"/>.
        /// </summary>
        public ulong GpuVirtualAddress => NativeMethods.UnityRhiGetAccelStructGpuVirtualAddress(Handle);

        internal AccelStruct(IntPtr handle, Rt.AccelStructDesc desc) : base(handle, desc.DebugName)
        {
            Desc = desc;
        }
    }

    public sealed class RayTracingPipeline : Resource
    {
        public Rt.PipelineDesc Desc { get; }
        private readonly Resource[] _retainedResources;

        internal RayTracingPipeline(IntPtr handle, Rt.PipelineDesc desc, Resource[] retainedResources)
            : base(handle, desc.DebugName)
        {
            Desc = desc;
            _retainedResources = retainedResources;
        }
    }

    public sealed class ShaderTable : Resource
    {
        public Rt.ShaderTableDesc Desc { get; }
        private readonly RayTracingPipeline _pipeline;

        internal ShaderTable(IntPtr handle, Rt.ShaderTableDesc desc, RayTracingPipeline pipeline)
            : base(handle, desc.DebugName)
        {
            Desc = desc;
            _pipeline = pipeline;
        }

        public void SetRayGenerationShader(string exportName, BindingSet bindings = null) =>
            NativeMethods.UnityRhiShaderTableSetRayGenerationShader(Handle, exportName, bindings?.Handle ?? IntPtr.Zero);

        public int AddMissShader(string exportName, BindingSet bindings = null) =>
            NativeMethods.UnityRhiShaderTableAddMissShader(Handle, exportName, bindings?.Handle ?? IntPtr.Zero);

        public int AddHitGroup(string exportName, BindingSet bindings = null) =>
            NativeMethods.UnityRhiShaderTableAddHitGroup(Handle, exportName, bindings?.Handle ?? IntPtr.Zero);

        public int AddCallableShader(string exportName, BindingSet bindings = null) =>
            NativeMethods.UnityRhiShaderTableAddCallableShader(Handle, exportName, bindings?.Handle ?? IntPtr.Zero);

        public void ClearMissShaders() => NativeMethods.UnityRhiShaderTableClearMissShaders(Handle);
        public void ClearHitShaders() => NativeMethods.UnityRhiShaderTableClearHitShaders(Handle);
        public void ClearCallableShaders() => NativeMethods.UnityRhiShaderTableClearCallableShaders(Handle);
    }
}
