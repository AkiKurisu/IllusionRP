using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public enum ProbeVolumeDebugMode { None, ProbeGrid, ProbeGridWithVirtualOffset, ProbeRadiance, ShadowCache }
    public enum ProbeDebugMode { IrradianceSphere, SphereDistribution, SampleDirection, Surfel, SurfelBrickGrid }
#if UNITY_EDITOR
    internal enum ShadowCacheDebugStatus : uint
    {
        Unknown, FreshHit, Sampled, FallbackFromCache, UncoveredNoCache
    }
    [ExecuteAlways]
#endif
    public partial class PRTProbeVolume : MonoBehaviour
    {
        public readonly struct Grid
        {
            public readonly int X, Y, Z;
            public readonly float Size;
            public Grid(int x, int y, int z, float size) { X = x; Y = y; Z = z; Size = size; }
            public bool Equals(Grid other) => X == other.X && Y == other.Y && Z == other.Z && Size.Equals(other.Size);
            public override int GetHashCode() => HashCode.Combine(X, Y, Z, Size);
        }

        [Range(1, 128)] public int probeSizeX = 8;
        [Range(1, 64)] public int probeSizeY = 4;
        [Range(1, 128)] public int probeSizeZ = 8;
        [Range(0.1f, 100f)] public float probeGridSize = 2;
        public bool enableBakePreprocess = true;
        public Vector3 virtualOffset;
        [Range(0, 1)] public float geometryBias = 0.1f;
        [Range(0, 1)] public float rayOriginBias = 0.1f;
        [Min(1)] public int sectorWidth = 4;
        public bool enableRelightShadow = true;
        [Min(1)] public int shadowCacheMaxAge = 30;
        public bool enableShadowCacheStats;
        [Min(1)] public int shadowCacheStatsReadbackInterval = 10;
        [Min(1)] public int sectorsPerFrame = 2;
        [Min(1)] public int sectorBudgetMiB = 256;
        [Min(1)] public int uploadBudgetMiB = 4;
        public Vector3Int voxelProbeSize = new(10, 5, 10);
        [HideInInspector] public PRTProbeVolumeAsset asset;

        private RenderTexture _coefficientVoxelRT, _validityVoxelRT;
        private uint[] _validity;
        private PRTProbeMetadata[] _allProbes;
        private bool _isDataInitialized;
        private Camera _mainCamera;
        private Bounds _currentBoundingBox;
        private Vector3Int _boundingBoxMin;
        private readonly List<PRTProbe> _probesInBoundingBox = new();

        public RenderTexture CoefficientVoxel3D => _coefficientVoxelRT;
        public RenderTexture ValidityVoxel3D => _validityVoxelRT;
        public Vector3Int BoundingBoxMin => _boundingBoxMin;
        public Grid CurrentVoxelGrid { get; private set; }
        public int ProbeCountInWindow => _probesInBoundingBox.Count;
        public PRTProbe[] Probes { get; private set; }
        internal static bool IsFeatureEnabled { get; private set; }
        public int RuntimeDataId { get; private set; }
        public int MetadataHash { get; private set; }
        public string AssetInvalidReason { get; private set; }
        public int UpdatedSectors { get; private set; }
        public int ResidentSectors { get; private set; }
        public int UploadingSectors { get; private set; }
        public int EvictingSectors { get; private set; }
        public long ResidentBytes { get; private set; }
        public long PeakResidentBytes { get; private set; }
        public long FixedGpuBytes { get; private set; }
        public int FrameUploadBytes { get; private set; }
        public string ResidencyPressure { get; private set; }
        public uint[] SectorUpdateFrames { get; private set; }
        public int ShadowPreviewSector { get; internal set; }
        public float SolverResidual { get; private set; }
        public float SolverAbsoluteResidual { get; private set; }
        public int SolverNonFiniteCount { get; private set; }
        public uint PublishedGeneration { get; private set; }
        public string ShadowPreviewLightName { get; internal set; }
        public int ShadowPreviewLightId { get; internal set; }

        internal PRTProbeGrid Layout => new()
        {
            origin = transform.position, min = Vector3Int.zero,
            count = new Vector3Int(probeSizeX, probeSizeY, probeSizeZ), spacing = probeGridSize
        };
        public uint[] GetValidityMasks() => _validity;

        internal void ObserveSolver(PRTRelightSolver solver)
        {
            UpdatedSectors = solver.Scheduler.UpdatedCount;
            ResidentSectors = solver.Residency.Count;
            UploadingSectors = EvictingSectors = 0;
            foreach (var sector in solver.Residency.Residents)
            {
                if (sector.Evicting) EvictingSectors++;
                else if (!sector.Ready) UploadingSectors++;
            }
            ResidentBytes = solver.Residency.Bytes;
            PeakResidentBytes = solver.Residency.PeakBytes;
            FixedGpuBytes = solver.FixedBytes;
            FrameUploadBytes = solver.Residency.UploadedBytes;
            ResidencyPressure = solver.Residency.Pressure;
            SectorUpdateFrames = solver.SectorFrames;
            SolverResidual = solver.Residual;
            SolverAbsoluteResidual = solver.AbsoluteResidual;
            SolverNonFiniteCount = solver.NonFiniteCount;
        }
    }
}
