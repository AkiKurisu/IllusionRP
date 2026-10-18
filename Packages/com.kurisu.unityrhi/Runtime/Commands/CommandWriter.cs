using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityRhi.Interop;

namespace UnityRhi
{
    public struct CommandStreamAllocatorStats
    {
        public long NativeAllocations;
        public long Reallocations;
        public long PoolRentHits;
        public long PoolReturns;
        public int PeakCapacity;
        public int PeakStreamBytes;
    }

    /// <summary>Command streams and upload payloads actually submitted in one Unity frame.</summary>
    public struct CommandFrameStats
    {
        public int FrameIndex;
        public ulong UploadBytes;
        public ulong CommandStreamBytes;
        public uint CommandListCount;
        public uint MaxCommandListBytes;
    }

    public struct CommandOpcodeFrameStats
    {
        public CommandOpcode Opcode;
        public uint Count;
        public uint ByteSize;
    }

    public struct CommandListFrameStats
    {
        public uint Index;
        public uint ByteSize;
        public ulong UploadBytes;
        public CommandOpcodeFrameStats[] Commands;
        public CommandTraceEvent[] Events;
    }

    public sealed class CommandFrameCapture
    {
        public int FrameIndex { get; internal set; }
        public CommandListFrameStats[] CommandLists { get; internal set; }
        public ResourceInfo[] Resources { get; internal set; }
    }

    internal static class CommandFrameStatistics
    {
        private static readonly object Sync = new object();
        private static CommandFrameStats s_Current;
        private static readonly List<CommandListFrameStats> s_CommandLists =
            new List<CommandListFrameStats>(16);
        private static readonly List<CommandListFrameStats> s_CapturedLists =
            new List<CommandListFrameStats>(16);
        private static ResourceInfo[] s_CapturedResources = Array.Empty<ResourceInfo>();
        internal static volatile bool DetailedEnabled;
        private static bool s_CaptureRequested;
        private static bool s_CaptureComplete;
        private static int s_CaptureFrame = -1;

        internal static void RecordSubmission(int frameIndex, int commandStreamBytes, ulong uploadBytes,
            CommandStreamInfo? decoded)
        {
            if (commandStreamBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(commandStreamBytes));
            lock (Sync)
            {
                if (s_Current.CommandListCount == 0 || s_Current.FrameIndex != frameIndex)
                {
                    s_Current = new CommandFrameStats { FrameIndex = frameIndex };
                    s_CommandLists.Clear();
                }
                s_Current.UploadBytes = checked(s_Current.UploadBytes + uploadBytes);
                s_Current.CommandStreamBytes = checked(s_Current.CommandStreamBytes + (ulong)commandStreamBytes);
                s_Current.CommandListCount = checked(s_Current.CommandListCount + 1);
                s_Current.MaxCommandListBytes = Math.Max(s_Current.MaxCommandListBytes,
                    checked((uint)commandStreamBytes));
                if (decoded.HasValue && decoded.Value.IsValid)
                {
                    CommandListFrameStats list = CreateCommandListStats(s_Current.CommandListCount,
                        commandStreamBytes, uploadBytes, decoded.Value);
                    s_CommandLists.Add(list);
                    RecordCapture(frameIndex, list);
                }
            }
        }

        private static CommandListFrameStats CreateCommandListStats(uint index, int streamBytes,
            ulong uploadBytes, CommandStreamInfo decoded)
        {
            var commands = new List<CommandOpcodeFrameStats>();
            for (int i = 0; i < decoded.OpcodeBytes.Length; ++i)
            {
                if (decoded.OpcodeBytes[i] == 0)
                    continue;
                commands.Add(new CommandOpcodeFrameStats
                {
                    Opcode = (CommandOpcode)i,
                    Count = decoded.OpcodeCounts[i],
                    ByteSize = decoded.OpcodeBytes[i],
                });
            }
            commands.Sort((a, b) => b.ByteSize.CompareTo(a.ByteSize));
            return new CommandListFrameStats
            {
                Index = index,
                ByteSize = checked((uint)streamBytes),
                UploadBytes = uploadBytes,
                Commands = commands.ToArray(),
                Events = decoded.Events ?? Array.Empty<CommandTraceEvent>(),
            };
        }

