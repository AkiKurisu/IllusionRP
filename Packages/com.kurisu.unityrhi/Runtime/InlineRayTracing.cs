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
            if (!RhiCore.IsD3D12Active || code == null || code.Length == 0) return;
            try
            {
                _eventId = UnityRhiInitializeInlineRayTracing();
                if (_eventId == 0) return;
                _callback = UnityRhiGetInlineRayTracingCallback();
                _handle = UnityRhiCreateInlineRayTracing(code, code.Length, entry, threadsX, threadsY, ser ? 1 : 0);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
#if UNITY_EDITOR
            RhiDomainReload.RegisterOwner(this);
#endif
        }

        public bool IsAvailable => _handle != IntPtr.Zero;

        public ulong GetDispatchCount(out int status)
        {
            status = 0;
            return IsAvailable ? UnityRhiGetInlineRayTracingDispatches(_handle, out status) : 0;
        }

        public void Bind(CommandBuffer commandBuffer)
        {
            if (IsAvailable)
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
