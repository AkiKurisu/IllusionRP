using System;
using UnityRhi.Interop;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityRhi
{
    /// <summary>Entry point to the native RHI plugin.</summary>
    public static class RhiCore
    {
        private static IntPtr s_RenderEventFunc;

        /// <summary>True once the native plugin has initialized on a D3D12 device.</summary>
        public static bool IsD3D12Active => NativeMethods.UnityRhiIsD3D12Active() != 0;

        public static int NativeApiVersion => NativeMethods.UnityRhiGetApiVersion();

        /// <summary>Raw NVSDK_NGX_Result returned by direct D3D12 initialization.</summary>
        public static int NgxInitResult => NativeMethods.UnityRhiGetNgxInitResult();

        /// <summary>True when the installed GPU/driver exposes direct NGX Ray Reconstruction.</summary>
        public static bool IsNgxDlrrAvailable => NativeMethods.UnityRhiGetNgxDlrrAvailable() != 0;

        /// <summary>Raw NGX feature-initialization result for Ray Reconstruction.</summary>
        public static int NgxDlrrInitResult => NativeMethods.UnityRhiGetNgxDlrrInitResult();

        /// <summary>True when the signed nvngx_dlssnr snippet loaded and initialized.</summary>
        public static bool IsDlssNrAvailable => NativeMethods.UnityRhiGetDlssNrAvailable() != 0;

        /// <summary>Raw result returned by the standalone DLSSNR snippet initialization.</summary>
        public static int DlssNrInitResult => NativeMethods.UnityRhiGetDlssNrInitResult();

        public static int DlssNrLastCreateResult =>
            NativeMethods.UnityRhiGetDlssNrLastCreateResult();

        public static int DlssNrLastEvaluateResult =>
            NativeMethods.UnityRhiGetDlssNrLastEvaluateResult();

        /// <summary>True when direct NGX Frame Generation is available on this GPU/driver.</summary>
        public static bool IsNgxFrameGenerationAvailable =>
            NativeMethods.UnityRhiGetNgxFrameGenerationAvailable() != 0;

        /// <summary>Raw NGX feature-initialization result for Frame Generation.</summary>
        public static int NgxFrameGenerationInitResult =>
            NativeMethods.UnityRhiGetNgxFrameGenerationInitResult();

        /// <summary>Maximum interpolated frames supported per NGX FG evaluation (1 = 2x only).</summary>
        public static uint NgxFrameGenerationMultiFrameCountMax =>
            NativeMethods.UnityRhiGetNgxFrameGenerationMultiFrameCountMax();

        /// <summary>Player-only: evaluate DLSS-G and insert generated frames on Present.</summary>
        public static void SetFrameGenerationEnabled(bool enabled) =>
            NativeMethods.UnityRhiSetFrameGenerationEnabled(enabled ? 1 : 0);

        /// <summary>
        /// Synchronously copies a native ABI packet. The caller may release its
        /// stack memory immediately; native code retains the two D3D12 resources.
        /// </summary>
        public static void SubmitFrameGenerationInputs(IntPtr inputs) =>
            NativeMethods.UnityRhiSubmitFrameGenerationInputs(inputs);

        /// <summary>
        /// DXGI Present count observed by the plugin (generated + real frames).
        /// Use this for displayed FPS; Unity's Time.deltaTime only counts rendered frames.
        /// </summary>
        public static ulong DisplayedPresentCount =>
            NativeMethods.UnityRhiGetDisplayedPresentCount();

        /// <summary>Number of command streams successfully replayed on the render thread.</summary>
        public static ulong CommandStreamEventCount => NativeMethods.UnityRhiGetCommandStreamEventCount();

        /// <summary>
        /// Number of command streams that were dropped instead of replayed
        /// (CommandRecordingState unavailable, or the stream failed to decode).
        /// A growing value means submitted GPU work silently never ran.
        /// </summary>
        public static ulong DroppedCommandStreamCount => NativeMethods.UnityRhiGetDroppedCommandStreamCount();

        /// <summary>
        /// 0 while the D3D12 device is healthy; otherwise the DXGI device-removed
        /// HRESULT. The first failing query also logs the removal reason and the
        /// native breadcrumb trail (last replayed markers/dispatches).
        /// </summary>
        public static int DeviceRemovedReason => NativeMethods.UnityRhiGetDeviceRemovedReason();

        internal static IntPtr RenderEventFunc
        {
            get
            {
                if (s_RenderEventFunc == IntPtr.Zero)
                    s_RenderEventFunc = NativeMethods.UnityRhiGetRenderEventAndDataFunc();
                return s_RenderEventFunc;
            }
        }

        internal static void IssueCommandStream(CommandBuffer commandBuffer, IntPtr submission)
        {
            commandBuffer.IssuePluginEventAndData(RenderEventFunc, NativeMethods.EventExecuteCommandStream, submission);
        }

        // Commands replayed through plugin events are invisible to Unity's
        // D3D12 backend. Once a render request has run in the session (e.g. a
        // render-feature test), Unity stops submitting command lists that
        // contain only plugin events — even at a FlushCommandBuffers event —
        // so replayed streams strand forever in an open command list while
        // the sync-point fence is still signaled directly on the queue.
        // SignalSyncPoint therefore records a 4-byte marker copy through
        // Unity's own API into the same command list, and WaitSyncPoint reads
        // the marker back: the synchronous GetData forces Unity to submit the
        // list containing the copy (and every plugin command recorded before
        // it) and to wait for the GPU to finish it.
        private static GraphicsBuffer s_FlushMarkerSrc;
        private static GraphicsBuffer s_FlushMarkerDst;
        private static readonly float[] s_FlushMarkerData = new float[1];

        /// <summary>
        /// Records a GPU sync point after the work already in the command buffer.
        /// When the event executes, Unity flushes its pending command lists and the
        /// plugin signals its sync-point fence on Unity's graphics queue, so the
        /// fence passes only after all preceding work has completed on the GPU.
        /// Block on it with <see cref="WaitSyncPoint"/>.
        /// </summary>
        public static ulong SignalSyncPoint(CommandBuffer commandBuffer)
        {
            ulong value = NativeMethods.UnityRhiCreateSyncPoint();
            if (s_FlushMarkerSrc == null || !s_FlushMarkerSrc.IsValid())
            {
                s_FlushMarkerSrc = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, 1, sizeof(float));
                s_FlushMarkerDst = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination, 1, sizeof(float));
            }
            commandBuffer.CopyBuffer(s_FlushMarkerSrc, s_FlushMarkerDst);
            commandBuffer.IssuePluginEventAndData(RenderEventFunc,
                NativeMethods.EventFlushAndSignalSyncPoint, unchecked((IntPtr)(long)value));
            return value;
        }

        /// <summary>
        /// Blocks the calling thread until the GPU passes the sync point (or the
        /// timeout expires). Graphics.WaitOnAsyncGraphicsFence cannot be used for
        /// this — it is a GPU-side wait and never blocks the CPU. While waiting,
        /// GL.Flush() kicks the render thread each iteration: with a threaded
        /// graphics device, Graphics.ExecuteCommandBuffer only queues the work,
        /// and a plain blocking wait could stall before the queue is processed.
        /// </summary>
        public static bool WaitSyncPoint(ulong syncPoint, uint timeoutMs = 10000)
        {
            // Force submission of any command list Unity is still sitting on
            // (see s_FlushMarkerSrc). The marker copy recorded by
            // SignalSyncPoint gives the destination buffer a pending GPU
            // write, so this synchronous readback cannot be short-circuited.
            if (s_FlushMarkerDst != null && s_FlushMarkerDst.IsValid())
                s_FlushMarkerDst.GetData(s_FlushMarkerData);

            int deadline = Environment.TickCount + (int)timeoutMs;
            for (;;)
            {
                if (NativeMethods.UnityRhiWaitSyncPoint(syncPoint, 10) != 0)
                    return true;
                if (Environment.TickCount - deadline >= 0)
                    return false;
                GL.Flush();
            }
        }

        /// <summary>
        /// Submits all graphics work recorded before this call and blocks until it has
        /// completed. Use this before releasing wrappers around Unity-owned native
        /// resources: their plugin lifetime marker only covers plugin commands and
        /// cannot account for later Unity commands that reference the same resource.
        /// </summary>
        public static bool WaitForGpuIdle(uint timeoutMs = 10000)
        {
            if (!IsD3D12Active)
                return true;

            var commandBuffer = new CommandBuffer { name = "UnityRHI.WaitForGpuIdle" };
            try
            {
                ulong syncPoint = SignalSyncPoint(commandBuffer);
                Graphics.ExecuteCommandBuffer(commandBuffer);
                return WaitSyncPoint(syncPoint, timeoutMs);
            }
            finally
            {
                commandBuffer.Release();
            }
        }

    }
}