        private static void RecordCapture(int frameIndex, CommandListFrameStats list)
        {
            if (!s_CaptureRequested)
                return;
            if (frameIndex < s_CaptureFrame)
                return;
            if (frameIndex > s_CaptureFrame && s_CapturedLists.Count == 0)
                s_CaptureFrame = frameIndex;
            if (frameIndex == s_CaptureFrame)
            {
                if (s_CapturedLists.Count == 0 && RhiCore.IsD3D12Active)
                    s_CapturedResources = Device.Instance.GetResourceSnapshot();
                s_CapturedLists.Add(list);
                return;
            }
            s_CaptureRequested = false;
            s_CaptureComplete = true;
        }

        internal static void RequestCapture(int earliestFrame)
        {
            lock (Sync)
            {
                s_CapturedLists.Clear();
                s_CapturedResources = Array.Empty<ResourceInfo>();
                s_CaptureFrame = earliestFrame;
                s_CaptureComplete = false;
                s_CaptureRequested = true;
            }
        }

        internal static void CancelCapture()
        {
            lock (Sync)
            {
                s_CaptureRequested = false;
                s_CaptureFrame = -1;
                s_CapturedLists.Clear();
                s_CapturedResources = Array.Empty<ResourceInfo>();
            }
        }

        internal static bool CaptureRequested
        {
            get { lock (Sync) return s_CaptureRequested; }
        }

        internal static CommandFrameCapture GetCapture(int currentFrame)
        {
            lock (Sync)
            {
                if (s_CaptureRequested && s_CapturedLists.Count != 0 && currentFrame > s_CaptureFrame)
                {
                    s_CaptureRequested = false;
                    s_CaptureComplete = true;
                }
                if (!s_CaptureComplete)
                    return null;
                return new CommandFrameCapture
                {
                    FrameIndex = s_CaptureFrame,
                    CommandLists = s_CapturedLists.ToArray(),
                    Resources = s_CapturedResources,
                };
            }
        }

        internal static CommandFrameStats GetStats()
        {
            lock (Sync)
                return s_Current;
        }

        internal static CommandListFrameStats[] GetCommandLists()
        {
            lock (Sync)
                return s_CommandLists.ToArray();
        }
    }

    internal static class CommandStreamBufferPool
    {
        private const int MinCapacity = 4096;
        private const int MaxPooledCapacity = 1024 * 1024;
        private const int MaxBuffersPerBucket = 8;
        private const int BucketCount = 9;

        private static readonly Stack<IntPtr>[] s_Buckets = CreateBuckets();
        private static readonly object[] s_Locks = CreateLocks();
        private static long s_NativeAllocations;
        private static long s_Reallocations;
        private static long s_PoolRentHits;
        private static long s_PoolReturns;
        private static int s_PeakCapacity;
        private static int s_PeakStreamBytes;

        internal static IntPtr Rent(int minimumCapacity, out int capacity)
        {
            capacity = RoundCapacity(minimumCapacity);
            int bucket = GetBucket(capacity);
            if (bucket >= 0)
            {
                lock (s_Locks[bucket])
                {
                    if (s_Buckets[bucket].Count != 0)
                    {
                        Interlocked.Increment(ref s_PoolRentHits);
                        return s_Buckets[bucket].Pop();
                    }
                }
            }

            Interlocked.Increment(ref s_NativeAllocations);
            UpdateMaximum(ref s_PeakCapacity, capacity);
            return Marshal.AllocHGlobal(capacity);
        }

        internal static void Return(IntPtr pointer, int capacity)
        {
            if (pointer == IntPtr.Zero)
                return;

            int bucket = GetBucket(capacity);
            if (bucket >= 0)
            {
                lock (s_Locks[bucket])
                {
                    if (s_Buckets[bucket].Count < MaxBuffersPerBucket)
                    {
                        s_Buckets[bucket].Push(pointer);
                        Interlocked.Increment(ref s_PoolReturns);
                        return;
                    }
                }
            }
            Marshal.FreeHGlobal(pointer);
        }

