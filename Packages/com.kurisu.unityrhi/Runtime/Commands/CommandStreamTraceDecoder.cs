using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityRhi.Interop;

namespace UnityRhi
{
    /// <summary>Debug-only ordered decoder for the managed command stream.</summary>
    internal static class CommandStreamTraceDecoder
    {
        private const uint Magic = 0x31494852;
        private const uint Version = 14;

        internal static CommandTraceEvent[] Decode(ReadOnlySpan<byte> stream)
        {
            try
            {
                int headerSize = SizeOf<CommandWire.StreamHeader>();
                if (stream.Length < headerSize)
                    return Array.Empty<CommandTraceEvent>();
                CommandWire.StreamHeader header = Read<CommandWire.StreamHeader>(stream, 0);
                if (header.Magic != Magic || header.Version != Version ||
                    header.ByteSize != stream.Length || header.CommandCount > int.MaxValue)
                    return Array.Empty<CommandTraceEvent>();

                int offset = headerSize;
                uint depth = 0;
                var events = new List<CommandTraceEvent>(checked((int)header.CommandCount));
                for (uint i = 0; i < header.CommandCount; ++i)
                {
                    int start = offset;
                    CommandOpcode opcode = (CommandOpcode)Read<uint>(stream, offset);
                    offset += sizeof(uint);
                    string label = "";
                    ulong[] args = new ulong[20];
                    var resources = new List<ulong>(8);

                    switch (opcode)
                    {
                        case CommandOpcode.CopyBuffer:
                        {
                            var c = Read<CommandWire.CopyBufferPayload>(stream, offset); offset += SizeOf<CommandWire.CopyBufferPayload>();
                            args[0] = c.Dest; args[1] = c.DestOffset; args[2] = c.Src; args[3] = c.SrcOffset; args[4] = c.ByteSize; break;
                        }
                        case CommandOpcode.SetComputeState:
                        {
                            var c = Read<CommandWire.StatePayload>(stream, offset); offset += SizeOf<CommandWire.StatePayload>();
                            args[0] = c.Object; args[1] = c.IndirectParams; args[2] = c.BindingCount; args[3] = c.Flags;
                            resources.Add(c.Object); resources.Add(c.IndirectParams);
                            if ((c.Flags & 1u) == 0)
                                offset = ReadHandles(stream, offset, c.BindingCount, resources);
                            break;
                        }
                        case CommandOpcode.Dispatch:
                        case CommandOpcode.DispatchRays:
                        {
                            var c = Read<CommandWire.DispatchPayload>(stream, offset); offset += SizeOf<CommandWire.DispatchPayload>();
                            args[0] = c.X; args[1] = c.Y; args[2] = c.Z; break;
                        }
                        case CommandOpcode.BeginMarker:
                        {
                            var c = Read<CommandWire.MarkerPayload>(stream, offset); offset += SizeOf<CommandWire.MarkerPayload>();
                            int length = checked((int)c.ByteSize); Ensure(stream, offset, length);
                            label = Encoding.UTF8.GetString(stream.Slice(offset, length).ToArray());
                            args[0] = c.ByteSize; offset += length; break;
                        }
                        case CommandOpcode.EndMarker:
                        case CommandOpcode.ClearState:
                        case CommandOpcode.CommitBarriers:
                            break;
                        case CommandOpcode.SetEnableAutomaticBarriers:
                        {
                            var c = Read<CommandWire.UInt32Payload>(stream, offset); offset += SizeOf<CommandWire.UInt32Payload>(); args[0] = c.Value; break;
                        }
                        case CommandOpcode.SetBufferState:
                        case CommandOpcode.SetTextureState:
                        case CommandOpcode.BeginTrackingBufferState:
                        case CommandOpcode.SetPermanentTextureState:
                        case CommandOpcode.SetPermanentBufferState:
                        case CommandOpcode.SetEnableUavBarriersForTexture:
                        case CommandOpcode.SetEnableUavBarriersForBuffer:
                        case CommandOpcode.ClearBufferUInt:
                        {
                            var c = Read<CommandWire.HandleUInt32Payload>(stream, offset); offset += SizeOf<CommandWire.HandleUInt32Payload>();
                            args[0] = c.Handle; args[1] = c.Value; break;
                        }
                        case CommandOpcode.BeginTrackingTextureState:
                        case CommandOpcode.SetTextureSubresourceState:
                        {
                            var c = Read<CommandWire.TextureSubresourceStatePayload>(stream, offset); offset += SizeOf<CommandWire.TextureSubresourceStatePayload>();
                            args[0] = c.Texture; args[1] = c.State; args[2] = c.Subresources.BaseMipLevel;
                            args[3] = c.Subresources.NumMipLevels; args[4] = c.Subresources.BaseArraySlice;
                            args[5] = c.Subresources.NumArraySlices; break;
                        }
                        case CommandOpcode.DispatchIndirect:
                        case CommandOpcode.UavBarrier:
                        case CommandOpcode.BeginTimerQuery:
                        case CommandOpcode.EndTimerQuery:
                        {
                            var c = Read<CommandWire.HandlePayload>(stream, offset); offset += SizeOf<CommandWire.HandlePayload>(); args[0] = c.Handle; break;
                        }
                        case CommandOpcode.WriteBuffer:
                        {
                            var c = Read<CommandWire.WriteBufferPayload>(stream, offset); offset += SizeOf<CommandWire.WriteBufferPayload>();
                            args[0] = c.Buffer; args[1] = c.DestOffset; args[2] = c.UploadTicket; break;
                        }
                        case CommandOpcode.SetPushConstants:
                        {
                            var c = Read<CommandWire.MarkerPayload>(stream, offset); offset += SizeOf<CommandWire.MarkerPayload>();
                            args[0] = c.ByteSize; offset = Skip(offset, c.ByteSize, 1, stream.Length); break;
                        }
                        case CommandOpcode.BuildBottomLevelAccelStruct:
                        case CommandOpcode.BuildTopLevelAccelStruct:
                        {
                            var c = Read<CommandWire.AccelStructBuildPayload>(stream, offset); offset += SizeOf<CommandWire.AccelStructBuildPayload>();
                            args[0] = c.AccelStruct; args[1] = c.ElementCount; args[2] = c.BuildFlags;
                            int elementSize = opcode == CommandOpcode.BuildBottomLevelAccelStruct
                                ? SizeOf<RhiRtGeometryDescNative>() : SizeOf<RhiRtInstanceDescNative>();
                            offset = Skip(offset, c.ElementCount, elementSize, stream.Length); break;
                        }
                        case CommandOpcode.BuildTopLevelAccelStructFromBuffer:
                        {
                            var c = Read<CommandWire.TlasFromBufferPayload>(stream, offset); offset += SizeOf<CommandWire.TlasFromBufferPayload>();
                            args[0] = c.AccelStruct; args[1] = c.InstanceBuffer; args[2] = c.InstanceBufferOffset;
                            args[3] = c.NumInstances; args[4] = c.BuildFlags; break;
                        }
                        case CommandOpcode.SetRayTracingState:
                        {
                            var c = Read<CommandWire.BindingStatePayload>(stream, offset); offset += SizeOf<CommandWire.BindingStatePayload>();
                            args[0] = c.Object; args[1] = c.BindingCount;
                            resources.Add(c.Object);
                            offset = ReadHandles(stream, offset, c.BindingCount, resources); break;
                        }
                        case CommandOpcode.WriteTexture:
                        {
                            var c = Read<CommandWire.WriteTexturePayload>(stream, offset); offset += SizeOf<CommandWire.WriteTexturePayload>();
                            args[0] = c.Texture; args[1] = c.ArraySlice; args[2] = c.MipLevel; args[3] = c.UploadTicket; break;
                        }
                        case CommandOpcode.CopyTextureToBuffer:
                        {
                            var c = Read<CommandWire.CopyTextureToBufferPayload>(stream, offset); offset += SizeOf<CommandWire.CopyTextureToBufferPayload>();
                            args[0] = c.Dest; args[1] = c.DestOffset; args[2] = c.Src; args[3] = c.ArraySlice; args[4] = c.MipLevel; break;
                        }
                        case CommandOpcode.CopyTexture:
                        case CommandOpcode.CopyTextureFromStaging:
                        case CommandOpcode.CopyTextureToStaging:
                        {
                            var c = Read<CommandWire.TextureCopyPayload>(stream, offset); offset += SizeOf<CommandWire.TextureCopyPayload>();
                            args[0] = c.Dest; args[1] = c.Src; args[2] = c.DestSlice.MipLevel; args[3] = c.DestSlice.ArraySlice;
                            args[4] = c.SrcSlice.MipLevel; args[5] = c.SrcSlice.ArraySlice; args[6] = c.SrcSlice.Width; args[7] = c.SrcSlice.Height; break;
                        }
                        case CommandOpcode.ResolveTexture:
                        {
                            var c = Read<CommandWire.ResolveTexturePayload>(stream, offset); offset += SizeOf<CommandWire.ResolveTexturePayload>();
                            args[0] = c.Dest; args[1] = c.Src; args[2] = c.DestSubresources.BaseMipLevel; args[3] = c.SrcSubresources.BaseMipLevel; break;
                        }
                        case CommandOpcode.SetGraphicsState:
                        {
                            var c = Read<CommandWire.GraphicsStatePayload>(stream, offset); offset += SizeOf<CommandWire.GraphicsStatePayload>();
                            args[0] = c.Pipeline; args[1] = c.Framebuffer; args[2] = c.IndexBuffer; args[3] = c.VertexBufferCount;
                            args[4] = c.IndirectParams; args[5] = c.IndirectCountBuffer;
                            args[7] = c.IndexFormat; args[8] = c.IndexOffset;
                            args[9] = FloatBits(c.Viewport.MinX); args[10] = FloatBits(c.Viewport.MaxX);
                            args[11] = FloatBits(c.Viewport.MinY); args[12] = FloatBits(c.Viewport.MaxY);
                            args[13] = FloatBits(c.Viewport.MinZ); args[14] = FloatBits(c.Viewport.MaxZ);
                            args[15] = FloatBits(c.BlendR); args[16] = FloatBits(c.BlendG);
                            args[17] = FloatBits(c.BlendB); args[18] = FloatBits(c.BlendA);
                            resources.Add(c.Pipeline); resources.Add(c.Framebuffer); resources.Add(c.IndexBuffer);
                            resources.Add(c.IndirectParams); resources.Add(c.IndirectCountBuffer);
                            for (uint vertexBuffer = 0; vertexBuffer < c.VertexBufferCount; ++vertexBuffer)
                            {
                                var binding = Read<CommandWire.VertexBufferBinding>(stream, offset);
                                offset += SizeOf<CommandWire.VertexBufferBinding>();
                                resources.Add(binding.Buffer);
                            }
                            uint bindings = Read<uint>(stream, offset); offset += sizeof(uint); args[6] = bindings;
                            offset = ReadHandles(stream, offset, bindings, resources); break;
                        }
                        case CommandOpcode.Draw:
                        {
                            var c = Read<CommandWire.DrawPayload>(stream, offset); offset += SizeOf<CommandWire.DrawPayload>();
                            args[0] = c.A; args[1] = c.B; args[2] = c.C; args[3] = c.D; break;
                        }
                        case CommandOpcode.DrawIndexed:
                        {
                            var c = Read<CommandWire.DrawIndexedPayload>(stream, offset); offset += SizeOf<CommandWire.DrawIndexedPayload>();
                            args[0] = c.A; args[1] = c.B; args[2] = c.C; args[3] = c.D; args[4] = c.E; break;
                        }
                        case CommandOpcode.DrawIndirect:
                        case CommandOpcode.DrawIndexedIndirect:
                        {
                            var c = Read<CommandWire.DrawIndirectPayload>(stream, offset); offset += SizeOf<CommandWire.DrawIndirectPayload>();
                            args[0] = c.Offset; args[1] = c.Count; break;
                        }
                        case CommandOpcode.DrawIndexedIndirectCount:
                        {
                            var c = Read<CommandWire.DrawIndirectCountPayload>(stream, offset); offset += SizeOf<CommandWire.DrawIndirectCountPayload>();
                            args[0] = c.ParamsOffset; args[1] = c.MaxDrawCount; args[2] = c.CountOffset; break;
                        }
                        case CommandOpcode.ClearTextureFloat:
                        {
                            var c = Read<CommandWire.ClearTextureFloatPayload>(stream, offset); offset += SizeOf<CommandWire.ClearTextureFloatPayload>();
                            args[0] = c.Texture; args[1] = FloatBits(c.R); args[2] = FloatBits(c.G);
                            args[3] = FloatBits(c.B); args[4] = FloatBits(c.A); break;
                        }
                        case CommandOpcode.ClearDepthStencilTexture:
                        {
                            var c = Read<CommandWire.ClearDepthStencilPayload>(stream, offset); offset += SizeOf<CommandWire.ClearDepthStencilPayload>();
                            args[0] = c.Texture; args[1] = c.ClearDepth; args[2] = FloatBits(c.Depth);
                            args[3] = c.ClearStencil; args[4] = c.Stencil; break;
                        }
                        case CommandOpcode.ClearTextureFloatSubresources:
                        {
                            var c = Read<CommandWire.ClearTextureFloatSubresourcesPayload>(stream, offset); offset += SizeOf<CommandWire.ClearTextureFloatSubresourcesPayload>();
                            args[0] = c.Texture; CopySubresources(c.Subresources, args);
                            args[5] = FloatBits(c.R); args[6] = FloatBits(c.G); args[7] = FloatBits(c.B); args[8] = FloatBits(c.A); break;
                        }
                        case CommandOpcode.ClearDepthStencilTextureSubresources:
                        {
                            var c = Read<CommandWire.ClearDepthStencilSubresourcesPayload>(stream, offset); offset += SizeOf<CommandWire.ClearDepthStencilSubresourcesPayload>();
                            args[0] = c.Texture; CopySubresources(c.Subresources, args);
                            args[5] = c.ClearDepth; args[6] = FloatBits(c.Depth); args[7] = c.ClearStencil; args[8] = c.Stencil; break;
                        }
                        case CommandOpcode.ClearTextureUInt:
                        {
                            var c = Read<CommandWire.ClearTextureUIntPayload>(stream, offset); offset += SizeOf<CommandWire.ClearTextureUIntPayload>();
                            args[0] = c.Texture; CopySubresources(c.Subresources, args); args[5] = c.Value; break;
                        }
                        case CommandOpcode.DispatchDlrr:
                        {
                            var c = Read<CommandWire.DlrrDispatchPayload>(stream, offset); offset += SizeOf<CommandWire.DlrrDispatchPayload>();
                            args[0] = unchecked((ulong)c.InstanceId); args[1] = c.OutputWidth; args[2] = c.OutputHeight;
                            resources.Add(c.Input); resources.Add(c.Output); resources.Add(c.MotionVectors); resources.Add(c.Depth);
                            resources.Add(c.DiffuseAlbedo); resources.Add(c.SpecularAlbedo);
                            resources.Add(c.NormalRoughness); resources.Add(c.SpecularMotion);
                            label = "DLSS Ray Reconstruction"; break;
                        }
                        case CommandOpcode.DispatchDlssNr:
                        {
                            var c = Read<CommandWire.DlssNrDispatchPayload>(stream, offset); offset += SizeOf<CommandWire.DlssNrDispatchPayload>();
                            args[0] = unchecked((ulong)c.InstanceId); args[1] = c.InputWidth; args[2] = c.InputHeight;
                            args[3] = c.OutputWidth; args[4] = c.OutputHeight;
                            resources.Add(c.Color); resources.Add(c.Output);
                            resources.Add(c.MotionVectors); resources.Add(c.Depth);
                            label = "DLSS Neural Rendering"; break;
                        }
                        default: return Array.Empty<CommandTraceEvent>();
                    }

                    if (opcode == CommandOpcode.EndMarker && depth != 0) --depth;
                    events.Add(new CommandTraceEvent
                    {
                        Opcode = opcode, ByteOffset = checked((uint)start), ByteSize = checked((uint)(offset - start)),
                        Depth = depth, Arguments = args, Resources = CompactResources(resources), Label = label,
                    });
                    if (opcode == CommandOpcode.BeginMarker) ++depth;
                }
                return offset == stream.Length ? events.ToArray() : Array.Empty<CommandTraceEvent>();
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is IndexOutOfRangeException)
            {
                return Array.Empty<CommandTraceEvent>();
            }
        }

