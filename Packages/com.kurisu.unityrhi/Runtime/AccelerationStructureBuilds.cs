using System;
using System.Runtime.InteropServices;
using System.Text;

namespace UnityRhi
{
    public static class AccelerationStructureBuilds
    {
        private const string Plugin = "GfxPluginIllusionRHI";

        [DllImport(Plugin)] private static extern void UnityRhiCaptureAccelerationStructureBuilds(int enabled);
        [DllImport(Plugin)] private static extern int UnityRhiReadAccelerationStructureBuilds(byte[] buffer, int capacity);
        [DllImport(Plugin)] private static extern ulong UnityRhiGetGpuVirtualAddress(IntPtr resource);

        public static void BeginCapture() => UnityRhiCaptureAccelerationStructureBuilds(1);

        public static string EndCapture()
        {
            int length = UnityRhiReadAccelerationStructureBuilds(null, 0);
            var buffer = new byte[length + 1];
            UnityRhiReadAccelerationStructureBuilds(buffer, buffer.Length);
            return Encoding.ASCII.GetString(buffer, 0, length);
        }

        public static ulong GpuVirtualAddress(IntPtr resource) => resource == IntPtr.Zero ? 0 : UnityRhiGetGpuVirtualAddress(resource);
    }
}
