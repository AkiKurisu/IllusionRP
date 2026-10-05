using UnityEngine;
#if UNITY_EDITOR
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Illusion.Rendering.PRTGI
{
    public enum PRTBakeResolution
    {
        [InspectorName("128 × 128")] _128 = 128,
        [InspectorName("256 × 256")] _256 = 256,
        [InspectorName("512 × 512")] _512 = 512
    }
#if UNITY_EDITOR
    internal readonly struct PRTProbeBakeSamples
    {
        public readonly Vector3 capturePosition;
        public readonly Surfel[] surfels;
        public PRTProbeBakeSamples(Vector3 position, Surfel[] samples) { capturePosition = position; surfels = samples; }
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
        PRTProbePlacement PlaceProbe(Vector3 position, float geometryBias, float rayOriginBias, float searchDistance);
    }
#endif
}