        private static T Read<T>(ReadOnlySpan<byte> stream, int offset) where T : struct
        {
            int size = SizeOf<T>(); Ensure(stream, offset, size);
            return MemoryMarshal.Read<T>(stream.Slice(offset, size));
        }

        private static int Skip(int offset, ulong count, int elementSize, int length)
        {
            int bytes = checked((int)(count * (ulong)elementSize));
            if (offset < 0 || bytes < 0 || offset > length - bytes) throw new ArgumentException("Truncated command stream.");
            return offset + bytes;
        }

        private static void CopySubresources(CommandWire.TextureSubresources source, ulong[] args)
        {
            args[1] = source.BaseMipLevel; args[2] = source.NumMipLevels;
            args[3] = source.BaseArraySlice; args[4] = source.NumArraySlices;
        }

        private static ulong FloatBits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));

        private static int ReadHandles(ReadOnlySpan<byte> stream, int offset, uint count, List<ulong> resources)
        {
            for (uint i = 0; i < count; ++i)
            {
                ulong handle = Read<ulong>(stream, offset);
                offset += sizeof(ulong);
                resources.Add(handle);
            }
            return offset;
        }

        private static ulong[] CompactResources(List<ulong> resources)
        {
            if (resources.Count == 0)
                return Array.Empty<ulong>();
            var seen = new HashSet<ulong>();
            resources.RemoveAll(handle => handle == 0 || !seen.Add(handle));
            return resources.ToArray();
        }

        private static void Ensure(ReadOnlySpan<byte> stream, int offset, int size)
        {
            if (offset < 0 || size < 0 || offset > stream.Length - size) throw new ArgumentException("Truncated command stream.");
        }

        private static int SizeOf<T>() where T : struct => Marshal.SizeOf<T>();
    }
}
