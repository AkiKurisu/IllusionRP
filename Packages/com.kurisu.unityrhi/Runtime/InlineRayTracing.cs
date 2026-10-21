using System;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace UnityRhi
{
    public sealed class InlineRayTracing : IDisposable
    {
        private const string Plugin = "GfxPluginIllusionRHI";
        private IntPtr _handle;
        private readonly IntPtr _callback;
        private readonly int _eventId;

        [DllImport(Plugin)] private static extern int UnityRhiInitializeInlineRayTracing();
        [DllImport(Plugin)] private static extern IntPtr UnityRhiGetInlineRayTracingCallback();
        [DllImport(Plugin, CharSet = CharSet.Unicode)]
        private static extern IntPtr UnityRhiCreateInlineRayTracing(byte[] code, int size, string entry, uint x, uint y, int ser);
        [DllImport(Plugin)] private static extern void UnityRhiReleaseInlineRayTracing(IntPtr handle);
        [DllImport(Plugin)] private static extern ulong UnityRhiGetInlineRayTracingDispatches(IntPtr handle, out int status);

        public InlineRayTracing(byte[] code, string entry, uint threadsX, uint threadsY, bool ser)
        {
            if (!RhiCore.IsD3D12Active) throw new NotSupportedException("Inline RayGen requires D3D12.");
            if (code == null || code.Length == 0) throw new ArgumentException("Inline RayGen bytecode is missing.", nameof(code));
            _eventId = UnityRhiInitializeInlineRayTracing();
            if (_eventId == 0) throw new InvalidOperationException("Inline RayGen device or hook initialization failed.");
            _callback = UnityRhiGetInlineRayTracingCallback();
            if (_callback == IntPtr.Zero) throw new InvalidOperationException("Inline RayGen callback is missing.");
            _handle = UnityRhiCreateInlineRayTracing(code, code.Length, entry, threadsX, threadsY, ser ? 1 : 0);
            if (_handle == IntPtr.Zero) throw new InvalidOperationException($"Inline RayGen '{entry}' creation failed (SER={ser}).");
#if UNITY_EDITOR
            RhiDomainReload.RegisterOwner(this);
#endif
        }

        public bool IsAvailable => _handle != IntPtr.Zero;

        public ulong GetDispatchCount(out int status)
        {
            status = unchecked((int)0x80070006);
            return IsAvailable ? UnityRhiGetInlineRayTracingDispatches(_handle, out status) : 0;
        }

        public void Bind(CommandBuffer commandBuffer)
        {
            if (!IsAvailable) throw new ObjectDisposedException(nameof(InlineRayTracing));
            GetDispatchCount(out int status);
            if (status < 0) throw new InvalidOperationException($"Inline RayGen dispatch failed (0x{status:X8}).");
            commandBuffer.IssuePluginEventAndData(_callback, _eventId, _handle);
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            UnityRhiReleaseInlineRayTracing(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