        internal static void RecordReallocation(int capacity)
        {
            Interlocked.Increment(ref s_Reallocations);
            UpdateMaximum(ref s_PeakCapacity, capacity);
        }

        internal static void RecordStreamBytes(int byteSize)
        {
            UpdateMaximum(ref s_PeakStreamBytes, byteSize);
        }

        internal static CommandStreamAllocatorStats GetStats()
        {
            return new CommandStreamAllocatorStats
            {
                NativeAllocations = Interlocked.Read(ref s_NativeAllocations),
                Reallocations = Interlocked.Read(ref s_Reallocations),
                PoolRentHits = Interlocked.Read(ref s_PoolRentHits),
                PoolReturns = Interlocked.Read(ref s_PoolReturns),
                PeakCapacity = Volatile.Read(ref s_PeakCapacity),
                PeakStreamBytes = Volatile.Read(ref s_PeakStreamBytes),
            };
        }

        private static int RoundCapacity(int required)
        {
            int capacity = MinCapacity;
            while (capacity < required && capacity <= int.MaxValue / 2)
                capacity *= 2;
            return capacity < required ? required : capacity;
        }

        private static int GetBucket(int capacity)
        {
            if (capacity < MinCapacity || capacity > MaxPooledCapacity ||
                (capacity & (capacity - 1)) != 0)
                return -1;
            int bucket = 0;
            for (int value = capacity; value > MinCapacity; value >>= 1)
                ++bucket;
            return bucket < BucketCount ? bucket : -1;
        }

        private static Stack<IntPtr>[] CreateBuckets()
        {
            var result = new Stack<IntPtr>[BucketCount];
            for (int i = 0; i < result.Length; ++i)
                result[i] = new Stack<IntPtr>();
            return result;
        }

        private static object[] CreateLocks()
        {
            var result = new object[BucketCount];
            for (int i = 0; i < result.Length; ++i)
                result[i] = new object();
            return result;
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            int current = Volatile.Read(ref target);
            while (value > current)
            {
                int observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                    break;
                current = observed;
            }
        }
    }

    /// <summary>
    /// Unmanaged, growable writer for the Version 14 command-stream ABI.
    /// Submitted streams detach their allocation, so recording never needs an
    /// intermediate managed byte array or a second full-stream copy.
    /// </summary>
    internal sealed unsafe class CommandWriter : IDisposable
    {
        private const int InitialCapacity = 4096;

        private IntPtr _data;
        private int _length;
        private int _capacity;
        private int _preferredCapacity = InitialCapacity;
        private int _recordingReallocations;
        private int _reservedSpanCount;

        internal int Length => _length;
        internal IntPtr Pointer => _data;
        internal int Capacity => _capacity;
        internal int RecordingReallocations => _recordingReallocations;
        internal int ReservedSpanCount => _reservedSpanCount;
        internal ReadOnlySpan<byte> Bytes => new ReadOnlySpan<byte>((void*)_data, _length);

        internal void Reset()
        {
            EnsureCapacity(_preferredCapacity);
            _length = 0;
            _recordingReallocations = 0;
            _reservedSpanCount = 0;
        }

        internal void Write<T>(in T value) where T : unmanaged
        {
            int size = sizeof(T);
            EnsureCapacity(checked(_length + size));
            fixed (T* source = &value)
                System.Buffer.MemoryCopy(source, (byte*)_data + _length, size, size);
            _length += size;
        }

        internal void WriteSpan<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            if (values.IsEmpty)
                return;

            int size = checked(values.Length * sizeof(T));
            EnsureCapacity(checked(_length + size));
            fixed (T* source = values)
                System.Buffer.MemoryCopy(source, (byte*)_data + _length, size, size);
            _length += size;
        }

        internal void WriteSpan<T>(T[] values) where T : unmanaged
        {
            WriteSpan(new ReadOnlySpan<T>(values));
        }

