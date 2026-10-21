using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;

namespace Illusion.Rendering.Editor
{
    internal sealed partial class DxcCompiler : IDisposable
    {
        internal readonly struct Result
        {
            public readonly bool Succeeded;
            public readonly string Output;
            public readonly string Errors;
            public readonly byte[] Bytecode;

            public Result(bool succeeded, string output, string errors, byte[] bytecode = null)
            {
                Succeeded = succeeded;
                Output = output;
                Errors = errors;
                Bytecode = bytecode;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DxcBuffer
        {
            public IntPtr Ptr;
            public UIntPtr Size;
            public uint Encoding;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateInstanceFn(ref Guid clsid, ref Guid iid, out IntPtr instance);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CompileFn(IntPtr self, ref DxcBuffer source, IntPtr[] arguments, uint argumentCount,
            IntPtr includeHandler, ref Guid iid, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDefaultIncludeHandlerFn(IntPtr self, out IntPtr handler);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetStatusFn(IntPtr self, out int status);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetOutputFn(IntPtr self, int kind, ref Guid iid, out IntPtr output, IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetBufferPointerFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate UIntPtr GetBufferSizeFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateBlobFn(IntPtr self, IntPtr data, uint size, uint encoding, out IntPtr blob);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ValidateFn(IntPtr self, IntPtr blob, uint flags, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBlobFn(IntPtr self, out IntPtr blob);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32")]
        private static extern bool FreeLibrary(IntPtr module);

        private const uint LoadWithAlteredSearchPath = 0x8;
        private const int OutErrors = 2;
        private const int OutHlsl = 6;
        private const int OutObject = 1;
        private const uint CodePageUtf8 = 65001;

        private static Guid s_ClsidCompiler = new("73e22d93-e6ce-47f3-b5bf-f0664f39c1b0");
        private static Guid s_ClsidUtils = new("6245d6af-66e0-48fd-80b4-4d271796748c");
        private static Guid s_IidCompiler3 = new("228b4687-5a6a-4730-900c-9702b2203f54");
        private static Guid s_IidUtils = new("4605c4cb-2019-492a-ada4-65f20bb7d67f");
        private static Guid s_IidResult = new("58346cda-dde7-4497-9461-6f87af5e0659");
        private static Guid s_IidBlob = new("8ba5fb08-5195-40e2-ac58-0d989c3a0102");
        private static Guid s_ClsidValidator = new("8ca3e215-f728-4cf3-8cdd-88af917587a1");
        private static Guid s_IidValidator = new("a6e82bd2-1fd7-4826-9811-2857e797f49a");

        private IntPtr _compiler;
        private IntPtr _utils;
        private IntPtr _includeHandler;

        public static DxcCompiler Create()
        {
            string path = Path.Combine(EditorApplication.applicationContentsPath, "Tools", "dxcompiler.dll");
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (module == IntPtr.Zero)
                throw new InvalidOperationException($"Could not load '{path}'.");
            IntPtr proc = GetProcAddress(module, "DxcCreateInstance");
            if (proc == IntPtr.Zero)
                throw new InvalidOperationException("dxcompiler.dll has no DxcCreateInstance export.");
            var create = Marshal.GetDelegateForFunctionPointer<CreateInstanceFn>(proc);

            var dxc = new DxcCompiler();
            Check(create(ref s_ClsidCompiler, ref s_IidCompiler3, out dxc._compiler), "create compiler");
            Check(create(ref s_ClsidUtils, ref s_IidUtils, out dxc._utils), "create utils");
            Check(Method<CreateDefaultIncludeHandlerFn>(dxc._utils, 9)(dxc._utils, out dxc._includeHandler), "create include handler");
            return dxc;
        }

        public Result Preprocess(string source, string sourceName, IEnumerable<string> arguments)
        {
            var args = new List<string> { sourceName, "-P" };
            args.AddRange(arguments);
            return Run(source, args, OutHlsl);
        }

        public Result Compile(string source, string sourceName, string entryPoint, string profile, IEnumerable<string> arguments, bool bytecode = false)
        {
            var args = new List<string> { sourceName, "-E", entryPoint, "-T", profile, "-Vd" };
            args.AddRange(arguments);
            var result = Run(source, args, OutObject, bytecode);
            return bytecode && result.Succeeded
                ? new Result(true, result.Output, result.Errors, Validate(result.Bytecode))
                : result;
        }

        private byte[] Validate(byte[] code)
        {
            string path = FileUtil.GetPhysicalPath("Packages/com.kurisu.unityrhi/Plugins/x86_64/dxil.dll");
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (module == IntPtr.Zero)
                throw new InvalidOperationException($"Could not load DXIL validator '{path}'.");
            IntPtr validator = IntPtr.Zero, blob = IntPtr.Zero, result = IntPtr.Zero, output = IntPtr.Zero;
            var pinned = GCHandle.Alloc(code, GCHandleType.Pinned);
            try
            {
                var create = Marshal.GetDelegateForFunctionPointer<CreateInstanceFn>(GetProcAddress(module, "DxcCreateInstance"));
                Check(create(ref s_ClsidValidator, ref s_IidValidator, out validator), "create validator");
                Check(Method<CreateBlobFn>(_utils, 4)(_utils, pinned.AddrOfPinnedObject(), (uint)code.Length, 0, out blob), "create bytecode blob");
                Check(Method<ValidateFn>(validator, 3)(validator, blob, 0, out result), "validate bytecode");
                Check(Method<GetStatusFn>(result, 3)(result, out int status), "read validation status");
                if (status < 0)
                {
                    Method<GetBlobFn>(result, 5)(result, out output);
                    throw new InvalidOperationException("DXIL validation failed: " + Encoding.UTF8.GetString(ReadBlob(output)).TrimEnd('\0'));
                }
                Check(Method<GetBlobFn>(result, 4)(result, out output), "read validated bytecode");
                return ReadBlob(output);
            }
            finally
            {
                if (output != IntPtr.Zero) Release(output);
                if (result != IntPtr.Zero) Release(result);
                if (blob != IntPtr.Zero) Release(blob);
                if (validator != IntPtr.Zero) Release(validator);
                pinned.Free();
                FreeLibrary(module);
            }
        }

        private Result Run(string source, List<string> arguments, int outputKind, bool bytecode = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var argPointers = new IntPtr[arguments.Count];
            IntPtr result = IntPtr.Zero;
            try
            {
                for (int i = 0; i < arguments.Count; i++)
                    argPointers[i] = Marshal.StringToHGlobalUni(arguments[i]);
                var buffer = new DxcBuffer
                {
                    Ptr = pinned.AddrOfPinnedObject(),
                    Size = (UIntPtr)bytes.Length,
                    Encoding = CodePageUtf8
                };
                Check(Method<CompileFn>(_compiler, 3)(_compiler, ref buffer, argPointers, (uint)argPointers.Length,
                    _includeHandler, ref s_IidResult, out result), "compile");

                Method<GetStatusFn>(result, 3)(result, out int status);
                string errors = ReadOutput(result, OutErrors);
                string output = outputKind == OutHlsl ? ReadOutput(result, OutHlsl) : null;
                return new Result(status >= 0, output, errors, bytecode && status >= 0 ? ReadBytes(result, OutObject) : null);
            }
            finally
            {
                if (result != IntPtr.Zero)
                    Release(result);
                foreach (IntPtr arg in argPointers)
                    if (arg != IntPtr.Zero)
                        Marshal.FreeHGlobal(arg);
                pinned.Free();
            }
        }

        private static string ReadOutput(IntPtr result, int kind)
        {
            return Encoding.UTF8.GetString(ReadBytes(result, kind)).TrimEnd('\0');
        }

        private static byte[] ReadBytes(IntPtr result, int kind)
        {
            if (Method<GetOutputFn>(result, 7)(result, kind, ref s_IidBlob, out IntPtr blob, IntPtr.Zero) < 0 || blob == IntPtr.Zero)
                return Array.Empty<byte>();
            try
            {
                return ReadBlob(blob);
            }
            finally
            {
                Release(blob);
            }
        }

        private static byte[] ReadBlob(IntPtr blob)
        {
            if (blob == IntPtr.Zero) return Array.Empty<byte>();
            IntPtr data = Method<GetBufferPointerFn>(blob, 3)(blob);
            int size = (int)Method<GetBufferSizeFn>(blob, 4)(blob).ToUInt64();
            var bytes = new byte[size];
            if (size > 0) Marshal.Copy(data, bytes, 0, size);
            return bytes;
        }

        private static T Method<T>(IntPtr instance, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }

        private static void Release(IntPtr instance) => Method<ReleaseFn>(instance, 2)(instance);

        private static void Check(int hr, string what)
        {
            if (hr < 0)
                throw new InvalidOperationException($"DXC {what} failed (0x{hr:X8}).");
        }

        public void Dispose()
        {
            if (_includeHandler != IntPtr.Zero) Release(_includeHandler);
            if (_utils != IntPtr.Zero) Release(_utils);
            if (_compiler != IntPtr.Zero) Release(_compiler);
            _includeHandler = _utils = _compiler = IntPtr.Zero;
        }
    }
}
