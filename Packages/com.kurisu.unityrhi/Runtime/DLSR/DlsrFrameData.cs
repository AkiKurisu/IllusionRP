using System;
using System.Runtime.InteropServices;
using UnityRhi;
using Unity.Mathematics;

namespace UnityRhi.Dlsr
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DlsrFrameData
    {
        public IntPtr inputTex;
        public IntPtr outputTex;
        public IntPtr mvTex;
        public IntPtr depthTex;
        public IntPtr exposureTex; // optional, IntPtr.Zero if unused
        public IntPtr reactiveTex; // optional, IntPtr.Zero if unused

        public ushort outputWidth;
        public ushort outputHeight;
        public ushort currentWidth;
        public ushort currentHeight;

        public float2 cameraJitter;
        public float2 mvScale;

        public int          instanceId;
        public UpscalerMode upscalerMode;
        public byte         preset; // 0 = default
        public byte         resetHistory; // non-zero = reset accumulation
    }
}