using UnityEngine;
#if UNITY_EDITOR
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Illusion.Rendering.PRTGI
{
#if UNITY_EDITOR
    internal readonly struct PRTProbeBakeSamples
    {
        public readonly Vector3 capturePosition;
        public readonly PRTCaptureSample[] surfels;
        public PRTProbeBakeSamples(Vector3 position, PRTCaptureSample[] samples) { capturePosition = position; surfels = samples; }
    }
    internal readonly struct PRTProbePlacement
    {
        public readonly Vector3 offset;
        public readonly bool valid;
        public PRTProbePlacement(Vector3 offset, bool valid) { this.offset = offset; this.valid = valid; }
    }
    internal interface IPRTBaker
    {
        Bounds GeometryBounds { get; }
        Hash128 GeometrySignature { get; }
        Hash128 MaterialSignature { get; }
        string BackendName { get; }
        float SceneTime { get; }
        void UpdateProgress(string status, float progress);
        Task<PRTProbeBakeSamples[]> CaptureProbesAsync(Vector3[] capturePositions, Vector4[] directionAndIntegralWeights,
            CancellationToken cancellationToken);
        Task<PRTProbePlacement[]> PlaceProbesAsync(Vector3[] positions, Vector2[] geometryAndRayOriginBiases, float searchDistance,
            CancellationToken cancellationToken);
    }
#endif
}
