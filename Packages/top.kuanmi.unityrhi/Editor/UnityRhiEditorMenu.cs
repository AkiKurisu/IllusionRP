using UnityRhi;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityRhi.EditorTools
{
    public static class UnityRhiEditorMenu
    {
        [InitializeOnLoadMethod]
        private static void InitializeNativePlugin()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Debug.LogWarning(
                    "UnityRHI requires Direct3D12, but the editor is running "
                    + SystemInfo.graphicsDeviceType
                    + ". Set Player Settings > Other Settings > Graphics APIs for Windows to Direct3D12 and restart the editor.");
                return;
            }

            // Unity loads native plugins lazily, on the first P/Invoke. Touch the
            // DLL here so UnityPluginLoad (and the [UnityRHI] init logs) run as
            // soon as scripts are loaded rather than at first use.
            try
            {
                Debug.Log($"UnityRHI: native plugin loaded, apiVersion={RhiCore.NativeApiVersion}, d3d12Active={RhiCore.IsD3D12Active}");
            }
            catch (System.DllNotFoundException)
            {
                Debug.LogError("UnityRHI: UnityRHI.dll not found in Packages/top.kuanmi.unityrhi.native/Plugins/x86_64. Run build_dll.bat.");
            }
        }
    }

    /// <summary>
    /// Domain-reload hardening. The native plugin outlives every
    /// domain reload, but managed wrappers do not — any Resource that was not
    /// Disposed would only release its handle whenever its finalizer happens
    /// to run during domain teardown. Run finalizers deterministically before
    /// the reload, drain the deferred-release queue, and report anything that
    /// is still alive so leaks surface at the reload that caused them.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityRhiDomainReloadHooks
    {
        static UnityRhiDomainReloadHooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            if (!RhiCore.IsD3D12Active)
                return;

            // Unity runs this callback before some ScriptableObject owners
            // dispose, so their device objects are still strongly reachable
            // and cannot be collected below. Dispose registered RHI owners
            // first; whatever survives afterward is a genuine leak.
            RhiDomainReload.DisposeRegisteredOwners();

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();

            Device device = Device.Instance;
            device.RunGarbageCollection();

            var stats = device.GetStats();
            uint live = stats.LiveBuffers + stats.LiveTextures + stats.LiveSamplers + stats.LiveShaders
                        + stats.LiveBindingLayouts + stats.LiveBindingSets + stats.LiveComputePipelines;
            if (live > 0)
            {
                Debug.LogWarning($"UnityRHI: {live} resource(s) still alive entering domain reload (see report below).");
                device.ReportLiveResources();
            }
        }
    }
}
