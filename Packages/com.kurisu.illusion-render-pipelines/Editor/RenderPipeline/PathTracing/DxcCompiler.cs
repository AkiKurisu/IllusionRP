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

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32")]
        private static extern bool FreeLibrary(IntPtr module);

        private const uint LoadWithAlteredSearchPath = 0x8;
        private const int OutErrors = 2;
        private const int OutHlsl = 6;
        private const uint CodePageUtf8 = 65001;

        private static Guid s_ClsidCompiler = new("73e22d93-e6ce-47f3-b5bf-f0664f39c1b0");
        private static Guid s_ClsidUtils = new("6245d6af-66e0-48fd-80b4-4d271796748c");
        private static Guid s_IidCompiler3 = new("228b4687-5a6a-4730-900c-9702b2203f54");
        private static Guid s_IidUtils = new("4605c4cb-2019-492a-ada4-65f20bb7d67f");
        private static Guid s_IidResult = new("58346cda-dde7-4497-9461-6f87af5e0659");
        private static Guid s_IidBlob = new("8ba5fb08-5195-40e2-ac58-0d989c3a0102");

        private IntPtr _module;
        private IntPtr _compiler;
        private IntPtr _utils;
        private IntPtr _includeHandler;

        public static string ToolchainVersion
        {
            get
            {
                var versions = new StringBuilder();
                foreach (string name in new[] { "dxc.exe", "dxcompiler.dll", "dxil.dll" })
                {
                    string path = FileUtil.GetPhysicalPath("Packages/com.kurisu.unityrhi/Plugins/x86_64/" + name);
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    using var stream = File.OpenRead(path);
                    versions.Append(name).Append(':').Append(System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion)
                        .Append(':').Append(BitConverter.ToString(sha.ComputeHash(stream))).Append(';');
                }
                return versions.ToString();
            }
        }

        public static DxcCompiler Create()
        {
            string path = FileUtil.GetPhysicalPath("Packages/com.kurisu.unityrhi/Plugins/x86_64/dxcompiler.dll");
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (module == IntPtr.Zero)
                throw new InvalidOperationException($"Could not load '{path}'.");
            IntPtr proc = GetProcAddress(module, "DxcCreateInstance");
            if (proc == IntPtr.Zero)
                throw new InvalidOperationException("dxcompiler.dll has no DxcCreateInstance export.");
            var create = Marshal.GetDelegateForFunctionPointer<CreateInstanceFn>(proc);

            var dxc = new DxcCompiler { _module = module };
            Check(create(ref s_ClsidCompiler, ref s_IidCompiler3, out dxc._compiler), "create compiler");
            Check(create(ref s_ClsidUtils, ref s_IidUtils, out dxc._utils), "create utils");
            Check(Method<CreateDefaultIncludeHandlerFn>(dxc._utils, 9)(dxc._utils, out dxc._includeHandler), "create include handler");
            return dxc;
        }

        public Result Preprocess(string source, string sourceName, IEnumerable<string> arguments)
        {
            var args = new List<string> { sourceName, "-P" };
            args.AddRange(arguments);
            return Run(source, args);
        }

        public Result Compile(string source, string entryPoint, string profile, IEnumerable<string> arguments)
        {
            string executable = FileUtil.GetPhysicalPath("Packages/com.kurisu.unityrhi/Plugins/x86_64/dxc.exe");
            string directory = Path.GetFullPath("Library/IllusionRP/Compiler");
            Directory.CreateDirectory(directory);
            string input = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".hlsl");
            string output = input + ".dxil";
            try
            {
                File.WriteAllText(input, source, new UTF8Encoding(false));
                var args = new List<string> { input, "-T", profile, "-Fo", output };
                if (!string.IsNullOrEmpty(entryPoint)) args.AddRange(new[] { "-E", entryPoint });
                args.AddRange(arguments);
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(executable, string.Join(" ", args.ConvertAll(Quote)))
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
                    }
                };
                process.Start();
                var errors = process.StandardError.ReadToEndAsync();
                var messages = process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();
                return new Result(process.ExitCode == 0, null, errors.GetAwaiter().GetResult() + messages.GetAwaiter().GetResult(),
                    process.ExitCode == 0 ? File.ReadAllBytes(output) : null);
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        private static string Quote(string argument)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '\"' ? slashes * 2 + 1 : slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('\"').ToString();
        }

        private Result Run(string source, List<string> arguments)
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
                string output = ReadOutput(result, OutHlsl);
                return new Result(status >= 0, output, errors);
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
            if (_module != IntPtr.Zero) FreeLibrary(_module);
            _module = IntPtr.Zero;
        }
    }
}
