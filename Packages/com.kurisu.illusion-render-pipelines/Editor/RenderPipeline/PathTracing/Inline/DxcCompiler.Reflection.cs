using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Illusion.Rendering.Editor
{
    internal sealed partial class DxcCompiler
    {
        internal sealed class Reflection
        {
            public readonly List<Resource> Resources = new();
            public readonly List<ConstantBuffer> ConstantBuffers = new();
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Resource
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Name;
            public uint Type, BindPoint, BindCount, Flags, ReturnType, Dimension, Samples, Space, Id;
        }

        internal sealed class ConstantBuffer
        {
            public string Name;
            public uint Size;
            public readonly List<Variable> Variables = new();
        }

        internal sealed class Variable
        {
            public string Name;
            public uint Offset, Size;
            public ReflectedType Type;
        }

        internal sealed class ReflectedType
        {
            public TypeDescription Description;
            public readonly List<(string Name, ReflectedType Type)> Members = new();
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TypeDescription
        {
            public uint Class, Type, Rows, Columns, Elements, Members, Offset;
            [MarshalAs(UnmanagedType.LPStr)] public string Name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShaderDescriptionPrefix
        {
            public uint Version;
            public IntPtr Creator;
            public uint Flags, ConstantBuffers, BoundResources;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BufferDescription
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Name;
            public uint Type, Variables, Size, Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VariableDescription
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Name;
            public uint Offset, Size, Flags;
            public IntPtr DefaultValue;
            public uint StartTexture, TextureSize, StartSampler, SamplerSize;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateReflectionFn(IntPtr self, ref DxcBuffer data, ref Guid iid, out IntPtr reflection);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DescriptionFn(IntPtr self, IntPtr description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr IndexedPointerFn(IntPtr self, uint index);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr PointerFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ResourceFn(IntPtr self, uint index, IntPtr resource);

        public Reflection Reflect(byte[] code, bool library)
        {
            var iid = new Guid(library ? "8e349d19-54db-4a56-9dc9-119d87bdb804" : "5a58797d-a72c-478d-8ba2-efc6b0efe88e");
            var pinned = GCHandle.Alloc(code, GCHandleType.Pinned);
            IntPtr reflection = IntPtr.Zero;
            try
            {
                var data = new DxcBuffer { Ptr = pinned.AddrOfPinnedObject(), Size = (UIntPtr)code.Length };
                Check(Method<CreateReflectionFn>(_utils, 13)(_utils, ref data, ref iid, out reflection), "reflect bytecode");
                IntPtr shader = library ? Method<IndexedPointerFn>(reflection, 4)(reflection, 0) : reflection;
                if (shader == IntPtr.Zero) throw new InvalidOperationException("DXIL contains no entry point.");
                var desc = Describe<ShaderDescriptionPrefix>(shader, library ? 0 : 3);
                var result = new Reflection();
                for (uint i = 0; i < desc.BoundResources; i++)
                {
                    IntPtr resource = Marshal.AllocHGlobal(Marshal.SizeOf<Resource>());
                    try
                    {
                        Check(Method<ResourceFn>(shader, library ? 3 : 6)(shader, i, resource), "reflect resource");
                        result.Resources.Add(Marshal.PtrToStructure<Resource>(resource));
                    }
                    finally { Marshal.FreeHGlobal(resource); }
                }
                for (uint i = 0; i < desc.ConstantBuffers; i++)
                {
                    var buffer = Method<IndexedPointerFn>(shader, library ? 1 : 4)(shader, i);
                    var description = Describe<BufferDescription>(buffer);
                    if (description.Type != 0) continue;
                    var item = new ConstantBuffer { Name = description.Name, Size = description.Size };
                    for (uint j = 0; j < description.Variables; j++)
                    {
                        var variable = Method<IndexedPointerFn>(buffer, 1)(buffer, j);
                        var value = Describe<VariableDescription>(variable);
                        item.Variables.Add(new Variable
                        {
                            Name = value.Name, Offset = value.Offset, Size = value.Size,
                            Type = ReadType(Method<PointerFn>(variable, 1)(variable))
                        });
                    }
                    result.ConstantBuffers.Add(item);
                }
                return result;
            }
            finally
            {
                if (reflection != IntPtr.Zero) Release(reflection);
                pinned.Free();
            }
        }

        private static ReflectedType ReadType(IntPtr type)
        {
            var result = new ReflectedType { Description = Describe<TypeDescription>(type) };
            for (uint i = 0; i < result.Description.Members; i++)
                result.Members.Add((Marshal.PtrToStringAnsi(Method<IndexedPointerFn>(type, 3)(type, i)),
                    ReadType(Method<IndexedPointerFn>(type, 1)(type, i))));
            return result;
        }

        private static T Describe<T>(IntPtr instance, int slot = 0) where T : struct
        {
            IntPtr data = Marshal.AllocHGlobal(512);
            try
            {
                Check(Method<DescriptionFn>(instance, slot)(instance, data), "read reflection description");
                return Marshal.PtrToStructure<T>(data);
            }
            finally { Marshal.FreeHGlobal(data); }
        }
    }
}
