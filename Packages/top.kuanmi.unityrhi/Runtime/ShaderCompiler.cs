using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityRhi.Interop;

namespace UnityRhi
{
    [Flags]
    public enum ShaderCompileFlags : uint
    {
        None = 0,
        DebugInfo = 1 << 0,         // -Zi -Qembed_debug
        SkipOptimizations = 1 << 1, // -Od (default is -O3)
        WarningsAreErrors = 1 << 2, // -WX
        Enable16BitTypes = 1 << 3,  // -enable-16bit-types
        StripReflection = 1 << 4,   // -Qstrip_reflect
        PayloadQualifiers = 1 << 5, // -enable-payload-qualifiers (lib_6_6 raytracing)
        AllResourcesBound = 1 << 6, // -all_resources_bound
        StableShaderHash = 1 << 7,  // -Zsb (hash executable shader code only)
    }

    /// <summary>
    /// HLSL to DXIL via the bundled DXC (dxcompiler.dll next to the native
    /// plugin). Compilation is device-independent; pair with
    /// Device.CreateShader to obtain a Shader handle.
    /// </summary>
    public static class ShaderCompiler
    {
        public sealed class Result
        {
            public bool Succeeded;
            public byte[] Bytecode;   // DXIL container ("DXBC" magic); null on failure
            public string Diagnostics; // errors, or warnings on success
        }

        /// <summary>Compiles HLSL source text. Defines are "NAME" or "NAME=VALUE".</summary>
        public static Result Compile(
            string source, string sourceName, string entryPoint, string targetProfile,
            IReadOnlyList<string> defines = null, IReadOnlyList<string> includeDirs = null,
            ShaderCompileFlags flags = ShaderCompileFlags.None)
        {
            if (string.IsNullOrEmpty(source)) throw new ArgumentException(nameof(source));
            if (string.IsNullOrEmpty(targetProfile)) throw new ArgumentException(nameof(targetProfile));

            IntPtr native = NativeMethods.UnityRhiCompileShader(
                source, sourceName ?? "shader.hlsl", entryPoint ?? "", targetProfile,
                defines != null ? string.Join("\n", defines) : "",
                includeDirs != null ? string.Join("\n", includeDirs) : "",
                (uint)flags);
            try
            {
                var result = new Result
                {
                    Succeeded = NativeMethods.UnityRhiShaderCompileGetSucceeded(native) != 0,
                    Diagnostics = Marshal.PtrToStringUTF8(NativeMethods.UnityRhiShaderCompileGetErrors(native)) ?? "",
                };
                if (result.Succeeded)
                {
                    IntPtr data = NativeMethods.UnityRhiShaderCompileGetBytecode(native, out ulong size);
                    if (size > int.MaxValue)
                        throw new InvalidOperationException($"UnityRHI: compiled shader bytecode is too large ({size} bytes).");
                    result.Bytecode = new byte[(int)size];
                    Marshal.Copy(data, result.Bytecode, 0, result.Bytecode.Length);
                }
                return result;
            }
            finally
            {
                NativeMethods.UnityRhiShaderCompileDestroy(native);
            }
        }
    }
}
