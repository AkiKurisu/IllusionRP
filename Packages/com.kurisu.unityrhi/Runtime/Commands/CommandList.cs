using System;
using UnityEngine.Rendering;

namespace UnityRhi
{
    /// <summary>
    /// Wire-format opcodes; mirrors unityrhi::CommandOpcode
    /// (RenderingPlugin/Source/CommandStream.h). Public so tests and tooling
    /// can index <see cref="CommandStreamInfo.OpcodeCounts"/>.
    /// </summary>
    public enum CommandOpcode : uint
    {
        CopyBuffer = 1,
        SetComputeState = 2,
        Dispatch = 3,
        BeginMarker = 4,
        EndMarker = 5,
        SetEnableAutomaticBarriers = 6,
        SetBufferState = 7,
        DispatchIndirect = 8,
        UavBarrier = 9,
        ClearBufferUInt = 10,
        WriteBuffer = 11,
        SetPushConstants = 12,
        SetTextureState = 13,
        BuildBottomLevelAccelStruct = 14,
        BuildTopLevelAccelStruct = 15,
        BuildTopLevelAccelStructFromBuffer = 16,
        SetRayTracingState = 17,
        DispatchRays = 18,

        // Raster path. C# writer methods land together with the
        // managed wrappers for framebuffers/graphics pipelines.
        SetGraphicsState = 19,
        Draw = 20,
        DrawIndexed = 21,
        ClearTextureFloat = 22,
        ClearDepthStencilTexture = 23,

        // 24..25 remain reserved for the raster path.
        WriteTexture = 26,
        CopyTextureToBuffer = 27,
        BeginTrackingTextureState = 28,
        BeginTrackingBufferState = 29,
        ClearState = 30,
        CommitBarriers = 31,
        SetPermanentTextureState = 32,
        SetPermanentBufferState = 33,
        SetEnableUavBarriersForTexture = 34,
        SetEnableUavBarriersForBuffer = 35,
        SetTextureSubresourceState = 36,
        CopyTexture = 37,
        ResolveTexture = 38,
        DrawIndirect = 39,
        DrawIndexedIndirect = 40,
        DrawIndexedIndirectCount = 41,
        ClearTextureFloatSubresources = 42,
        ClearDepthStencilTextureSubresources = 43,
        ClearTextureUInt = 44,
        CopyTextureFromStaging = 45,
        CopyTextureToStaging = 46,
        BeginTimerQuery = 47,
        EndTimerQuery = 48,
        DispatchDlrr = 49,
        DispatchDlssNr = 50,
    }

    /// <summary>Result of <see cref="CommandList.Validate"/> (native dry-run decode).</summary>
    public struct CommandStreamInfo
    {
        public bool IsValid;
        public uint CommandCount;
        public uint ByteSize;      // from the stream header
        public int StreamLength;   // actual recorded bytes
        public uint[] OpcodeCounts;
        public uint[] OpcodeBytes;
        public CommandTraceEvent[] Events;

        public uint Count(CommandOpcode opcode) => OpcodeCounts[(int)opcode];
        public uint Bytes(CommandOpcode opcode) => OpcodeBytes[(int)opcode];
    }

    public struct CommandTraceEvent
    {
        public CommandOpcode Opcode;
        public uint ByteOffset;
        public uint ByteSize;
        public uint Depth;
        /// <summary>Payload bytes owned by the upload ticket, if this command has one.</summary>
        public ulong UploadByteSize;
        public ulong[] Arguments;
        /// <summary>
        /// Ordered resource handles encoded by the command, including binding
        /// sets and vertex buffers that do not fit in the compact argument list.
        /// Tooling can use this without knowing the command wire layout.
        /// </summary>
        public ulong[] Resources;
        public string Label;
    }

    public struct CommandRecordingStats
    {
        public uint CommandCount;
        public int ByteSize;
        public ulong UploadBytes;
        public int Capacity;
        public int ReallocationCount;
        public int ReservedSpanCount;
    }

    public sealed class CommandList : IDisposable
    {
        private const uint Magic = 0x31494852; // RHI1
        private const uint Version = 14;
        private const uint StateFlagReuseBindings = 1u;

        private readonly CommandWriter _writer = new CommandWriter();
        private readonly System.Collections.Generic.List<IntPtr> _uploadTickets;
        // Keep both the original native handle and its managed wrapper. The native
        // handle is submitted; the wrapper lets submission diagnostics identify a
        // resource that was disposed after recording but before Submit.
        private readonly System.Collections.Generic.Dictionary<IntPtr, Resource> _retainedResources =
            new System.Collections.Generic.Dictionary<IntPtr, Resource>();
        private CommandStreamAllocation _closedStream;
        private int _commandCount;
        private bool _open;
        private ComputeState _currentComputeState;
        private bool _currentComputeStateValid;
        private IntPtr[] _currentComputeBindingHandles = Array.Empty<IntPtr>();
        private int _currentComputeBindingCount;
        private GraphicsState _currentGraphicsState;
        private bool _currentGraphicsStateValid;
        private Rt.State _currentRayTracingState;
        private bool _currentRayTracingStateValid;
        private CommandRecordingStats _recordingStats;
        private ulong _recordedUploadBytes;
        private bool _disposed;

        public CommandList(int uploadTicketCapacity = 0)
        {
            _uploadTickets = uploadTicketCapacity > 0
                ? new System.Collections.Generic.List<IntPtr>(uploadTicketCapacity)
                : new System.Collections.Generic.List<IntPtr>();
        }

        public CommandRecordingStats RecordingStats => _recordingStats;
        public static CommandStreamAllocatorStats AllocatorStats => CommandStreamBufferPool.GetStats();
        public static CommandFrameStats FrameStats => CommandFrameStatistics.GetStats();
        public static CommandListFrameStats[] FrameCommandLists => CommandFrameStatistics.GetCommandLists();
        public static bool DetailedFrameStatsEnabled
        {
            get => CommandFrameStatistics.DetailedEnabled;
            set => CommandFrameStatistics.DetailedEnabled = value;
        }
        public static bool FrameCaptureRequested => CommandFrameStatistics.CaptureRequested;
        public static void RequestFrameCapture() =>
            CommandFrameStatistics.RequestCapture(UnityEngine.Time.frameCount + 1);
        public static void CancelFrameCapture() => CommandFrameStatistics.CancelCapture();
        public static CommandFrameCapture GetFrameCapture() =>
            CommandFrameStatistics.GetCapture(UnityEngine.Time.frameCount);

