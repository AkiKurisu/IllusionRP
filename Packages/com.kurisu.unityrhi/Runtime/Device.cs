using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityRhi.Interop;

namespace UnityRhi
{
    /// <summary>
    /// Mirrors the resource-creation subset of nvrhi::IDevice. The native
    /// device wraps Unity's ID3D12Device and is created when the plugin
    /// initializes on D3D12; this class is a thin façade over it.
    /// </summary>
    public sealed class Device
    {
        private static Device s_instance;

        /// <summary>The singleton device. Throws when D3D12 is not active.</summary>
        public static Device Instance
        {
            get
            {
                if (s_instance == null)
                {
                    if (!RhiCore.IsD3D12Active)
                        throw new InvalidOperationException(
                            "UnityRHI: native device is not initialized. The editor/player must run Direct3D12.");
                    s_instance = new Device();
                }
                return s_instance;
            }
        }

        private Device() { }

        public ulong GetNativeObject(NativeObjectType objectType) =>
            NativeMethods.UnityRhiGetDeviceNativeObject(objectType);

        public Heap CreateHeap(HeapDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (desc.Capacity == 0) throw new ArgumentException("Heap capacity must be non-zero.", nameof(desc));
            var native = RhiHeapDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateHeap(ref native, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateHeap '{desc.DebugName}' failed (see console).");
            return new Heap(handle, desc);
        }

        public Buffer CreateBuffer(BufferDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            var native = RhiBufferDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateBuffer(ref native, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateBuffer '{desc.DebugName}' failed (see console).");
            return new Buffer(handle, desc);
        }

        public Buffer CreateBufferFromNativeResource(IntPtr nativeResource, BufferDesc desc)
        {
            if (nativeResource == IntPtr.Zero)
                throw new ArgumentException("nativeResource must be non-zero.", nameof(nativeResource));
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            var native = RhiBufferDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateBufferFromNativeResource(nativeResource, ref native, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateBufferFromNativeResource '{desc.DebugName}' failed.");
            return new Buffer(handle, desc);
        }

        public MemoryRequirements GetBufferMemoryRequirements(Buffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            NativeMethods.UnityRhiGetBufferMemoryRequirements(buffer.Handle, out var requirements);
            return requirements;
        }

        public bool BindBufferMemory(Buffer buffer, Heap heap, ulong offset)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (heap == null) throw new ArgumentNullException(nameof(heap));
            return NativeMethods.UnityRhiBindBufferMemory(buffer.Handle, heap.Handle, offset) != 0;
        }

        public Texture CreateTexture(TextureDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            var native = RhiTextureDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateTexture(ref native, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateTexture '{desc.DebugName}' failed (see console).");
            return new Texture(handle, desc);
        }

        public MemoryRequirements GetTextureMemoryRequirements(Texture texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            NativeMethods.UnityRhiGetTextureMemoryRequirements(texture.Handle, out var requirements);
            return requirements;
        }

        public bool BindTextureMemory(Texture texture, Heap heap, ulong offset)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (heap == null) throw new ArgumentNullException(nameof(heap));
            return NativeMethods.UnityRhiBindTextureMemory(texture.Handle, heap.Handle, offset) != 0;
        }

        public SubresourceTiling[] GetTextureTiling(Texture texture, out uint numTiles,
            out PackedMipDesc packedMipDesc, out TileShape tileShape)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (!texture.Desc.IsTiled)
                throw new ArgumentException("Texture was not created as tiled.", nameof(texture));
            uint count = checked(texture.Desc.MipLevels * texture.Desc.ArraySize);
            var tilings = new SubresourceTiling[checked((int)count)];
            if (NativeMethods.UnityRhiGetTextureTiling(texture.Handle, out numTiles,
                out packedMipDesc, out tileShape, ref count, tilings) == 0)
                throw new InvalidOperationException("UnityRHI: GetTextureTiling failed.");
            if (count != tilings.Length)
                Array.Resize(ref tilings, checked((int)count));
            return tilings;
        }

        public void UpdateTextureTileMappings(Texture texture, params TextureTilesMapping[] mappings)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (!texture.Desc.IsTiled)
                throw new ArgumentException("Texture was not created as tiled.", nameof(texture));
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));
            foreach (TextureTilesMapping mapping in mappings)
            {
                if (mapping == null) throw new ArgumentException("Mapping cannot be null.", nameof(mappings));
                int count = mapping.Coordinates?.Length ?? 0;
                if (count == 0 || mapping.Regions == null || mapping.Regions.Length != count)
                    throw new ArgumentException("Coordinates and Regions must have the same non-zero length.", nameof(mappings));
                if (mapping.Heap != null && (mapping.ByteOffsets == null || mapping.ByteOffsets.Length != count))
                    throw new ArgumentException("Heap mappings require one byte offset per region.", nameof(mappings));
                ulong[] offsets = mapping.ByteOffsets ?? Array.Empty<ulong>();
                if (NativeMethods.UnityRhiUpdateTextureTileMappings(texture.Handle,
                    mapping.Heap?.Handle ?? IntPtr.Zero, mapping.Coordinates, mapping.Regions,
                    offsets, checked((uint)count)) == 0)
                    throw new InvalidOperationException("UnityRHI: UpdateTextureTileMappings failed.");
            }
        }

        public Texture CreateTextureFromNativeResource(IntPtr nativeResource, TextureDesc desc)
        {
            if (nativeResource == IntPtr.Zero)
                throw new ArgumentException("nativeResource must be non-zero.", nameof(nativeResource));
            var native = RhiTextureDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateTextureFromNativeResource(nativeResource, ref native, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateTextureFromNativeResource '{desc.DebugName}' failed (see console).");
            return new Texture(handle, desc);
        }

        public StagingTexture CreateStagingTexture(TextureDesc desc, CpuAccessMode cpuAccess)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (cpuAccess == CpuAccessMode.None)
                throw new ArgumentException("Staging textures require Read or Write CPU access.", nameof(cpuAccess));
            if (desc.SampleCount != 1)
                throw new NotSupportedException("Multisampled staging textures are not supported by D3D12 footprints.");
            var native = RhiTextureDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateStagingTexture(ref native, cpuAccess, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateStagingTexture '{desc.DebugName}' failed.");
            return new StagingTexture(handle, desc, cpuAccess);
        }

        public IntPtr MapStagingTexture(StagingTexture texture, TextureSlice slice,
            CpuAccessMode cpuAccess, out ulong rowPitch)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (cpuAccess != texture.CpuAccess)
                throw new ArgumentException("Map access must match the staging texture's creation access.", nameof(cpuAccess));
            slice = slice.Resolve(texture.Desc);
            if (slice.X != 0 || slice.Y != 0 || slice.Z != 0)
                throw new ArgumentException("Staging texture mapping only supports a whole subresource.", nameof(slice));
            IntPtr result = NativeMethods.UnityRhiMapStagingTexture(texture.Handle, ref slice, cpuAccess, out rowPitch);
            if (result == IntPtr.Zero)
                throw new InvalidOperationException("UnityRHI: MapStagingTexture failed.");
            return result;
        }

        public void UnmapStagingTexture(StagingTexture texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            NativeMethods.UnityRhiUnmapStagingTexture(texture.Handle);
        }

        public Sampler CreateSampler(SamplerDesc desc)
        {
            var native = RhiSamplerDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateSampler(ref native, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateSampler '{desc.DebugName}' failed (see console).");
            return new Sampler(handle, desc);
        }

        /// <summary>
        /// Mirrors nvrhi::IDevice::createShader: wraps compiled DXIL bytecode
        /// (from <see cref="ShaderCompiler"/>) in a Shader handle.
        /// </summary>
        public Shader CreateShader(ShaderDesc desc, byte[] bytecode)
        {
            if (bytecode == null || bytecode.Length == 0)
                throw new ArgumentException("bytecode must be non-empty", nameof(bytecode));
            IntPtr handle = NativeMethods.UnityRhiCreateShader(
                (uint)desc.ShaderType, desc.EntryName, bytecode, (ulong)bytecode.Length, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateShader '{desc.DebugName}' failed (see console).");
            return new Shader(handle, desc);
        }

        public ShaderLibrary CreateShaderLibrary(byte[] bytecode, string debugName = "")
        {
            if (bytecode == null || bytecode.Length == 0)
                throw new ArgumentException("bytecode must be non-empty", nameof(bytecode));
            IntPtr handle = NativeMethods.UnityRhiCreateShaderLibrary(bytecode, (ulong)bytecode.Length, debugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateShaderLibrary '{debugName}' failed (see console).");
            return new ShaderLibrary(handle, debugName);
        }

        public BindingLayout CreateBindingLayout(BindingLayoutDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            BindingLayoutItem[] bindings = desc.Bindings ?? Array.Empty<BindingLayoutItem>();
            var nativeDesc = RhiBindingLayoutDescNative.FromManaged(desc);
            var items = new RhiBindingLayoutItemNative[bindings.Length];
            for (int i = 0; i < items.Length; ++i)
                items[i] = RhiBindingLayoutItemNative.FromManaged(bindings[i]);

            IntPtr handle = NativeMethods.UnityRhiCreateBindingLayout(ref nativeDesc, items, (uint)items.Length, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateBindingLayout '{desc.DebugName}' failed (see console).");
            return new BindingLayout(handle, desc);
        }

        public BindingLayout CreateBindlessLayout(BindlessLayoutDesc desc)
        {
            BindingLayoutItem[] registerSpaces = desc.RegisterSpaces ?? Array.Empty<BindingLayoutItem>();
            if (desc.MaxCapacity == 0)
                throw new ArgumentException("BindlessLayoutDesc.MaxCapacity must be non-zero.", nameof(desc));
            if (desc.Type == BindlessLayoutDesc.LayoutType.Immutable)
            {
                if (registerSpaces.Length == 0)
                    throw new ArgumentException("Immutable bindless layouts require at least one register space.", nameof(desc));
            }
            else if (registerSpaces.Length != 0)
            {
                throw new ArgumentException("Mutable bindless layouts must not define register spaces.", nameof(desc));
            }

            var nativeDesc = RhiBindlessLayoutDescNative.FromManaged(desc);
            var items = new RhiBindingLayoutItemNative[registerSpaces.Length];
            for (int i = 0; i < items.Length; ++i)
                items[i] = RhiBindingLayoutItemNative.FromManaged(registerSpaces[i]);

            IntPtr handle = NativeMethods.UnityRhiCreateBindlessLayout(ref nativeDesc, items, (uint)items.Length, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateBindlessLayout '{desc.DebugName}' failed (see console).");
            return new BindingLayout(handle, new BindingLayoutDesc
            {
                DebugName = desc.DebugName,
                Visibility = desc.Visibility,
                Bindings = registerSpaces,
            });
        }

        public BindingSet CreateBindingSet(BindingSetDesc desc, BindingLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (desc.Bindings == null || desc.Bindings.Length == 0)
                throw new ArgumentException("BindingSetDesc.Bindings must be non-empty.", nameof(desc));
            var items = new RhiBindingSetItemNative[desc.Bindings.Length];
            for (int i = 0; i < items.Length; ++i)
                items[i] = RhiBindingSetItemNative.FromManaged(desc.Bindings[i]);

            IntPtr handle = NativeMethods.UnityRhiCreateBindingSet(layout.Handle, items, (uint)items.Length, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateBindingSet '{desc.DebugName}' failed (see console).");
            return new BindingSet(handle, desc, layout);
        }

        public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (desc.CS == null) throw new ArgumentNullException(nameof(desc.CS));
            BindingLayout[] bindingLayouts = desc.BindingLayouts ?? Array.Empty<BindingLayout>();
            var layouts = new IntPtr[bindingLayouts.Length];
            for (int i = 0; i < layouts.Length; ++i)
            {
                if (bindingLayouts[i] == null)
                    throw new ArgumentException("ComputePipelineDesc.BindingLayouts contains null.", nameof(desc));
                layouts[i] = bindingLayouts[i].Handle;
            }

            IntPtr handle = NativeMethods.UnityRhiCreateComputePipeline(desc.CS.Handle, layouts, (uint)layouts.Length, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateComputePipeline '{desc.DebugName}' failed (see console).");
            return new ComputePipeline(handle, desc);
        }

        public InputLayout CreateInputLayout(VertexAttributeDesc[] attributes, string debugName = "")
        {
            if (attributes == null || attributes.Length == 0)
                throw new ArgumentException("attributes must be non-empty.", nameof(attributes));

            var utf8 = new System.Collections.Generic.List<IntPtr>();
            try
            {
                var native = new RhiVertexAttributeDescNative[attributes.Length];
                for (int i = 0; i < native.Length; ++i)
                    native[i] = RhiVertexAttributeDescNative.FromManaged(
                        attributes[i], AllocUtf8(attributes[i].Name, utf8));

                IntPtr handle = NativeMethods.UnityRhiCreateInputLayout(native, (uint)native.Length, debugName);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException($"UnityRHI: CreateInputLayout '{debugName}' failed (see console).");
                return new InputLayout(handle, attributes, debugName);
            }
            finally
            {
                foreach (IntPtr ptr in utf8)
                    Marshal.FreeHGlobal(ptr);
            }
        }

        public Framebuffer CreateFramebuffer(FramebufferDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            Texture[] colors = desc.ColorAttachments ?? Array.Empty<Texture>();
            if (colors.Length == 0 && desc.DepthAttachment == null)
                throw new ArgumentException("FramebufferDesc requires at least one attachment.", nameof(desc));

            var colorHandles = new IntPtr[colors.Length];
            for (int i = 0; i < colors.Length; ++i)
            {
                if (colors[i] == null)
                    throw new ArgumentException("FramebufferDesc.ColorAttachments contains null.", nameof(desc));
                colorHandles[i] = colors[i].Handle;
            }

            IntPtr handle = NativeMethods.UnityRhiCreateFramebuffer(
                colorHandles, (uint)colorHandles.Length, desc.DepthAttachment?.Handle ?? IntPtr.Zero, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateFramebuffer '{desc.DebugName}' failed (see console).");
            return new Framebuffer(handle, desc);
        }

        public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc, Framebuffer framebuffer)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (framebuffer == null) throw new ArgumentNullException(nameof(framebuffer));
            if (desc.VS == null) throw new ArgumentNullException(nameof(desc.VS));

            BindingLayout[] bindingLayouts = desc.BindingLayouts ?? Array.Empty<BindingLayout>();
            var layouts = new IntPtr[bindingLayouts.Length];
            for (int i = 0; i < layouts.Length; ++i)
            {
                if (bindingLayouts[i] == null)
                    throw new ArgumentException("GraphicsPipelineDesc.BindingLayouts contains null.", nameof(desc));
                layouts[i] = bindingLayouts[i].Handle;
            }

            var nativeDesc = RhiGraphicsPipelineDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateGraphicsPipeline(
                ref nativeDesc,
                desc.VS.Handle,
                desc.PS?.Handle ?? IntPtr.Zero,
                desc.InputLayout?.Handle ?? IntPtr.Zero,
                layouts, (uint)layouts.Length,
                framebuffer.Handle,
                desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateGraphicsPipeline '{desc.DebugName}' failed (see console).");
            return new GraphicsPipeline(handle, desc, framebuffer);
        }

        public DescriptorTable CreateDescriptorTable(BindingLayout layout, string debugName = "")
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            IntPtr handle = NativeMethods.UnityRhiCreateDescriptorTable(layout.Handle, debugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateDescriptorTable '{debugName}' failed (see console).");
            return new DescriptorTable(handle, layout, debugName);
        }

        public EventQuery CreateEventQuery()
        {
            IntPtr handle = NativeMethods.UnityRhiCreateEventQuery();
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("UnityRHI: CreateEventQuery failed.");
            return new EventQuery(handle);
        }

        public void SetEventQuery(EventQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            NativeMethods.UnityRhiSetEventQuery(query.Handle);
        }

        public bool PollEventQuery(EventQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return NativeMethods.UnityRhiPollEventQuery(query.Handle) != 0;
        }

        public void WaitEventQuery(EventQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            NativeMethods.UnityRhiWaitEventQuery(query.Handle);
        }

        public void ResetEventQuery(EventQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            NativeMethods.UnityRhiResetEventQuery(query.Handle);
        }

        public TimerQuery CreateTimerQuery()
        {
            IntPtr handle = NativeMethods.UnityRhiCreateTimerQuery();
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("UnityRHI: CreateTimerQuery failed.");
            return new TimerQuery(handle);
        }

        public bool PollTimerQuery(TimerQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return NativeMethods.UnityRhiPollTimerQuery(query.Handle) != 0;
        }

        public float GetTimerQueryTime(TimerQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return NativeMethods.UnityRhiGetTimerQueryTime(query.Handle);
        }

        public void ResetTimerQuery(TimerQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            NativeMethods.UnityRhiResetTimerQuery(query.Handle);
        }

        public AccelStruct CreateAccelStruct(Rt.AccelStructDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            var nativeDesc = RhiRtAccelStructDescNative.FromManaged(desc);
            Rt.GeometryDesc[] geometries = desc.BottomLevelGeometries ?? Array.Empty<Rt.GeometryDesc>();
            var nativeGeometries = new RhiRtGeometryDescNative[geometries.Length];
            for (int i = 0; i < nativeGeometries.Length; ++i)
                nativeGeometries[i] = RhiRtGeometryDescNative.FromManaged(geometries[i]);

            IntPtr handle = NativeMethods.UnityRhiCreateAccelStruct(
                ref nativeDesc, nativeGeometries, (uint)nativeGeometries.Length, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateAccelStruct '{desc.DebugName}' failed (see console).");
            return new AccelStruct(handle, desc);
        }

        public MemoryRequirements GetAccelStructMemoryRequirements(AccelStruct accelStruct)
        {
            if (accelStruct == null) throw new ArgumentNullException(nameof(accelStruct));
            NativeMethods.UnityRhiGetAccelStructMemoryRequirements(accelStruct.Handle, out var requirements);
            return requirements;
        }

        public bool BindAccelStructMemory(AccelStruct accelStruct, Heap heap, ulong offset)
        {
            if (accelStruct == null) throw new ArgumentNullException(nameof(accelStruct));
            if (heap == null) throw new ArgumentNullException(nameof(heap));
            return NativeMethods.UnityRhiBindAccelStructMemory(accelStruct.Handle, heap.Handle, offset) != 0;
        }

        public RayTracingPipeline CreateRayTracingPipeline(Rt.PipelineDesc desc)
        {
            var nativeDesc = RhiRtPipelineDescNative.FromManaged(desc);
            var shaders = new RhiRtPipelineShaderDescNative[desc.Shaders?.Length ?? 0];
            var retained = new System.Collections.Generic.List<Resource>();
            var utf8 = new System.Collections.Generic.List<IntPtr>();
            try
            {
                for (int i = 0; i < shaders.Length; ++i)
                {
                    IntPtr exportName = AllocUtf8(desc.Shaders[i].ExportName, utf8);
                    shaders[i] = RhiRtPipelineShaderDescNative.FromManaged(desc.Shaders[i], exportName);
                    if (desc.Shaders[i].Shader != null) retained.Add(desc.Shaders[i].Shader);
                    if (desc.Shaders[i].BindingLayout != null) retained.Add(desc.Shaders[i].BindingLayout);
                }

                var hitGroups = new RhiRtPipelineHitGroupDescNative[desc.HitGroups?.Length ?? 0];
                for (int i = 0; i < hitGroups.Length; ++i)
                {
                    IntPtr exportName = AllocUtf8(desc.HitGroups[i].ExportName, utf8);
                    hitGroups[i] = RhiRtPipelineHitGroupDescNative.FromManaged(desc.HitGroups[i], exportName);
                    if (desc.HitGroups[i].ClosestHitShader != null) retained.Add(desc.HitGroups[i].ClosestHitShader);
                    if (desc.HitGroups[i].AnyHitShader != null) retained.Add(desc.HitGroups[i].AnyHitShader);
                    if (desc.HitGroups[i].IntersectionShader != null) retained.Add(desc.HitGroups[i].IntersectionShader);
                    if (desc.HitGroups[i].BindingLayout != null) retained.Add(desc.HitGroups[i].BindingLayout);
                }

                BindingLayout[] globalLayouts = desc.GlobalBindingLayouts ?? Array.Empty<BindingLayout>();
                var layouts = new IntPtr[globalLayouts.Length];
                for (int i = 0; i < layouts.Length; ++i)
                {
                    layouts[i] = globalLayouts[i].Handle;
                    retained.Add(globalLayouts[i]);
                }

                IntPtr handle = NativeMethods.UnityRhiCreateRayTracingPipeline(
                    ref nativeDesc, shaders, (uint)shaders.Length, hitGroups, (uint)hitGroups.Length,
                    layouts, (uint)layouts.Length, desc.DebugName);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException($"UnityRHI: CreateRayTracingPipeline '{desc.DebugName}' failed (see console).");
                return new RayTracingPipeline(handle, desc, retained.ToArray());
            }
            finally
            {
                foreach (IntPtr ptr in utf8)
                    Marshal.FreeHGlobal(ptr);
            }
        }

        public ShaderTable CreateShaderTable(Rt.ShaderTableDesc desc, RayTracingPipeline pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            var nativeDesc = RhiRtShaderTableDescNative.FromManaged(desc);
            IntPtr handle = NativeMethods.UnityRhiCreateShaderTable(pipeline.Handle, ref nativeDesc, desc.DebugName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"UnityRHI: CreateShaderTable '{desc.DebugName}' failed (see console).");
            return new ShaderTable(handle, desc, pipeline);
        }

        /// <summary>Mirrors nvrhi::IDevice::runGarbageCollection.</summary>
        public void RunGarbageCollection() => NativeMethods.UnityRhiGarbageCollect();

        /// <summary>
        /// Logs every live resource and pending release to the console
        /// (diagnostics only; destroys nothing). Useful before domain reloads
        /// and when hunting leaked handles.
        /// </summary>
        public void ReportLiveResources() => NativeMethods.UnityRhiReportLiveResources();

        /// <summary>Live-object counters (leak checks, debug UI).</summary>
        public DeviceStats GetStats()
        {
            NativeMethods.UnityRhiGetDeviceStats(out DeviceStats stats);
            return stats;
        }

        /// <summary>
        /// Returns a point-in-time copy of all live and GPU-pending native resources.
        /// The copy owns no native references and is safe to retain for editor display.
        /// </summary>
        public ResourceInfo[] GetResourceSnapshot()
        {
            uint count = NativeMethods.UnityRhiGetResourceSnapshot(null, 0);
            if (count == 0)
                return Array.Empty<ResourceInfo>();

            for (int attempt = 0; attempt < 3; ++attempt)
            {
                var native = new ResourceInfoNative[count];
                uint available = NativeMethods.UnityRhiGetResourceSnapshot(native, count);
                if (available > count)
                {
                    count = available;
                    continue;
                }

                int resultCount = (int)Math.Min(available, count);
                var result = new ResourceInfo[resultCount];
                for (int i = 0; i < resultCount; ++i)
                    result[i] = native[i].ToManaged();
                return result;
            }

            return Array.Empty<ResourceInfo>();
        }

        public bool QueryFeatureSupport(Feature feature) =>
            NativeMethods.UnityRhiQueryFeatureSupport((uint)feature) != 0;

        public FormatSupport QueryFormatSupport(Format format) =>
            (FormatSupport)NativeMethods.UnityRhiQueryFormatSupport((uint)format);

        private static IntPtr AllocUtf8(string value, System.Collections.Generic.List<IntPtr> allocations)
        {
            if (string.IsNullOrEmpty(value))
                return IntPtr.Zero;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            allocations.Add(ptr);
            return ptr;
        }
    }
}
