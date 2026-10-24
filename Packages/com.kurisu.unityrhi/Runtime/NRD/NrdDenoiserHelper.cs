using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace UnityRhi.Nrd
{
    public static class NrdDenoiserHelper
    {
        public struct CommonFrameInput
        {
            public Matrix4x4 worldToView;
            public Matrix4x4 prevWorldToView;
            public Matrix4x4 viewToClip;
            public Matrix4x4 prevViewToClip;
            public float2    viewportJitter;
            public float2    prevViewportJitter;
            public float     resolutionScale;
            public float     prevResolutionScale;
            public int2      renderResolution;
            public uint      frameIndex;
            public bool      flipMotionVectors;
            public bool      enableValidation;
            public bool      isHistoryConfidenceAvailable;
            public float     splitScreen;
            public float     denoisingRange;
            public float     strandMaterialID;
        }

        public static unsafe void GetCommonSettings(ref CommonSettings commonSettings, NrdDenoiserHelper.CommonFrameInput fi)
        {
            commonSettings.viewToClipMatrix      = fi.viewToClip;
            commonSettings.viewToClipMatrixPrev  = fi.prevViewToClip;
            commonSettings.worldToViewMatrix     = fi.worldToView;
            commonSettings.worldToViewMatrixPrev = fi.prevWorldToView;

            commonSettings.cameraJitter     = fi.viewportJitter;
            commonSettings.cameraJitterPrev = fi.prevViewportJitter;

            ushort rectW     = (ushort)(fi.renderResolution.x * fi.resolutionScale + 0.5f);
            ushort rectH     = (ushort)(fi.renderResolution.y * fi.resolutionScale + 0.5f);
            ushort prevRectW = (ushort)(fi.renderResolution.x * fi.prevResolutionScale + 0.5f);
            ushort prevRectH = (ushort)(fi.renderResolution.y * fi.prevResolutionScale + 0.5f);

            commonSettings.resourceSize[0]     = (ushort)fi.renderResolution.x;
            commonSettings.resourceSize[1]     = (ushort)fi.renderResolution.y;
            commonSettings.rectSize[0]         = rectW;
            commonSettings.rectSize[1]         = rectH;
            commonSettings.resourceSizePrev[0] = (ushort)fi.renderResolution.x;
            commonSettings.resourceSizePrev[1] = (ushort)fi.renderResolution.y;
            commonSettings.rectSizePrev[0]     = prevRectW;
            commonSettings.rectSizePrev[1]     = prevRectH;

            commonSettings.motionVectorScale          = new float3(1.0f / rectW, 1.0f / rectH, fi.flipMotionVectors ? 1.0f : -1.0f);
            commonSettings.isMotionVectorInWorldSpace = false;
            commonSettings.accumulationMode           = AccumulationMode.CONTINUE;
            commonSettings.frameIndex                 = fi.frameIndex;

            commonSettings.denoisingRange                 = fi.denoisingRange;
            commonSettings.splitScreen                    = fi.splitScreen;
            commonSettings.strandMaterialID               = fi.strandMaterialID == 0f ? 999.0f : fi.strandMaterialID;
            commonSettings.enableValidation               = fi.enableValidation;
            commonSettings.disocclusionThresholdAlternate = 0.1f;
            commonSettings.isHistoryConfidenceAvailable   = fi.isHistoryConfidenceAvailable;
        }

        [DllImport("NRIPlugin")]
        public static extern int CreateDenoiserInstance(IntPtr denoisers, int count);

        [DllImport("NRIPlugin")]
        public static extern void DestroyDenoiserInstance(int id);

        [DllImport("NRIPlugin")]
        public static extern void UpdateDenoiserResources(int instanceId, IntPtr resources, int count);
    }
}