        public void Open()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CommandList));
            _closedStream?.Release();
            _closedStream = null;
            ReleaseUnsubmittedUploadTickets();
            _retainedResources.Clear();
            _writer.Reset();
            _commandCount = 0;
            _recordedUploadBytes = 0;
            _open = true;
            _currentComputeState = default;
            _currentComputeStateValid = false;
            _currentComputeBindingCount = 0;
            _currentGraphicsState = default;
            _currentGraphicsStateValid = false;
            _currentRayTracingState = default;
            _currentRayTracingStateValid = false;
            WriteHeaderPlaceholder();
        }

        public void Close()
        {
            RequireOpen();
            _open = false;
            var header = new CommandWire.StreamHeader
            {
                Magic = Magic,
                Version = Version,
                ByteSize = checked((uint)_writer.Length),
                CommandCount = checked((uint)_commandCount),
            };
            _writer.Patch(0, header);
            _recordingStats = new CommandRecordingStats
            {
                CommandCount = checked((uint)_commandCount),
                ByteSize = _writer.Length,
                UploadBytes = _recordedUploadBytes,
                Capacity = _writer.Capacity,
                ReallocationCount = _writer.RecordingReallocations,
                ReservedSpanCount = _writer.ReservedSpanCount,
            };
            CommandStreamBufferPool.RecordStreamBytes(_writer.Length);
        }

        /// <summary>Mirrors nvrhi::ICommandList::clearState.</summary>
        public void ClearState()
        {
            RequireOpen();
            WriteOpcode(CommandOpcode.ClearState);
            _currentComputeStateValid = false;
            _currentGraphicsStateValid = false;
            _currentRayTracingStateValid = false;
            _commandCount++;
        }

        public void CopyBuffer(Buffer dest, ulong destOffsetBytes, Buffer src, ulong srcOffsetBytes, ulong byteSize)
        {
            RequireOpen();
            Retain(dest);
            Retain(src);
            WriteCommand(CommandOpcode.CopyBuffer, new CommandWire.CopyBufferPayload
            {
                Dest = ToUInt64(dest.Handle), DestOffset = destOffsetBytes,
                Src = ToUInt64(src.Handle), SrcOffset = srcOffsetBytes, ByteSize = byteSize,
            });
            _commandCount++;
        }

        /// <summary>
        /// Copies one texture subresource into a readback buffer using the
        /// D3D12 placed-subresource footprint for that mip. The destination
        /// offset must be aligned to 512 bytes; rows in the destination use the
        /// D3D12-aligned pitch returned by GetCopyableFootprints (256 bytes).
        /// </summary>
        public void CopyTextureToBuffer(Buffer dest, ulong destOffsetBytes, Texture src,
            uint arraySlice, uint mipLevel)
        {
            RequireOpen();
            if (dest.Desc.CpuAccess != CpuAccessMode.Read)
                throw new ArgumentException("Destination must be a readback buffer.", nameof(dest));
            if ((destOffsetBytes & 511ul) != 0)
                throw new ArgumentException("Destination offset must be 512-byte aligned.", nameof(destOffsetBytes));
            if (arraySlice >= src.Desc.ArraySize || mipLevel >= src.Desc.MipLevels)
                throw new ArgumentOutOfRangeException(nameof(mipLevel));
            Retain(dest);
            Retain(src);
            WriteCommand(CommandOpcode.CopyTextureToBuffer, new CommandWire.CopyTextureToBufferPayload
            {
                Dest = ToUInt64(dest.Handle), DestOffset = destOffsetBytes, Src = ToUInt64(src.Handle),
                ArraySlice = arraySlice, MipLevel = mipLevel,
            });
            _commandCount++;
        }

        public void ClearBufferUInt(Buffer buffer, uint clearValue)
        {
            RequireOpen();
            Retain(buffer);
            WriteHandleUInt32(CommandOpcode.ClearBufferUInt, buffer.Handle, clearValue);
            _commandCount++;
        }

        public void BeginMarker(string name)
        {
            RequireOpen();
            string markerName = name ?? "";
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(markerName);
            WriteCommand(CommandOpcode.BeginMarker, new CommandWire.MarkerPayload
                { ByteSize = checked((uint)byteCount) });
            Span<byte> utf8 = _writer.AllocateSpan<byte>(byteCount);
            System.Text.Encoding.UTF8.GetBytes(markerName.AsSpan(), utf8);
            _commandCount++;
        }

        public void EndMarker()
        {
            RequireOpen();
            WriteOpcode(CommandOpcode.EndMarker);
            _commandCount++;
        }

        public void SetEnableAutomaticBarriers(bool enable)
        {
            RequireOpen();
            WriteUInt32(CommandOpcode.SetEnableAutomaticBarriers, enable ? 1u : 0u);
            _commandCount++;
        }

        public void SetBufferState(Buffer buffer, ResourceStates state)
        {
            RequireOpen();
            Retain(buffer);
            WriteHandleUInt32(CommandOpcode.SetBufferState, buffer.Handle, (uint)state);
            _commandCount++;
        }

        public void SetTextureState(Texture texture, ResourceStates state)
        {
            RequireOpen();
            Retain(texture);
            WriteHandleUInt32(CommandOpcode.SetTextureState, texture.Handle, (uint)state);
            _commandCount++;
        }

        public void BeginTimerQuery(TimerQuery query)
        {
            RequireOpen();
            if (query == null) throw new ArgumentNullException(nameof(query));
            WriteHandle(CommandOpcode.BeginTimerQuery, query.Handle);
            _commandCount++;
        }

        public void EndTimerQuery(TimerQuery query)
        {
            RequireOpen();
            if (query == null) throw new ArgumentNullException(nameof(query));
            WriteHandle(CommandOpcode.EndTimerQuery, query.Handle);
            _commandCount++;
        }

        internal void DispatchDlrr(in CommandWire.DlrrDispatchPayload command,
            Texture input, Texture output, Texture motionVectors, Texture depth,
            Texture diffuseAlbedo, Texture specularAlbedo, Texture normalRoughness,
            Texture specularMotion)
        {
            RequireOpen();
            Retain(input);
            Retain(output);
            Retain(motionVectors);
            Retain(depth);
            Retain(diffuseAlbedo);
            Retain(specularAlbedo);
            Retain(normalRoughness);
            Retain(specularMotion);
            WriteCommand(CommandOpcode.DispatchDlrr, command);
            _commandCount++;
            InvalidateCachedState();
        }

        internal void DispatchDlssNr(in CommandWire.DlssNrDispatchPayload command,
            Texture color, Texture output, Texture motionVectors, Texture depth)
        {
            RequireOpen();
            Retain(color);
            Retain(output);
            Retain(motionVectors);
            Retain(depth);
            WriteCommand(CommandOpcode.DispatchDlssNr, command);
            _commandCount++;
            InvalidateCachedState();
        }

        public void CopyTexture(Texture dest, TextureSlice destSlice, Texture src, TextureSlice srcSlice)
        {
            RequireOpen();
            Retain(dest);
            Retain(src);
            TextureSlice resolvedDest = destSlice.Resolve(dest.Desc);
            TextureSlice resolvedSrc = srcSlice.Resolve(src.Desc);
            WriteTextureCopy(CommandOpcode.CopyTexture, dest.Handle, resolvedDest, src.Handle, resolvedSrc);
            _commandCount++;
        }

        public void CopyTexture(Texture dest, TextureSlice destSlice,
            StagingTexture src, TextureSlice srcSlice)
        {
            RequireOpen();
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (src.CpuAccess != CpuAccessMode.Write)
                throw new ArgumentException("Source staging texture must have Write CPU access.", nameof(src));
            Retain(dest);
            Retain(src);
            TextureSlice resolvedDest = destSlice.Resolve(dest.Desc);
            TextureSlice resolvedSrc = srcSlice.Resolve(src.Desc);
            WriteTextureCopy(CommandOpcode.CopyTextureFromStaging, dest.Handle, resolvedDest, src.Handle, resolvedSrc);
            _commandCount++;
        }

        public void CopyTexture(StagingTexture dest, TextureSlice destSlice,
            Texture src, TextureSlice srcSlice)
        {
            RequireOpen();
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (dest.CpuAccess != CpuAccessMode.Read)
                throw new ArgumentException("Destination staging texture must have Read CPU access.", nameof(dest));
            Retain(dest);
            Retain(src);
            TextureSlice resolvedDest = destSlice.Resolve(dest.Desc);
            TextureSlice resolvedSrc = srcSlice.Resolve(src.Desc);
            WriteTextureCopy(CommandOpcode.CopyTextureToStaging, dest.Handle, resolvedDest, src.Handle, resolvedSrc);
            _commandCount++;
        }

        public void ResolveTexture(Texture dest, TextureSubresourceSet destSubresources,
            Texture src, TextureSubresourceSet srcSubresources)
        {
            RequireOpen();
            Retain(dest);
            Retain(src);
            TextureSubresourceSet resolvedDest = destSubresources.Resolve(dest.Desc, false);
            TextureSubresourceSet resolvedSrc = srcSubresources.Resolve(src.Desc, false);
            if (resolvedDest.NumMipLevels != resolvedSrc.NumMipLevels ||
                resolvedDest.NumArraySlices != resolvedSrc.NumArraySlices)
                throw new ArgumentException("Source and destination resolve subresource counts must match.");
            WriteCommand(CommandOpcode.ResolveTexture, new CommandWire.ResolveTexturePayload
            {
                Dest = ToUInt64(dest.Handle), DestSubresources = ToWire(resolvedDest),
                Src = ToUInt64(src.Handle), SrcSubresources = ToWire(resolvedSrc),
            });
            _commandCount++;
        }

        public void SetTextureState(Texture texture, TextureSubresourceSet subresources, ResourceStates state)
        {
            RequireOpen();
            Retain(texture);
            WriteCommand(CommandOpcode.SetTextureSubresourceState, new CommandWire.TextureSubresourceStatePayload
            {
                Texture = ToUInt64(texture.Handle), Subresources = ToWire(subresources), State = (uint)state,
            });
            _commandCount++;
        }

        public void SetPermanentTextureState(Texture texture, ResourceStates state)
        {
            RequireOpen();
            Retain(texture);
            WriteHandleUInt32(CommandOpcode.SetPermanentTextureState, texture.Handle, (uint)state);
            _commandCount++;
        }

        public void SetPermanentBufferState(Buffer buffer, ResourceStates state)
        {
            RequireOpen();
            Retain(buffer);
            WriteHandleUInt32(CommandOpcode.SetPermanentBufferState, buffer.Handle, (uint)state);
            _commandCount++;
        }

        public void SetEnableUavBarriersForTexture(Texture texture, bool enableBarriers)
        {
            RequireOpen();
            Retain(texture);
            WriteHandleUInt32(CommandOpcode.SetEnableUavBarriersForTexture, texture.Handle,
                enableBarriers ? 1u : 0u);
            _commandCount++;
        }

        public void SetEnableUavBarriersForBuffer(Buffer buffer, bool enableBarriers)
        {
            RequireOpen();
            Retain(buffer);
            WriteHandleUInt32(CommandOpcode.SetEnableUavBarriersForBuffer, buffer.Handle,
                enableBarriers ? 1u : 0u);
            _commandCount++;
        }

        public void CommitBarriers()
        {
            RequireOpen();
            WriteOpcode(CommandOpcode.CommitBarriers);
            _commandCount++;
        }

        /// <summary>
        /// Informs this command list of the current state of a texture or some
        /// of its subresources. This does not perform a state transition.
        /// </summary>
        public void BeginTrackingTextureState(Texture texture,
            TextureSubresourceSet subresources, ResourceStates state)
        {
            RequireOpen();
            Retain(texture);
            WriteCommand(CommandOpcode.BeginTrackingTextureState, new CommandWire.TextureSubresourceStatePayload
            {
                Texture = ToUInt64(texture.Handle), Subresources = ToWire(subresources), State = (uint)state,
            });
            _commandCount++;
        }

        /// <summary>
        /// Informs this command list of the current state of a buffer. This
        /// does not perform a state transition.
        /// </summary>
        public void BeginTrackingBufferState(Buffer buffer, ResourceStates state)
        {
            RequireOpen();
            Retain(buffer);
            WriteHandleUInt32(CommandOpcode.BeginTrackingBufferState, buffer.Handle, (uint)state);
            _commandCount++;
        }

        public void SetComputeState(ComputeState state)
        {
            RequireOpen();
            if (state.Pipeline == null)
                throw new ArgumentNullException(nameof(state.Pipeline));
            Resource[] bindings = state.Bindings ?? Array.Empty<Resource>();
            bool reuseBindings = _currentComputeStateValid &&
                _currentComputeBindingCount == bindings.Length;
            Retain(state.Pipeline);
            if (state.IndirectParams != null)
                Retain(state.IndirectParams);
            for (int i = 0; i < bindings.Length; ++i)
            {
                if (bindings[i] == null)
                    throw new ArgumentException("ComputeState.Bindings contains null.", nameof(state));
                Retain(bindings[i]);
                if (reuseBindings && _currentComputeBindingHandles[i] != bindings[i].Handle)
                    reuseBindings = false;
            }
            WriteCommand(CommandOpcode.SetComputeState, new CommandWire.StatePayload
            {
                Object = ToUInt64(state.Pipeline.Handle),
                IndirectParams = state.IndirectParams != null ? ToUInt64(state.IndirectParams.Handle) : 0ul,
                BindingCount = checked((uint)bindings.Length),
                Flags = reuseBindings ? StateFlagReuseBindings : 0u,
            });
            if (!reuseBindings)
            {
                Span<ulong> bindingHandles = _writer.AllocateSpan<ulong>(bindings.Length);
                for (int i = 0; i < bindings.Length; ++i)
                    bindingHandles[i] = ToUInt64(bindings[i].Handle);
            }
            if (_currentComputeBindingHandles.Length < bindings.Length)
                Array.Resize(ref _currentComputeBindingHandles, bindings.Length);
            for (int i = 0; i < bindings.Length; ++i)
                _currentComputeBindingHandles[i] = bindings[i].Handle;
            _currentComputeBindingCount = bindings.Length;
            _currentComputeState = state;
            _currentComputeStateValid = true;
            _currentGraphicsStateValid = false;
            _currentRayTracingStateValid = false;
            _commandCount++;
        }

        /// <summary>
        /// Mirrors nvrhi::ICommandList::writeBuffer: data is copied directly
        /// into the native upload heap at record time; replay only records the
        /// GPU copy (or volatile constant-buffer address).
        /// </summary>
        public unsafe void WriteBuffer<T>(Buffer buffer, T[] data, ulong destOffsetBytes = 0) where T : unmanaged
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("data must be non-empty", nameof(data));
            WriteBuffer(buffer, data, 0, data.Length, destOffsetBytes);
        }

        /// <summary>
        /// Uploads an effective range from a reusable managed array. This mirrors NVRHI's
        /// writeBuffer(pointer, dataSize, destinationOffset) contract without requiring
        /// callers to allocate a right-sized temporary array.
        /// </summary>
        public unsafe void WriteBuffer<T>(Buffer buffer, T[] data, int sourceIndex,
            int elementCount, ulong destOffsetBytes = 0) where T : unmanaged
        {
            RequireOpen();
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (sourceIndex < 0 || elementCount <= 0 || sourceIndex > data.Length - elementCount)
                throw new ArgumentOutOfRangeException(nameof(elementCount),
                    "The source range must be non-empty and contained in the array.");
            Retain(buffer);
            int byteCount = checked(elementCount * sizeof(T));
            fixed (T* source = &data[sourceIndex])
                WriteBufferUpload(buffer, source, checked((ulong)byteCount), destOffsetBytes);
            _commandCount++;
        }

        /// <summary>Writes one blittable value without requiring a temporary one-element array.</summary>
        public unsafe void WriteBuffer<T>(Buffer buffer, in T data, ulong destOffsetBytes = 0) where T : unmanaged
        {
            RequireOpen();
            Retain(buffer);
            int byteCount = sizeof(T);
            fixed (T* source = &data)
                WriteBufferUpload(buffer, source, checked((ulong)byteCount), destOffsetBytes);
            _commandCount++;
        }

        /// <summary>
        /// Mirrors nvrhi::ICommandList::writeTexture: texel data for one
        /// subresource is laid out directly in the native upload heap at record
        /// time; replay only records the texture copy. <paramref name="rowPitch"/> is the byte stride between
        /// rows in <paramref name="data"/>; <paramref name="depthPitch"/> is
        /// the byte stride between depth slices (0 for 2D textures).
        /// </summary>
        public unsafe void WriteTexture<T>(Texture texture, uint arraySlice, uint mipLevel,
            T[] data, ulong rowPitch, ulong depthPitch = 0) where T : unmanaged
        {
            RequireOpen();
            if (data == null || data.Length == 0)
                throw new ArgumentException("data must be non-empty", nameof(data));
            Retain(texture);
            WriteTexturePayload(ToUInt64(texture.Handle), arraySlice, mipLevel, data, rowPitch, depthPitch);
        }

        /// <summary>Mirrors nvrhi::ICommandList::setPushConstants.</summary>
        public unsafe void SetPushConstants<T>(T data) where T : unmanaged
        {
            RequireOpen();
            if (!_currentComputeStateValid && !_currentGraphicsStateValid && !_currentRayTracingStateValid)
                throw new InvalidOperationException(
                    "SetPushConstants requires SetComputeState, SetGraphicsState, or SetRayTracingState first.");
            int byteCount = sizeof(T);
            WriteCommand(CommandOpcode.SetPushConstants, new CommandWire.MarkerPayload
                { ByteSize = checked((uint)byteCount) });
            _writer.Write(data);
            _commandCount++;
        }

        public void Dispatch(uint groupsX, uint groupsY = 1, uint groupsZ = 1)
        {
            RequireOpen();
            WriteCommand(CommandOpcode.Dispatch, new CommandWire.DispatchPayload
                { X = groupsX, Y = groupsY, Z = groupsZ });
            _commandCount++;
        }

        /// <summary>
        /// Mirrors nvrhi::ICommandList::dispatchIndirect: reads the argument
        /// buffer from the current ComputeState.IndirectParams.
        /// </summary>
        public void DispatchIndirect(ulong offsetBytes = 0)
        {
            RequireOpen();
            if (!_currentComputeStateValid || _currentComputeState.IndirectParams == null)
                throw new InvalidOperationException(
                    "DispatchIndirect requires SetComputeState with a non-null IndirectParams.");
            WriteCommand(CommandOpcode.DispatchIndirect, new CommandWire.HandlePayload
                { Handle = offsetBytes });
            _commandCount++;
        }

        public void BuildBottomLevelAccelStruct(AccelStruct accelStruct, Rt.GeometryDesc[] geometries, Rt.AccelStructBuildFlags buildFlags)
        {
            RequireOpen();
            Retain(accelStruct);
            geometries ??= Array.Empty<Rt.GeometryDesc>();
            for (int i = 0; i < geometries.Length; ++i)
                RetainGeometryResources(geometries[i]);
            WriteCommand(CommandOpcode.BuildBottomLevelAccelStruct, new CommandWire.AccelStructBuildPayload
            {
                AccelStruct = ToUInt64(accelStruct.Handle), ElementCount = checked((uint)geometries.Length),
                BuildFlags = (uint)buildFlags,
            });
            Span<Interop.RhiRtGeometryDescNative> nativeGeometries =
                _writer.AllocateSpan<Interop.RhiRtGeometryDescNative>(geometries.Length);
            for (int i = 0; i < geometries.Length; ++i)
                nativeGeometries[i] = Interop.RhiRtGeometryDescNative.FromManaged(geometries[i]);
            _commandCount++;
        }

        public void BuildTopLevelAccelStruct(AccelStruct accelStruct, Rt.InstanceDesc[] instances, Rt.AccelStructBuildFlags buildFlags)
        {
            BuildTopLevelAccelStruct(accelStruct, instances, instances?.Length ?? 0, buildFlags);
        }

        /// <summary>Builds a TLAS from the active prefix of a reusable instance array.</summary>
        public void BuildTopLevelAccelStruct(AccelStruct accelStruct, Rt.InstanceDesc[] instances,
            int instanceCount, Rt.AccelStructBuildFlags buildFlags)
        {
            RequireOpen();
            Retain(accelStruct);
            instances ??= Array.Empty<Rt.InstanceDesc>();
            if (instanceCount < 0 || instanceCount > instances.Length)
                throw new ArgumentOutOfRangeException(nameof(instanceCount));
            for (int i = 0; i < instanceCount; ++i)
                if (instances[i].BottomLevelAS != null)
                    Retain(instances[i].BottomLevelAS);
            WriteCommand(CommandOpcode.BuildTopLevelAccelStruct, new CommandWire.AccelStructBuildPayload
            {
                AccelStruct = ToUInt64(accelStruct.Handle), ElementCount = checked((uint)instanceCount),
                BuildFlags = (uint)buildFlags,
            });
            Span<Interop.RhiRtInstanceDescNative> nativeInstances =
                _writer.AllocateSpan<Interop.RhiRtInstanceDescNative>(instanceCount);
            for (int i = 0; i < instanceCount; ++i)
                Interop.RhiRtInstanceDescNative.WriteFromManaged(in instances[i], ref nativeInstances[i]);
            _commandCount++;
        }

        public void BuildTopLevelAccelStructFromBuffer(
            AccelStruct accelStruct, Buffer instanceBuffer, ulong instanceBufferOffset, uint numInstances,
            Rt.AccelStructBuildFlags buildFlags)
        {
            RequireOpen();
            Retain(accelStruct);
            Retain(instanceBuffer);
            WriteCommand(CommandOpcode.BuildTopLevelAccelStructFromBuffer, new CommandWire.TlasFromBufferPayload
            {
                AccelStruct = ToUInt64(accelStruct.Handle), InstanceBuffer = ToUInt64(instanceBuffer.Handle),
                InstanceBufferOffset = instanceBufferOffset, NumInstances = numInstances,
                BuildFlags = (uint)buildFlags,
            });
            _commandCount++;
        }

        public void SetRayTracingState(Rt.State state)
        {
            RequireOpen();
            if (state.ShaderTable == null)
                throw new ArgumentNullException(nameof(state.ShaderTable));
            Resource[] bindings = state.Bindings ?? Array.Empty<Resource>();
            Retain(state.ShaderTable);
            for (int i = 0; i < bindings.Length; ++i)
                Retain(bindings[i]);
            WriteCommand(CommandOpcode.SetRayTracingState, new CommandWire.BindingStatePayload
            {
                Object = ToUInt64(state.ShaderTable.Handle), BindingCount = checked((uint)bindings.Length),
            });
            Span<ulong> bindingHandles = _writer.AllocateSpan<ulong>(bindings.Length);
            for (int i = 0; i < bindings.Length; ++i)
                bindingHandles[i] = ToUInt64(bindings[i].Handle);
            _currentRayTracingState = state;
            _currentRayTracingStateValid = true;
            _currentComputeStateValid = false;
            _currentGraphicsStateValid = false;
            _commandCount++;
        }

        public void DispatchRays(Rt.DispatchRaysArguments args)
        {
            RequireOpen();
            if (!_currentRayTracingStateValid)
                throw new InvalidOperationException("DispatchRays requires SetRayTracingState first.");
            WriteCommand(CommandOpcode.DispatchRays, new CommandWire.DispatchPayload
                { X = args.Width, Y = args.Height, Z = args.Depth });
            _commandCount++;
        }

        public void UavBarrier(Resource resource)
        {
            RequireOpen();
            Retain(resource);
            WriteHandle(CommandOpcode.UavBarrier, resource.Handle);
            _commandCount++;
        }

        public void ClearTextureFloat(Texture texture, UnityEngine.Color color)
        {
            RequireOpen();
            Retain(texture);

            WriteCommand(CommandOpcode.ClearTextureFloat, new CommandWire.ClearTextureFloatPayload
            {
                Texture = ToUInt64(texture.Handle),
                R = color.r, G = color.g, B = color.b, A = color.a,
            });
            _commandCount++;
        }

        public void ClearTextureFloat(Texture texture, TextureSubresourceSet subresources, UnityEngine.Color color)
        {
            RequireOpen();
            Retain(texture);
            TextureSubresourceSet resolved = subresources.Resolve(texture.Desc, false);
            WriteCommand(CommandOpcode.ClearTextureFloatSubresources, new CommandWire.ClearTextureFloatSubresourcesPayload
            {
                Texture = ToUInt64(texture.Handle), Subresources = ToWire(resolved),
                R = color.r, G = color.g, B = color.b, A = color.a,
            });
            _commandCount++;
        }

        public void ClearDepthStencilTexture(
            Texture texture, bool clearDepth, float depth, bool clearStencil, uint stencil)
        {
            RequireOpen();
            Retain(texture);

            WriteCommand(CommandOpcode.ClearDepthStencilTexture, new CommandWire.ClearDepthStencilPayload
            {
                Texture = ToUInt64(texture.Handle),
                ClearDepth = clearDepth ? 1u : 0u, Depth = depth,
                ClearStencil = clearStencil ? 1u : 0u, Stencil = stencil,
            });
            _commandCount++;
        }

        public void ClearDepthStencilTexture(Texture texture, TextureSubresourceSet subresources,
            bool clearDepth, float depth, bool clearStencil, uint stencil)
        {
            RequireOpen();
            Retain(texture);
            TextureSubresourceSet resolved = subresources.Resolve(texture.Desc, false);
            WriteCommand(CommandOpcode.ClearDepthStencilTextureSubresources, new CommandWire.ClearDepthStencilSubresourcesPayload
            {
                Texture = ToUInt64(texture.Handle), Subresources = ToWire(resolved),
                ClearDepth = clearDepth ? 1u : 0u, Depth = depth,
                ClearStencil = clearStencil ? 1u : 0u, Stencil = stencil,
            });
            _commandCount++;
        }

        public void ClearTextureUInt(Texture texture, TextureSubresourceSet subresources, uint clearColor)
        {
            RequireOpen();
            Retain(texture);
            TextureSubresourceSet resolved = subresources.Resolve(texture.Desc, false);
            WriteCommand(CommandOpcode.ClearTextureUInt, new CommandWire.ClearTextureUIntPayload
            {
                Texture = ToUInt64(texture.Handle),
                Subresources = ToWire(resolved), Value = clearColor,
            });
            _commandCount++;
        }

        public void SetGraphicsState(GraphicsState state)
        {
            RequireOpen();
            if (state.Pipeline == null)
                throw new ArgumentNullException(nameof(state.Pipeline));
            if (state.Framebuffer == null)
                throw new ArgumentNullException(nameof(state.Framebuffer));

            VertexBufferBinding[] vertexBuffers = state.VertexBuffers ?? Array.Empty<VertexBufferBinding>();
            Resource[] bindings = state.Bindings ?? Array.Empty<Resource>();

            Retain(state.Pipeline);
            Retain(state.Framebuffer);
            if (state.IndexBuffer != null)
                Retain(state.IndexBuffer);
            if (state.IndirectParams != null)
                Retain(state.IndirectParams);
            if (state.IndirectCountBuffer != null)
                Retain(state.IndirectCountBuffer);
            for (int i = 0; i < vertexBuffers.Length; ++i)
            {
                if (vertexBuffers[i].Buffer == null)
                    throw new ArgumentException("GraphicsState.VertexBuffers contains a null Buffer.", nameof(state));
                Retain(vertexBuffers[i].Buffer);
            }
            for (int i = 0; i < bindings.Length; ++i)
            {
                if (bindings[i] == null)
                    throw new ArgumentException("GraphicsState.Bindings contains null.", nameof(state));
                Retain(bindings[i]);
            }

            WriteCommand(CommandOpcode.SetGraphicsState, new CommandWire.GraphicsStatePayload
            {
                Pipeline = ToUInt64(state.Pipeline.Handle), Framebuffer = ToUInt64(state.Framebuffer.Handle),
                Viewport = new CommandWire.Viewport
                {
                    MinX = state.Viewport.MinX, MaxX = state.Viewport.MaxX,
                    MinY = state.Viewport.MinY, MaxY = state.Viewport.MaxY,
                    MinZ = state.Viewport.MinZ, MaxZ = state.Viewport.MaxZ,
                },
                BlendR = state.BlendConstantColor.r, BlendG = state.BlendConstantColor.g,
                BlendB = state.BlendConstantColor.b, BlendA = state.BlendConstantColor.a,
                IndexBuffer = state.IndexBuffer != null ? ToUInt64(state.IndexBuffer.Handle) : 0ul,
                IndexFormat = (uint)state.IndexBufferFormat, IndexOffset = state.IndexBufferOffset,
                IndirectParams = state.IndirectParams != null ? ToUInt64(state.IndirectParams.Handle) : 0ul,
                IndirectCountBuffer = state.IndirectCountBuffer != null
                    ? ToUInt64(state.IndirectCountBuffer.Handle) : 0ul,
                VertexBufferCount = checked((uint)vertexBuffers.Length),
            });
            Span<CommandWire.VertexBufferBinding> nativeVertexBuffers =
                _writer.AllocateSpan<CommandWire.VertexBufferBinding>(vertexBuffers.Length);
            for (int i = 0; i < vertexBuffers.Length; ++i)
            {
                VertexBufferBinding binding = vertexBuffers[i];
                nativeVertexBuffers[i] = new CommandWire.VertexBufferBinding
                    { Buffer = ToUInt64(binding.Buffer.Handle), Slot = binding.Slot, Offset = binding.Offset };
            }
            uint bindingCount = checked((uint)bindings.Length);
            _writer.Write(bindingCount);
            Span<ulong> bindingHandles = _writer.AllocateSpan<ulong>(bindings.Length);
            for (int i = 0; i < bindings.Length; ++i)
                bindingHandles[i] = ToUInt64(bindings[i].Handle);
            _currentGraphicsState = state;
            _currentGraphicsStateValid = true;
            _currentComputeStateValid = false;
            _currentRayTracingStateValid = false;
            _commandCount++;
        }

        public void Draw(DrawArguments args)
        {
            RequireOpen();
            if (!_currentGraphicsStateValid)
                throw new InvalidOperationException("Draw requires SetGraphicsState first.");
            WriteCommand(CommandOpcode.Draw, new CommandWire.DrawPayload
            {
                A = args.VertexCount, B = args.InstanceCount,
                C = args.StartVertexLocation, D = args.StartInstanceLocation,
            });
            _commandCount++;
        }

        public void DrawIndexed(DrawIndexedArguments args)
        {
            RequireOpen();
            if (!_currentGraphicsStateValid)
                throw new InvalidOperationException("DrawIndexed requires SetGraphicsState first.");
            WriteCommand(CommandOpcode.DrawIndexed, new CommandWire.DrawIndexedPayload
            {
                A = args.IndexCount, B = args.InstanceCount,
                C = args.StartIndexLocation, D = args.BaseVertexLocation, E = args.StartInstanceLocation,
            });
            _commandCount++;
        }

        public void DrawIndirect(ulong offsetBytes, uint drawCount = 1)
        {
            RequireGraphicsIndirectState(false);
            WriteCommand(CommandOpcode.DrawIndirect, new CommandWire.DrawIndirectPayload
                { Offset = offsetBytes, Count = drawCount });
            _commandCount++;
        }

        public void DrawIndexedIndirect(ulong offsetBytes, uint drawCount = 1)
        {
            RequireGraphicsIndirectState(false);
            WriteCommand(CommandOpcode.DrawIndexedIndirect, new CommandWire.DrawIndirectPayload
                { Offset = offsetBytes, Count = drawCount });
            _commandCount++;
        }

        public void DrawIndexedIndirectCount(ulong paramsOffsetBytes, ulong countOffsetBytes, uint maxDrawCount)
        {
            RequireGraphicsIndirectState(true);
            WriteCommand(CommandOpcode.DrawIndexedIndirectCount, new CommandWire.DrawIndirectCountPayload
            {
                ParamsOffset = paramsOffsetBytes,
                MaxDrawCount = maxDrawCount, CountOffset = countOffsetBytes,
            });
            _commandCount++;
        }

        private void RequireGraphicsIndirectState(bool requireCountBuffer)
        {
            RequireOpen();
            if (!_currentGraphicsStateValid || _currentGraphicsState.IndirectParams == null)
                throw new InvalidOperationException("Indirect draw requires SetGraphicsState with IndirectParams.");
            if (requireCountBuffer && _currentGraphicsState.IndirectCountBuffer == null)
                throw new InvalidOperationException("Indirect-count draw requires IndirectCountBuffer.");
        }

        /// <summary>
        /// Copies the encoded stream into a native-owned submission and records
        /// that submission in <paramref name="commandBuffer"/>. Native code owns
        /// the copied bytes, upload tickets, and resource references until the
        /// render-thread event consumes the submission.
        /// </summary>
        public SubmittedCommandList Submit(CommandBuffer commandBuffer)
        {
            return new SubmittedCommandList(SubmitAllocation(commandBuffer));
        }

        /// <summary>Submits without allocating the compatibility receipt.</summary>
        public void SubmitAndForget(CommandBuffer commandBuffer)
        {
            SubmitAllocation(commandBuffer);
        }

        internal int SubmitAllocation(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null) throw new ArgumentNullException(nameof(commandBuffer));
            if (_open)
                throw new InvalidOperationException("CommandList must be closed before Submit.");
            if (_closedStream == null)
            {
                IntPtr pointer = _writer.Detach(out int byteSize, out int capacity);
                if (pointer == IntPtr.Zero || byteSize == 0)
                    throw new InvalidOperationException("Command stream is unavailable.");
                try
                {
                    _closedStream = CommandStreamAllocation.Rent(
                        pointer, byteSize, capacity, _uploadTickets, _retainedResources);
                }
                catch
                {
                    CommandStreamBufferPool.Return(pointer, capacity);
                    throw;
                }
                _uploadTickets.Clear();
                _retainedResources.Clear();
            }
            CommandStreamInfo? decoded = null;
            bool captureEvents = CommandFrameStatistics.CaptureRequested;
            if (captureEvents)
            {
                CommandTraceEvent[] events = CommandStreamTraceDecoder.Decode(_closedStream.Bytes);
                if (RhiCore.NativeApiVersion >= 10)
                    PopulateUploadTicketSizes(events);
                if (events.Length == _recordingStats.CommandCount)
                {
                    var counts = new uint[Interop.CommandStreamInfoNative.MaxOpcode];
                    var bytes = new uint[Interop.CommandStreamInfoNative.MaxOpcode];
                    foreach (CommandTraceEvent trace in events)
                    {
                        int opcode = (int)trace.Opcode;
                        if ((uint)opcode >= counts.Length) continue;
                        counts[opcode]++;
                        bytes[opcode] += trace.ByteSize;
                    }
                    decoded = new CommandStreamInfo
                    {
                        IsValid = true,
                        CommandCount = checked((uint)events.Length),
                        ByteSize = checked((uint)_closedStream.ByteSize),
                        StreamLength = _closedStream.ByteSize,
                        OpcodeCounts = counts,
                        OpcodeBytes = bytes,
                        Events = events,
                    };
                }
            }
            else if (CommandFrameStatistics.DetailedEnabled && RhiCore.NativeApiVersion >= 6)
            {
                CommandStreamInfo info = Validate();
                if (info.IsValid)
                    decoded = info;
            }
            IntPtr submission = _closedStream.CreateNativeSubmission();
            try
            {
                RhiCore.IssueCommandStream(commandBuffer, submission);
            }
            catch
            {
                Interop.NativeMethods.UnityRhiDestroyCommandSubmission(submission);
                throw;
            }
            CommandFrameStatistics.RecordSubmission(UnityEngine.Time.frameCount,
                _closedStream.ByteSize, _recordedUploadBytes, decoded);
            return _closedStream.ByteSize;
        }

        private static void PopulateUploadTicketSizes(CommandTraceEvent[] events)
        {
            for (int i = 0; i < events.Length; ++i)
            {
                CommandTraceEvent trace = events[i];
                int ticketArgument = trace.Opcode == CommandOpcode.WriteBuffer ? 2 :
                    trace.Opcode == CommandOpcode.WriteTexture ? 3 : -1;
                if (ticketArgument < 0 || trace.Arguments == null ||
                    trace.Arguments.Length <= ticketArgument || trace.Arguments[ticketArgument] == 0)
                    continue;

                trace.UploadByteSize = Interop.NativeMethods.UnityRhiGetUploadTicketSize(
                    unchecked((IntPtr)(long)trace.Arguments[ticketArgument]));
                events[i] = trace;
            }
        }

        /// <summary>
        /// Dry-run decodes the recorded stream through the native decoder (no
        /// device or submission involved). Catches drift between this writer
        /// and the native wire-format expectations; also handy for tooling
        /// that wants per-opcode statistics.
        /// </summary>
        public unsafe CommandStreamInfo Validate()
        {
            if (_open)
                throw new InvalidOperationException("CommandList must be closed before Validate.");
            IntPtr pointer = _closedStream?.Pointer ?? _writer.Pointer;
            int byteSize = _closedStream?.ByteSize ?? _writer.Length;
            if (pointer == IntPtr.Zero || byteSize == 0)
                throw new InvalidOperationException("Command stream is unavailable.");
            int ok = Interop.NativeMethods.UnityRhiDecodeCommandStream(
                pointer, out Interop.CommandStreamInfoNative native);

            var info = new CommandStreamInfo
            {
                IsValid = ok != 0 && native.Ok != 0,
                CommandCount = native.CommandCount,
                ByteSize = native.ByteSize,
                StreamLength = byteSize,
                OpcodeCounts = new uint[Interop.CommandStreamInfoNative.MaxOpcode],
                OpcodeBytes = new uint[Interop.CommandStreamInfoNative.MaxOpcode],
            };
            for (int i = 0; i < info.OpcodeCounts.Length; ++i)
            {
                info.OpcodeCounts[i] = native.OpcodeCounts[i];
                info.OpcodeBytes[i] = native.OpcodeBytes[i];
            }
            return info;
        }


        private void WriteHeaderPlaceholder()
        {
            _writer.Write(default(CommandWire.StreamHeader));
        }

        internal ReadOnlySpan<byte> RecordedBytes =>
            _closedStream != null ? _closedStream.Bytes : _writer.Bytes;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            _closedStream?.Release();
            _closedStream = null;
            ReleaseUnsubmittedUploadTickets();
            _writer.Dispose();
            _open = false;
            _disposed = true;
        }

        ~CommandList()
        {
            Dispose(false);
        }

        private void RequireOpen()
        {
            if (!_open)
                throw new InvalidOperationException("CommandList is not open.");
        }

        private void InvalidateCachedState()
        {
            _currentComputeStateValid = false;
            _currentGraphicsStateValid = false;
            _currentRayTracingStateValid = false;
        }

        // Record each unique native handle so the submitted stream can acquire
        // a plugin-side reference before its raw command bytes become visible
        // to Unity's render thread.
        private void Retain(Resource resource)
        {
            if (resource == null)
                throw new ArgumentNullException(nameof(resource));
            if (!resource.IsValid)
                throw new ObjectDisposedException(resource.GetType().Name);
            _retainedResources[resource.Handle] = resource;
        }

        /// <summary>
        /// Keeps a resource alive through command-stream replay even when its handle is
        /// reached indirectly (for example, through a mutable descriptor table).
        /// The caller must retain its managed reference until Submit succeeds.
        /// </summary>
        public void KeepAliveForSubmission(Resource resource)
        {
            RequireOpen();
            Retain(resource);
        }

        private static ulong ToUInt64(IntPtr ptr) => unchecked((ulong)ptr.ToInt64());

        private void WriteOpcode(CommandOpcode opcode)
        {
            _writer.Write((uint)opcode);
        }

        private void WriteCommand<T>(CommandOpcode opcode, in T payload) where T : unmanaged
        {
            WriteOpcode(opcode);
            _writer.Write(payload);
        }

        private void WriteUInt32(CommandOpcode opcode, uint value)
        {
            WriteCommand(opcode, new CommandWire.UInt32Payload { Value = value });
        }

        private void WriteHandle(CommandOpcode opcode, IntPtr handle)
        {
            WriteCommand(opcode, new CommandWire.HandlePayload { Handle = ToUInt64(handle) });
        }

        private void WriteHandleUInt32(CommandOpcode opcode, IntPtr handle, uint value)
        {
            WriteCommand(opcode, new CommandWire.HandleUInt32Payload
                { Handle = ToUInt64(handle), Value = value });
        }

        private void WriteTextureCopy(CommandOpcode opcode, IntPtr dest, TextureSlice destSlice,
            IntPtr src, TextureSlice srcSlice)
        {
            WriteCommand(opcode, new CommandWire.TextureCopyPayload
            {
                Dest = ToUInt64(dest), DestSlice = ToWire(destSlice),
                Src = ToUInt64(src), SrcSlice = ToWire(srcSlice),
            });
        }

        private static CommandWire.TextureSubresources ToWire(TextureSubresourceSet value)
        {
            return new CommandWire.TextureSubresources
            {
                BaseMipLevel = value.BaseMipLevel, NumMipLevels = value.NumMipLevels,
                BaseArraySlice = value.BaseArraySlice, NumArraySlices = value.NumArraySlices,
            };
        }

        private static CommandWire.TextureSlice ToWire(TextureSlice value)
        {
            return new CommandWire.TextureSlice
            {
                X = value.X, Y = value.Y, Z = value.Z,
                Width = value.Width, Height = value.Height, Depth = value.Depth,
                MipLevel = value.MipLevel, ArraySlice = value.ArraySlice,
            };
        }

        private unsafe void WriteTexturePayload<T>(ulong textureHandle, uint arraySlice, uint mipLevel,
            T[] data, ulong rowPitch, ulong depthPitch) where T : unmanaged
        {
            int byteCount = checked(data.Length * sizeof(T));
            IntPtr ticket;
            fixed (T* source = data)
                ticket = Interop.NativeMethods.UnityRhiStageTextureUpload(
                    unchecked((IntPtr)(long)textureHandle), arraySlice, mipLevel,
                    (IntPtr)source, checked((ulong)byteCount), rowPitch, depthPitch);
            if (ticket == IntPtr.Zero)
                throw new InvalidOperationException("Native texture upload allocation failed.");
            try
            {
                WriteCommand(CommandOpcode.WriteTexture, new CommandWire.WriteTexturePayload
                {
                    Texture = textureHandle,
                    ArraySlice = arraySlice, MipLevel = mipLevel, UploadTicket = ToUInt64(ticket),
                });
                _uploadTickets.Add(ticket);
                _recordedUploadBytes = checked(_recordedUploadBytes + (ulong)byteCount);
            }
            catch
            {
                Interop.NativeMethods.UnityRhiReleaseUploadTicket(ticket);
                throw;
            }
            _commandCount++;
        }

        private unsafe void WriteBufferUpload(Buffer buffer, void* data, ulong byteCount, ulong destOffsetBytes)
        {
            IntPtr ticket = Interop.NativeMethods.UnityRhiStageBufferUpload((IntPtr)data, byteCount);
            if (ticket == IntPtr.Zero)
                throw new InvalidOperationException("Native buffer upload allocation failed.");
            try
            {
                WriteCommand(CommandOpcode.WriteBuffer, new CommandWire.WriteBufferPayload
                {
                    Buffer = ToUInt64(buffer.Handle), DestOffset = destOffsetBytes,
                    UploadTicket = ToUInt64(ticket),
                });
                _uploadTickets.Add(ticket);
                _recordedUploadBytes = checked(_recordedUploadBytes + byteCount);
            }
            catch
            {
                Interop.NativeMethods.UnityRhiReleaseUploadTicket(ticket);
                throw;
            }
        }

        private void ReleaseUnsubmittedUploadTickets()
        {
            foreach (IntPtr ticket in _uploadTickets)
                if (ticket != IntPtr.Zero)
                    Interop.NativeMethods.UnityRhiReleaseUploadTicket(ticket);
            _uploadTickets.Clear();
        }

        private void RetainGeometryResources(Rt.GeometryDesc geometry)
        {
            if (geometry.GeometryType == Rt.GeometryType.Triangles)
            {
                if (geometry.Triangles.IndexBuffer != null)
                    Retain(geometry.Triangles.IndexBuffer);
                Retain(geometry.Triangles.VertexBuffer);
            }
            else
            {
                Retain(geometry.AABBs.Buffer);
            }
        }

    }

    /// <summary>
    /// Submission receipt retained for API compatibility. Native code owns the
    /// actual pending submission, so this object does not need to be kept alive.
    /// </summary>
    public sealed class SubmittedCommandList : IDisposable
    {
        public int ByteSize { get; private set; }

        internal SubmittedCommandList(int byteSize)
        {
            ByteSize = byteSize;
        }

        public void Dispose()
        {
            ByteSize = 0;
        }
    }
}
