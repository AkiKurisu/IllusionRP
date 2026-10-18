using System;
using System.Runtime.InteropServices;

namespace UnityRhi.Interop
{
    /// <summary>
    /// P/Invoke surface of UnityRHI.dll. Event IDs mirror
    /// RenderingPlugin/Source/UnityRhiEvents.h.
    /// </summary>
    internal static class NativeMethods
    {
        public const string PluginName = "UnityRHI";

        public const int EventExecuteCommandStream = 2;
        public const int EventFlushAndSignalSyncPoint = 3;
        public const int EventBeginExternalHeapDispatch = 4;
        public const int EventEndExternalHeapDispatch = 5;

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiGetRenderEventAndDataFunc();

        [DllImport(PluginName)]
        public static extern int UnityRhiGetApiVersion();

        [DllImport(PluginName)]
        internal static extern int UnityRhiCreateDlrrInstance();

        [DllImport(PluginName)]
        internal static extern void UnityRhiDestroyDlrrInstance(int instanceId);

        [DllImport(PluginName)]
        internal static extern int UnityRhiCreateDlssNrInstance();

        [DllImport(PluginName)]
        internal static extern void UnityRhiDestroyDlssNrInstance(int instanceId);

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetDlssNrAvailable();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetDlssNrInitResult();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetDlssNrLastCreateResult();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetDlssNrLastEvaluateResult();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetNgxInitResult();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetNgxDlrrAvailable();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetNgxDlrrInitResult();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetNgxFrameGenerationAvailable();

        [DllImport(PluginName)]
        internal static extern int UnityRhiGetNgxFrameGenerationInitResult();

        [DllImport(PluginName)]
        internal static extern uint UnityRhiGetNgxFrameGenerationMultiFrameCountMax();

        [DllImport(PluginName)]
        internal static extern void UnityRhiSetFrameGenerationEnabled(int enabled);

        [DllImport(PluginName)]
        internal static extern void UnityRhiSubmitFrameGenerationInputs(IntPtr inputs);

        [DllImport(PluginName)]
        internal static extern ulong UnityRhiGetDisplayedPresentCount();

        [DllImport(PluginName)]
        public static extern int UnityRhiIsD3D12Active();

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetDeviceNativeObject(NativeObjectType objectType);

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetNativeObject(IntPtr handle, NativeObjectType objectType);

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetTextureNativeView(IntPtr handle, NativeObjectType objectType,
            Format format, ref TextureSubresourceSet subresources, TextureDimension dimension, uint isReadOnlyDSV);

        [DllImport(PluginName)]
        public static extern int UnityRhiGetTextureTiling(IntPtr texture, out uint numTiles,
            out PackedMipDesc packedMipDesc, out TileShape tileShape,
            ref uint subresourceTilingCount, [Out] SubresourceTiling[] subresourceTilings);

        [DllImport(PluginName)]
        public static extern int UnityRhiUpdateTextureTileMappings(IntPtr texture, IntPtr heap,
            TiledTextureCoordinate[] coordinates, TiledTextureRegion[] regions,
            ulong[] byteOffsets, uint regionCount);

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetCommandStreamEventCount();

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetDroppedCommandStreamCount();

        // ---- Command-stream decode (editor / debug) ----

        [DllImport(PluginName)]
        public static extern int UnityRhiDecodeCommandStream(IntPtr data, out CommandStreamInfoNative info);

        // ---- Device-removed / live-resource diagnostics ----

        [DllImport(PluginName)]
        public static extern int UnityRhiGetDeviceRemovedReason();

        [DllImport(PluginName)]
        public static extern void UnityRhiReportLiveResources();

