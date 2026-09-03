using System;
using System.Runtime.InteropServices;
using UnityRhi.Nri;
using UnityRhi;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace UnityRhi.Dlsr
{
    /// <summary>
    /// NRI/NGX DLSS Super Resolution path reused by UnityRHI. This integration
    /// intentionally does not initialize Streamline or DLSS frame generation.
    /// </summary>
    public class DlsrUpscaler : IDisposable
    {
        [DllImport("NRIPlugin")]
        private static extern int CreateDLSRInstance();

        [DllImport("NRIPlugin")]
        private static extern void DestroyDLSRInstance(int id);

        private readonly int                        _instanceId;
        private          NativeArray<DlsrFrameData> _buffer;
        private const    int                        BufferCount = 3;
        private readonly string                     _cameraName;

        /// <summary>
        /// Per-frame camera data filled by the feature from CameraFrameState.
        /// </summary>
        public struct DlsrFrameInput
        {
            public float2 viewportJitter;
            public int2   renderResolution;
            public uint   frameIndex;
            public ushort outputWidth;
            public ushort outputHeight;
        }

        /// <summary>
        /// DLSS SR textures passed each frame.
        /// </summary>
        public struct DlsrResources
        {
            public NriTextureResource input; // low-res color
            public NriTextureResource output; // upscaled color (UAV)
            public NriTextureResource mv; // motion vectors
            public NriTextureResource depth; // depth
            public NriTextureResource exposure; // optional – set NriPtr to IntPtr.Zero if unused
            public NriTextureResource reactive; // optional – set NriPtr to IntPtr.Zero if unused
        }

        /// <summary>Per-dispatch settings.</summary>
        public struct DlsrSettings
        {
            public UpscalerMode upscalerMode;
            public byte         preset; // 0 = default
            public bool         resetHistory;
            public float        mvScaleX; // default 1
            public float        mvScaleY; // default 1
        }

        public DlsrUpscaler(string camName)
        {
            _instanceId = CreateDLSRInstance();
            _cameraName = camName;
            _buffer     = new NativeArray<DlsrFrameData>(BufferCount, Allocator.Persistent);
            Debug.Log($"[DLSR] Created Upscaler Instance {_instanceId} for Camera {_cameraName}");
        }

        private DlsrFrameData BuildFrameData(DlsrFrameInput fi, DlsrResources res, float resolutionScale, DlsrSettings settings)
        {
            ushort rectW = (ushort)(fi.renderResolution.x * resolutionScale + 0.5f);
            ushort rectH = (ushort)(fi.renderResolution.y * resolutionScale + 0.5f);

            return new DlsrFrameData
            {
                inputTex      = res.input.NriPtr,
                outputTex     = res.output.NriPtr,
                mvTex         = res.mv.NriPtr,
                depthTex      = res.depth.NriPtr,
                exposureTex   = res.exposure?.NriPtr ?? IntPtr.Zero,
                reactiveTex   = res.reactive?.NriPtr ?? IntPtr.Zero,
                outputWidth   = fi.outputWidth,
                outputHeight  = fi.outputHeight,
                currentWidth  = rectW,
                currentHeight = rectH,
                cameraJitter  = fi.viewportJitter,
                mvScale = new float2(settings.mvScaleX == 0 ? 1f : settings.mvScaleX,
                    settings.mvScaleY == 0 ? 1f : settings.mvScaleY),
                instanceId   = _instanceId,
                upscalerMode = settings.upscalerMode,
                preset       = settings.preset,
                resetHistory = (byte)(settings.resetHistory ? 1 : 0),
            };
        }

        public IntPtr GetInteropDataPtr(DlsrFrameInput fi, DlsrResources res, float resolutionScale, DlsrSettings settings)
        {
            var index = (int)(fi.frameIndex % BufferCount);
            _buffer[index] = BuildFrameData(fi, res, resolutionScale, settings);
            unsafe
            {
                return (IntPtr)_buffer.GetUnsafePtr() + index * sizeof(DlsrFrameData);
            }
        }

        public void Dispose()
        {
            if (_buffer.IsCreated)
                _buffer.Dispose();

            DestroyDLSRInstance(_instanceId);
            Debug.Log($"[DLSR] Destroyed Upscaler Instance {_instanceId} for Camera {_cameraName}");
        }
    }
}