        internal Span<T> AllocateSpan<T>(int count) where T : unmanaged
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return Span<T>.Empty;
            int size = checked(count * sizeof(T));
            EnsureCapacity(checked(_length + size));
            var result = new Span<T>((byte*)_data + _length, count);
            _length += size;
            ++_reservedSpanCount;
            return result;
        }

        internal void Patch<T>(int offset, in T value) where T : unmanaged
        {
            int size = sizeof(T);
            if (offset < 0 || offset > _length - size)
                throw new ArgumentOutOfRangeException(nameof(offset));
            fixed (T* source = &value)
                System.Buffer.MemoryCopy(source, (byte*)_data + offset, size, size);
        }

        internal IntPtr Detach(out int length, out int capacity)
        {
            IntPtr result = _data;
            length = _length;
            capacity = _capacity;
            _preferredCapacity = Math.Max(InitialCapacity, _capacity);
            _data = IntPtr.Zero;
            _length = 0;
            _capacity = 0;
            return result;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _capacity)
                return;

            if (_data == IntPtr.Zero)
            {
                _data = CommandStreamBufferPool.Rent(required, out int rentedCapacity);
                _capacity = rentedCapacity;
                return;
            }

            IntPtr oldData = _data;
            int oldCapacity = _capacity;
            IntPtr newData = CommandStreamBufferPool.Rent(required, out int newCapacity);
            System.Buffer.MemoryCopy((void*)oldData, (void*)newData, newCapacity, _length);
            _data = newData;
            _capacity = newCapacity;
            _preferredCapacity = Math.Max(_preferredCapacity, newCapacity);
            CommandStreamBufferPool.Return(oldData, oldCapacity);
            ++_recordingReallocations;
            CommandStreamBufferPool.RecordReallocation(newCapacity);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_data != IntPtr.Zero)
                CommandStreamBufferPool.Return(_data, _capacity);
            _data = IntPtr.Zero;
            _length = 0;
            _capacity = 0;
        }

        ~CommandWriter()
        {
            Dispose(false);
        }
    }

    internal sealed unsafe class CommandStreamAllocation
    {
        private static readonly object PoolLock = new object();
        private static readonly Stack<CommandStreamAllocation> Pool =
            new Stack<CommandStreamAllocation>(16);

        private IntPtr _pointer;
        private IntPtr[] _uploadTickets = Array.Empty<IntPtr>();
        private int _uploadTicketCount;
        private IntPtr[] _resourceHandles = Array.Empty<IntPtr>();
        private Resource[] _resourceObjects = Array.Empty<Resource>();
        private int _resourceHandleCount;
        private bool _poolable;

        internal CommandStreamAllocation(IntPtr pointer, int byteSize, int capacity,
            IntPtr[] uploadTickets = null, int uploadTicketCount = 0)
        {
            _pointer = pointer;
            ByteSize = byteSize;
            Capacity = capacity;
            _uploadTickets = uploadTickets ?? Array.Empty<IntPtr>();
            _uploadTicketCount = uploadTicketCount;
        }

        internal static CommandStreamAllocation Rent(IntPtr pointer, int byteSize, int capacity,
            List<IntPtr> uploadTickets, Dictionary<IntPtr, Resource> resources)
        {
            CommandStreamAllocation allocation;
            lock (PoolLock)
                allocation = Pool.Count > 0
                    ? Pool.Pop()
                    : new CommandStreamAllocation(IntPtr.Zero, 0, 0);
            allocation._pointer = pointer;
            allocation.ByteSize = byteSize;
            allocation.Capacity = capacity;
            allocation._poolable = true;
            int ticketCount = uploadTickets.Count;
            if (allocation._uploadTickets.Length < ticketCount)
                Array.Resize(ref allocation._uploadTickets, Math.Max(ticketCount,
                    Math.Max(16, allocation._uploadTickets.Length * 2)));
            uploadTickets.CopyTo(allocation._uploadTickets, 0);
            allocation._uploadTicketCount = ticketCount;
            int resourceHandleCount = resources.Count;
            if (allocation._resourceHandles.Length < resourceHandleCount)
                Array.Resize(ref allocation._resourceHandles, Math.Max(resourceHandleCount,
                    Math.Max(16, allocation._resourceHandles.Length * 2)));
            if (allocation._resourceObjects.Length < resourceHandleCount)
                Array.Resize(ref allocation._resourceObjects, Math.Max(resourceHandleCount,
                    Math.Max(16, allocation._resourceObjects.Length * 2)));
            int resourceIndex = 0;
            foreach (KeyValuePair<IntPtr, Resource> pair in resources)
            {
                allocation._resourceHandles[resourceIndex] = pair.Key;
                allocation._resourceObjects[resourceIndex] = pair.Value;
                resourceIndex++;
            }
            allocation._resourceHandleCount = resourceHandleCount;
            return allocation;
        }

        internal IntPtr Pointer => _pointer;
        internal int ByteSize { get; private set; }
        internal int Capacity { get; private set; }
        internal ReadOnlySpan<byte> Bytes => new ReadOnlySpan<byte>((void*)_pointer, ByteSize);

        internal IntPtr CreateNativeSubmission()
        {
            IntPtr submission = Interop.NativeMethods.UnityRhiCreateCommandSubmission(
                _pointer, checked((uint)ByteSize), _resourceHandles,
                checked((uint)_resourceHandleCount), _uploadTickets,
                checked((uint)_uploadTicketCount));
            if (submission == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(Resource), BuildSubmissionFailureMessage());
            return submission;
        }

        private string BuildSubmissionFailureMessage()
        {
            var message = new StringBuilder(512);
            message.Append("A resource or upload ticket referenced by the command stream was disposed before submission.")
                .Append(" Stream bytes=").Append(ByteSize)
                .Append(", resources=").Append(_resourceHandleCount)
                .Append(", uploads=").Append(_uploadTicketCount).Append('.');

            int disposedCount = 0;
            for (int i = 0; i < _resourceHandleCount; ++i)
            {
                Resource resource = _resourceObjects[i];
                if (resource != null && resource.IsValid)
                    continue;
                disposedCount++;
                message.AppendLine().Append("Disposed resource #").Append(i)
                    .Append(": handle=0x")
                    .Append(unchecked((ulong)_resourceHandles[i].ToInt64()).ToString("X"));
                if (resource == null)
                {
                    message.Append(", managed wrapper unavailable");
                    continue;
                }
                message.Append(", type=").Append(resource.GetType().Name)
                    .Append(", name='").Append(resource.DebugName ?? "<unnamed>").Append('\'');
                if (!string.IsNullOrEmpty(resource.DisposeStack))
                    message.AppendLine().Append("Dispose stack:").AppendLine()
                        .Append(resource.DisposeStack);
            }

            if (disposedCount == 0)
                message.AppendLine().Append(
                    "All managed resource wrappers are still valid; inspect native UnityRHI logs for an " +
                    "externally released handle or allocation failure.");
            else
                message.AppendLine().Append("Disposed managed resource count: ").Append(disposedCount).Append('.');
            return message.ToString();
        }

        internal void Release()
        {
            IntPtr pointer = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
            if (pointer != IntPtr.Zero)
                CommandStreamBufferPool.Return(pointer, Capacity);
            for (int i = 0; i < _uploadTicketCount; ++i)
            {
                IntPtr ticket = _uploadTickets[i];
                if (ticket != IntPtr.Zero)
                    Interop.NativeMethods.UnityRhiReleaseUploadTicket(ticket);
                _uploadTickets[i] = IntPtr.Zero;
            }
            _uploadTicketCount = 0;
            Array.Clear(_resourceHandles, 0, _resourceHandleCount);
            Array.Clear(_resourceObjects, 0, _resourceHandleCount);
            _resourceHandleCount = 0;
            if (_poolable)
            {
                _poolable = false;
                ByteSize = 0;
                Capacity = 0;
                lock (PoolLock)
                    Pool.Push(this);
            }
        }
    }
}
