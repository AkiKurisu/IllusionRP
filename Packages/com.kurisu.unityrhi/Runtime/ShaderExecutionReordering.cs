using System;
using System.Runtime.InteropServices;

namespace UnityRhi
{
    public static class ShaderExecutionReordering
    {
        private static ulong _device;
        private static bool _supported;

        [DllImport("GfxPluginIllusionRHI", CallingConvention = CallingConvention.Cdecl)]
        private static extern int UnityRhiInitializeShaderExecutionReordering(IntPtr device);

        public static bool IsSupported
        {
            get
            {
                if (!RhiCore.IsD3D12Active) return false;
                ulong device = Device.Instance.GetNativeObject(NativeObjectType.D3D12_Device);
                if (device == 0) return false;
                if (_device == device) return _supported;
                _device = device;
                try
                {
                    _supported = UnityRhiInitializeShaderExecutionReordering(new IntPtr(unchecked((long)device))) != 0;
                }
                catch (DllNotFoundException) { _supported = false; }
                catch (EntryPointNotFoundException) { _supported = false; }
                return _supported;
            }
        }
    }
}
