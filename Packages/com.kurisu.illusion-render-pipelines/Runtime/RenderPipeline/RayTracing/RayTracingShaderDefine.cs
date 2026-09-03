using UnityEngine;

namespace Illusion.Rendering.RayTracing
{
    public static class RayTracingShaderProperties
    {
        public static readonly int PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");

        public static readonly int CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");

        public static readonly int HistoryBuffer = Shader.PropertyToID("_HistoryBuffer");

        public static readonly int ValidationBuffer = Shader.PropertyToID("_ValidationBuffer");

        public static readonly int ValidationBufferRW = Shader.PropertyToID("_ValidationBufferRW");

        public static readonly int HistoryDepthTexture = Shader.PropertyToID("_HistoryDepthTexture");

        public static readonly int HistoryNormalTexture = Shader.PropertyToID("_HistoryNormalTexture");

        public static readonly int DepthTexture = Shader.PropertyToID("_DepthTexture");

        public static readonly int HistoryValidity = Shader.PropertyToID("_HistoryValidity");

        public static readonly int ReceiverMotionRejection = Shader.PropertyToID("_ReceiverMotionRejection");

        public static readonly int OccluderMotionRejection = Shader.PropertyToID("_OccluderMotionRejection");

        public static readonly int DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");

        public static readonly int DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");

        public static readonly int AccumulationOutputTextureRW = Shader.PropertyToID("_AccumulationOutputTextureRW");

        public static readonly int VelocityBuffer = Shader.PropertyToID("_VelocityBuffer");

        public static readonly int NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");

        public static readonly int StencilTexture = Shader.PropertyToID("_StencilTexture");

        public static readonly int HistorySizeAndScale = Shader.PropertyToID("_HistorySizeAndScale");

        public static readonly int DenoiseInputArrayTexture = Shader.PropertyToID("_DenoiseInputArrayTexture");

        public static readonly int ValidityInputArrayTexture = Shader.PropertyToID("_ValidityInputArrayTexture");

        public static readonly int IntermediateDenoiseOutputTexture = Shader.PropertyToID("_IntermediateDenoiseOutputTexture");

        public static readonly int IntermediateValidityOutputTexture = Shader.PropertyToID("_IntermediateValidityOutputTexture");

        public static readonly int IntermediateDenoiseOutputTextureRW = Shader.PropertyToID("_IntermediateDenoiseOutputTextureRW");

        public static readonly int IntermediateValidityOutputTextureRW = Shader.PropertyToID("_IntermediateValidityOutputTextureRW");

        public static readonly int DenoisingHistorySlice = Shader.PropertyToID("_DenoisingHistorySlice");

        public static readonly int DenoisingHistoryMask = Shader.PropertyToID("_DenoisingHistoryMask");

        public static readonly int DenoiseOutputArrayTextureRW = Shader.PropertyToID("_DenoiseOutputArrayTextureRW");

        public static readonly int HistoryValidityBuffer = Shader.PropertyToID("_HistoryValidityBuffer");

        public static readonly int ValidityOutputTextureRW = Shader.PropertyToID("_ValidityOutputTextureRW");

        public static readonly int CameraFOV = Shader.PropertyToID("_CameraFOV");

        public static readonly int DenoiserFilterRadius = Shader.PropertyToID("_DenoiserFilterRadius");

        public static readonly int DistanceTexture = Shader.PropertyToID("_DistanceTexture");

        public static readonly int RaytracingLightAngle = Shader.PropertyToID("_RaytracingLightAngle");
    }

    public enum RayTracingProfileIds
    {
        HistoryValidity,
        TemporalFilter,
        DiffuseFilter
    }
}