        // ---- Device and resources ----

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateBuffer(
            ref RhiBufferDescNative desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateBufferFromNativeResource(
            IntPtr nativeResource, ref RhiBufferDescNative desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiGetBufferNativeResource(IntPtr handle);

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetBufferGpuVirtualAddress(IntPtr handle);

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetAccelStructGpuVirtualAddress(IntPtr handle);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateHeap(
            ref RhiHeapDescNative desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern void UnityRhiGetBufferMemoryRequirements(IntPtr buffer, out MemoryRequirements requirements);

        [DllImport(PluginName)]
        public static extern void UnityRhiGetTextureMemoryRequirements(IntPtr texture, out MemoryRequirements requirements);

        [DllImport(PluginName)]
        public static extern void UnityRhiGetAccelStructMemoryRequirements(IntPtr accelStruct, out MemoryRequirements requirements);

        [DllImport(PluginName)]
        public static extern int UnityRhiBindBufferMemory(IntPtr buffer, IntPtr heap, ulong offset);

        [DllImport(PluginName)]
        public static extern int UnityRhiBindTextureMemory(IntPtr texture, IntPtr heap, ulong offset);

        [DllImport(PluginName)]
        public static extern int UnityRhiBindAccelStructMemory(IntPtr accelStruct, IntPtr heap, ulong offset);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateTexture(
            ref RhiTextureDescNative desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateTextureFromNativeResource(
            IntPtr nativeResource, ref RhiTextureDescNative desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiGetTextureNativeResource(IntPtr handle);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateStagingTexture(
            ref RhiTextureDescNative desc, CpuAccessMode cpuAccess,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiMapStagingTexture(
            IntPtr handle, ref TextureSlice slice, CpuAccessMode cpuAccess, out ulong rowPitch);

        [DllImport(PluginName)]
        public static extern void UnityRhiUnmapStagingTexture(IntPtr handle);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateSampler(
            ref RhiSamplerDescNative desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern void UnityRhiReleaseResource(IntPtr handle);

        [DllImport(PluginName)]
        public static extern int UnityRhiRetainResources(IntPtr[] handles, uint count);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateCommandSubmission(
            IntPtr stream, uint byteSize,
            IntPtr[] resources, uint resourceCount,
            IntPtr[] uploadTickets, uint uploadTicketCount);

        [DllImport(PluginName)]
        public static extern void UnityRhiDestroyCommandSubmission(IntPtr submission);

        [DllImport(PluginName)]
        public static extern void UnityRhiGarbageCollect();

        [DllImport(PluginName)]
        public static extern void UnityRhiGetDeviceStats(out DeviceStats stats);

        [DllImport(PluginName)]
        public static extern uint UnityRhiGetResourceSnapshot(
            [Out] ResourceInfoNative[] resources, uint capacity);

        // ---- Shader compilation ----

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCompileShader(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string entryPoint,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string targetProfile,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string defines,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string includeDirs,
            uint flags);

        [DllImport(PluginName)]
        public static extern int UnityRhiShaderCompileGetSucceeded(IntPtr result);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiShaderCompileGetBytecode(IntPtr result, out ulong size);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiShaderCompileGetErrors(IntPtr result);

        [DllImport(PluginName)]
        public static extern void UnityRhiShaderCompileDestroy(IntPtr result);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateShader(
            uint shaderType,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string entryName,
            byte[] bytecode, ulong byteSize,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateShaderLibrary(
            byte[] bytecode, ulong byteSize,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateLibraryShader(
            IntPtr library, uint shaderType,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string entryName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateBindingLayout(
            ref RhiBindingLayoutDescNative desc,
            RhiBindingLayoutItemNative[] items, uint itemCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateBindlessLayout(
            ref RhiBindlessLayoutDescNative desc,
            RhiBindingLayoutItemNative[] registerSpaces, uint registerSpaceCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateBindingSet(
            IntPtr layout, RhiBindingSetItemNative[] items, uint itemCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateComputePipeline(
            IntPtr shader, IntPtr[] layouts, uint layoutCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateInputLayout(
            RhiVertexAttributeDescNative[] attributes, uint attributeCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateFramebuffer(
            IntPtr[] colorTextures, uint colorCount, IntPtr depthTexture,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateGraphicsPipeline(
            ref RhiGraphicsPipelineDescNative desc,
            IntPtr vertexShader, IntPtr pixelShader, IntPtr inputLayout,
            IntPtr[] layouts, uint layoutCount,
            IntPtr framebuffer,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateDescriptorTable(
            IntPtr layout,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern void UnityRhiResizeDescriptorTable(IntPtr descriptorTable, uint newSize, uint keepContents);

        [DllImport(PluginName)]
        public static extern int UnityRhiWriteDescriptorTable(IntPtr descriptorTable, ref RhiBindingSetItemNative binding);

        [DllImport(PluginName)]
        public static extern uint UnityRhiGetDescriptorTableFirstDescriptorIndexInHeap(IntPtr descriptorTable);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateAccelStruct(
            ref RhiRtAccelStructDescNative desc,
            RhiRtGeometryDescNative[] geometries, uint geometryCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateRayTracingPipeline(
            ref RhiRtPipelineDescNative desc,
            RhiRtPipelineShaderDescNative[] shaders, uint shaderCount,
            RhiRtPipelineHitGroupDescNative[] hitGroups, uint hitGroupCount,
            IntPtr[] globalLayouts, uint globalLayoutCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiCreateShaderTable(
            IntPtr pipeline, ref RhiRtShaderTableDescNative desc,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string debugName);

        [DllImport(PluginName)]
        public static extern void UnityRhiShaderTableSetRayGenerationShader(
            IntPtr shaderTable, [MarshalAs(UnmanagedType.LPUTF8Str)] string exportName, IntPtr bindings);

        [DllImport(PluginName)]
        public static extern int UnityRhiShaderTableAddMissShader(
            IntPtr shaderTable, [MarshalAs(UnmanagedType.LPUTF8Str)] string exportName, IntPtr bindings);

        [DllImport(PluginName)]
        public static extern int UnityRhiShaderTableAddHitGroup(
            IntPtr shaderTable, [MarshalAs(UnmanagedType.LPUTF8Str)] string exportName, IntPtr bindings);

        [DllImport(PluginName)]
        public static extern int UnityRhiShaderTableAddCallableShader(
            IntPtr shaderTable, [MarshalAs(UnmanagedType.LPUTF8Str)] string exportName, IntPtr bindings);

        [DllImport(PluginName)]
        public static extern void UnityRhiShaderTableClearMissShaders(IntPtr shaderTable);

        [DllImport(PluginName)]
        public static extern void UnityRhiShaderTableClearHitShaders(IntPtr shaderTable);

        [DllImport(PluginName)]
        public static extern void UnityRhiShaderTableClearCallableShaders(IntPtr shaderTable);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiMapBuffer(IntPtr buffer);

        [DllImport(PluginName)]
        public static extern void UnityRhiUnmapBuffer(IntPtr buffer);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiStageBufferUpload(IntPtr data, ulong byteSize);

        [DllImport(PluginName)]
        public static extern IntPtr UnityRhiStageTextureUpload(
            IntPtr texture, uint arraySlice, uint mipLevel, IntPtr data, ulong dataSize,
            ulong rowPitch, ulong depthPitch);

        [DllImport(PluginName)]
        public static extern void UnityRhiReleaseUploadTicket(IntPtr ticket);

        [DllImport(PluginName)]
        public static extern ulong UnityRhiGetUploadTicketSize(IntPtr ticket);

        [DllImport(PluginName)]
        public static extern int UnityRhiQueryFeatureSupport(uint feature);

        [DllImport(PluginName)]
        public static extern uint UnityRhiQueryFormatSupport(uint format);

        [DllImport(PluginName)] public static extern IntPtr UnityRhiCreateEventQuery();
        [DllImport(PluginName)] public static extern void UnityRhiSetEventQuery(IntPtr query);
        [DllImport(PluginName)] public static extern int UnityRhiPollEventQuery(IntPtr query);
        [DllImport(PluginName)] public static extern void UnityRhiWaitEventQuery(IntPtr query);
        [DllImport(PluginName)] public static extern void UnityRhiResetEventQuery(IntPtr query);

        [DllImport(PluginName)] public static extern IntPtr UnityRhiCreateTimerQuery();
        [DllImport(PluginName)] public static extern int UnityRhiPollTimerQuery(IntPtr query);
        [DllImport(PluginName)] public static extern float UnityRhiGetTimerQueryTime(IntPtr query);
        [DllImport(PluginName)] public static extern void UnityRhiResetTimerQuery(IntPtr query);

        // ---- GPU sync points ----

        [DllImport(PluginName)]
        public static extern ulong UnityRhiCreateSyncPoint();

        [DllImport(PluginName)]
        public static extern int UnityRhiWaitSyncPoint(ulong value, uint timeoutMs);
    }
}
