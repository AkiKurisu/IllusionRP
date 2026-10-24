using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing.UnityRHI
{
    internal sealed class UnityRHIPathTracingOpacityMicromaps : IPathTracingOpacityMicromapBackend
    {
        private const string Plugin = "GfxPluginIllusionRHI";

        [DllImport(Plugin)] private static extern int UnityRhiOpacityMicromapsSupported();
        [DllImport(Plugin)] private static extern int UnityRhiGetOpacityMicromapEvent();
        [DllImport(Plugin)] private static extern IntPtr UnityRhiGetOpacityMicromapCallback();
        [DllImport(Plugin)] private static extern ulong UnityRhiGetGpuVirtualAddress(IntPtr resource);
        [DllImport(Plugin)] private static extern IntPtr UnityRhiCreateOpacityMicromapArray(IntPtr data, IntPtr descs, uint count, uint level, uint format);
        [DllImport(Plugin)] private static extern void UnityRhiReleaseOpacityMicromapArray(IntPtr array);
        [DllImport(Plugin)] private static extern void UnityRhiAttachOpacityMicromap(ulong indexAddress, uint indexCount, IntPtr array, IntPtr indices);
        [DllImport(Plugin)] private static extern void UnityRhiDetachOpacityMicromap(ulong indexAddress, uint indexCount);

        private readonly IntPtr _callback;
        private readonly int _event;

        public UnityRHIPathTracingOpacityMicromaps()
        {
            try
            {
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12 || UnityRhiOpacityMicromapsSupported() == 0)
                    return;
                _event = UnityRhiGetOpacityMicromapEvent();
                _callback = UnityRhiGetOpacityMicromapCallback();
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                _event = 0;
            }
        }

        public bool IsSupported => _event != 0 && _callback != IntPtr.Zero;

        public ulong GpuAddress(IntPtr resource) => resource == IntPtr.Zero ? 0 : UnityRhiGetGpuVirtualAddress(resource);

        public IntPtr CreateArray(IntPtr data, IntPtr descs, uint count, uint level, uint format) =>
            UnityRhiCreateOpacityMicromapArray(data, descs, count, level, format);

        public void ReleaseArray(IntPtr array) => UnityRhiReleaseOpacityMicromapArray(array);

        public void Attach(ulong indexAddress, uint indexCount, IntPtr array, IntPtr indices) =>
            UnityRhiAttachOpacityMicromap(indexAddress, indexCount, array, indices);

        public void Detach(ulong indexAddress, uint indexCount) => UnityRhiDetachOpacityMicromap(indexAddress, indexCount);

        public void Issue(CommandBuffer cmd) => cmd.IssuePluginEvent(_callback, _event);
    }
}
