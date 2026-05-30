using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;

namespace Illusion.Rendering.PRTGI
{
    public enum ProbeVolumeDebugMode
    {
        None = 0,
        ProbeGrid = 1,
        ProbeGridWithVirtualOffset = 2,
        ProbeRadiance = 3,
        ShadowCache = 4
    }

    public enum ProbeDebugMode
    {
        IrradianceSphere = 0,
        SphereDistribution = 1,
        SampleDirection = 2,
        Surfel = 3,
        SurfelBrickGrid = 4
    }

#if UNITY_EDITOR
    internal enum ShadowCacheDebugStatus : uint
    {
        Unknown = 0,
        FreshHit = 1,
        Sampled = 2,
        FallbackFromCache = 3,
        UncoveredNoCache = 4,
        BrickCacheHit = 5
    }
#endif

#if UNITY_EDITOR
    [ExecuteAlways]
#endif
    public partial class PRTProbeVolume : MonoBehaviour
    {
        public readonly struct Grid
        {
            public readonly int X;

            public readonly int Y;

            public readonly int Z;

            public readonly float Size;

            public Grid(int x, int y, int z, float size)
            {
                X = x;
                Y = y;
                Z = z;
                Size = size;
            }

            public bool Equals(Grid other)
            {
                return X == other.X && Y == other.Y && Z == other.Z && Size.Equals(other.Size);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(X, Y, Z, Size);
            }
        }

        // Grid Settings
        [Range(1, 128)]
        public int probeSizeX = 8;

        [Range(1, 64)]
        public int probeSizeY = 4;

        [Range(1, 128)]
        public int probeSizeZ = 8;

        [Range(0.1f, 100f)]
        public float probeGridSize = 2.0f;

        // Probe Placement
        /// <summary>
        /// Enable bake preprocess for per-probe place adjustment
        /// </summary>
        public bool enableBakePreprocess = true;

        /// <summary>
        /// Set volume offset when sampling surfels at bake time
        /// </summary>
        public Vector3 virtualOffset;

        /// <summary>
        /// How far to push a probe's capture point out of geometry
        /// </summary>
        [Range(0f, 1f)]
        public float geometryBias = 0.1f;

        /// <summary>
        /// Distance between a probe's center and the point URP uses for sampling ray origin
        /// </summary>
        [Range(0f, 1f)]
        public float rayOriginBias = 0.1f;

        /// <summary>
        /// Enable multi frame relight to improve performance
        /// </summary>
        public bool multiFrameRelight;

        /// <summary>
        /// Enable shadow calculation in PRT relight
        /// </summary>
        public bool enableRelightShadow = true;

        /// <summary>
        /// Maximum frame age for cached PRT relight shadows.
        /// </summary>
        [Min(1)]
        public int shadowCacheMaxAge = 30;

        /// <summary>
        /// Brick shadow variance threshold below which the brick cache can be reused.
        /// </summary>
        [Range(0f, 1f)]
        public float shadowCacheVarianceThreshold = 0.02f;

        /// <summary>
        /// Read back PRT shadow cache counters for profiling and debugging.
        /// </summary>
        public bool enableShadowCacheStats;

        /// <summary>
        /// Frames between full shadow cache snapshot readbacks.
        /// </summary>
        [Min(1)]
        public int shadowCacheStatsReadbackInterval = 10;

        /// <summary>
        /// Number of probes to update per frame
        /// </summary>
        [Range(1, 100)]
        public int probesPerFrameUpdate = 2;

        // Camera-based local update
        /// <summary>
        /// Number of camera nearby probes to relight in additional to per frame update roulette
        /// </summary>
        [Range(3, 9)]
        public int localProbeCount = 6;

        /// <summary>
        /// Stop relighting after every probe in the current window has been updated once.
        /// Relight starts again when lighting, shadow, ambient, asset, volume transform, or window inputs change.
        /// </summary>
        public bool relightOnceUntilDirty = true;

        // Voxel texture const probe size
        public Vector3Int voxelProbeSize = new(10, 5, 10);

        [HideInInspector]
        public PRTProbeVolumeAsset asset;

        private const RenderTextureFormat Texture3DFormat = RenderTextureFormat.RGB111110Float;

        private Grid _probeGrid;

        // Layout: [probeSizeX, probeSizeZ, probeSizeY * 9]
        private RenderTexture _coefficientVoxelRT;

        // Validity texture to store per-probe invalidation masks
        // Layout: [probeSizeX, probeSizeZ, probeSizeY]
        // Each texel stores packed data: intensity (24 bits) + validity mask (8 bits)
        private RenderTexture _validityVoxelRT;

        // Validity texture stores packed data: intensity (24 bits) + validity mask (8 bits)
        private float[] _validity;

        private SurfelIndices[] _allBricks;

        private BrickFactor[] _allFactors;

        private FactorIndices[] _allProbes;

        private ComputeBuffer _globalSurfelBuffer;

        private bool _isDataInitialized;

        private bool _hasStarted;

        // Probe update rotation
        private int _currentProbeUpdateIndex;

        private int _probesToUpdateCount;

        private int _lastRoundRobinProbeCount;

        private Camera _mainCamera;

        private readonly List<int> _localProbeIndices = new();

        private Vector3 _lastCameraPosition;

        private const float CameraMovementThreshold = 1.0f; // Only recalculate when camera moves more than this distance

        // Bounding box related fields
        private Bounds _currentBoundingBox;

        private Vector3Int _boundingBoxMin; // Bounding box minimum corner (grid coordinates)

        private Vector3Int _originalBoxMin = new(-1, -1, -1);

        private bool _boundingBoxChanged;

        private int _lastBoundingBoxHash = -1;

        private readonly List<PRTProbe> _probesInBoundingBox = new();

        // Priority queue for high-priority probe updates (new probes, local probes)
        private readonly Queue<PRTProbe> _priorityProbeQueue = new();

        private readonly HashSet<int> _relitProbeIndicesInWindow = new();

        private bool _relightWindowCycleComplete;

        private int _lastRelightInputHash;

        private bool _hasRelightInputHash;

        private bool ShouldStopRelightAfterWindowCycle => relightOnceUntilDirty;

        /// <summary>
        /// 3D Texture to store SH coefficients
        /// </summary>
        public RenderTexture CoefficientVoxel3D => _coefficientVoxelRT;

        /// <summary>
        /// 3D Texture to store probe validity masks
        /// </summary>
        public RenderTexture ValidityVoxel3D => _validityVoxelRT;

        /// <summary>
        /// Bounding box minimum corner in grid coordinates
        /// </summary>
        public Vector3Int BoundingBoxMin => _boundingBoxMin;

        /// <summary>
        /// 3D Texture toroidal addressing origin
        /// </summary>
        public Vector3Int OriginalBoxMin => _originalBoxMin;

        /// <summary>
        /// Current grid dimensions for voxel
        /// </summary>
        public Grid CurrentVoxelGrid { get; private set; }

        public bool RelightWindowCycleComplete => _relightWindowCycleComplete;

        public int RelitProbeCountInWindow => _relitProbeIndicesInWindow.Count;

        public int ProbeCountInWindow => _probesInBoundingBox.Count;

        public int CurrentProbeUpdateIndex => _currentProbeUpdateIndex;

        public bool HasRelightInputHash => _hasRelightInputHash;

        public int LastRelightInputHash => _lastRelightInputHash;

        /// <summary>
        /// Get global surfel buffer
        /// </summary>
        public ComputeBuffer GlobalSurfelBuffer => _globalSurfelBuffer;

        public PRTProbe[] Probes { get; private set; }

        /// <summary>
        /// Is PRTGI feature activated in the renderer.
        /// </summary>
        internal static bool IsFeatureEnabled { get; private set; }

        public readonly struct ShadowCacheStats
        {
            public readonly bool valid;

            public readonly uint evaluated;

            public readonly uint cacheHits;

            public readonly uint cacheMisses;

            public readonly uint invalidByEpoch;

            public readonly uint invalidByAge;

            public readonly uint shadowmapSamples;

            public readonly uint fallbackFromCache;

            public readonly uint uncoveredNoCache;

            public ShadowCacheStats(uint evaluated, uint cacheHits, uint cacheMisses,
                uint invalidByEpoch, uint invalidByAge, uint shadowmapSamples,
                uint fallbackFromCache, uint uncoveredNoCache)
            {
                valid = true;
                this.evaluated = evaluated;
                this.cacheHits = cacheHits;
                this.cacheMisses = cacheMisses;
                this.invalidByEpoch = invalidByEpoch;
                this.invalidByAge = invalidByAge;
                this.shadowmapSamples = shadowmapSamples;
                this.fallbackFromCache = fallbackFromCache;
                this.uncoveredNoCache = uncoveredNoCache;
            }
        }

        public ShadowCacheStats LatestShadowCacheStats { get; private set; }

        internal struct ShadowCacheSnapshotEntry
        {
            public float shadow;

            public uint epoch;

            public uint lastUpdateFrame;

            public uint valid;
        }

        internal struct BrickShadowCacheSnapshotEntry
        {
            public float meanShadow;

            public float variance;

            public uint epoch;

            public uint lastUpdateFrame;
        }

        public readonly struct ShadowCacheGlobalStats
        {
            public readonly bool valid;

            public readonly uint epoch;

            public readonly uint frameIndex;

            public readonly uint surfelCount;

            public readonly uint surfelReady;

            public readonly uint surfelFresh;

            public readonly uint surfelStale;

            public readonly uint surfelInvalidEpoch;

            public readonly uint surfelUninitialized;

            public readonly float surfelMeanShadow;

            public readonly uint brickCount;

            public readonly uint brickReady;

            public readonly uint brickFresh;

            public readonly uint brickStale;

            public readonly uint brickHighVariance;

            public readonly uint brickInvalidEpoch;

            public readonly uint brickUninitialized;

            public readonly float brickMeanShadow;

            public ShadowCacheGlobalStats(
                uint epoch, uint frameIndex,
                uint surfelCount, uint surfelReady, uint surfelFresh, uint surfelStale,
                uint surfelInvalidEpoch, uint surfelUninitialized, float surfelMeanShadow,
                uint brickCount, uint brickReady, uint brickFresh, uint brickStale,
                uint brickHighVariance, uint brickInvalidEpoch, uint brickUninitialized,
                float brickMeanShadow)
            {
                valid = true;
                this.epoch = epoch;
                this.frameIndex = frameIndex;
                this.surfelCount = surfelCount;
                this.surfelReady = surfelReady;
                this.surfelFresh = surfelFresh;
                this.surfelStale = surfelStale;
                this.surfelInvalidEpoch = surfelInvalidEpoch;
                this.surfelUninitialized = surfelUninitialized;
                this.surfelMeanShadow = surfelMeanShadow;
                this.brickCount = brickCount;
                this.brickReady = brickReady;
                this.brickFresh = brickFresh;
                this.brickStale = brickStale;
                this.brickHighVariance = brickHighVariance;
                this.brickInvalidEpoch = brickInvalidEpoch;
                this.brickUninitialized = brickUninitialized;
                this.brickMeanShadow = brickMeanShadow;
            }
        }

        public ShadowCacheGlobalStats LatestShadowCacheGlobalStats { get; private set; }

        public ShadowCacheGlobalStats LatestShadowCacheWindowStats { get; private set; }

        private bool _shadowCacheGlobalStatsReadbackPending;

        private int _shadowCacheGlobalStatsPendingReadbacks;

        private bool _hasLastShadowCacheGlobalStatsReadbackFrame;

        private uint _lastShadowCacheGlobalStatsReadbackFrame;

        private uint _pendingShadowCacheGlobalStatsEpoch;

        private uint _pendingShadowCacheGlobalStatsFrameIndex;

        private int _pendingShadowCacheGlobalStatsMaxAge;

        private float _pendingShadowCacheGlobalStatsVarianceThreshold;

        private ShadowCacheSnapshotEntry[] _pendingShadowCacheGlobalStatsEntries;

        private BrickShadowCacheSnapshotEntry[] _pendingBrickShadowCacheGlobalStatsEntries;

        internal void UpdateShadowCacheStats(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                return;
            }

            var data = request.GetData<uint>();
            if (data.Length < 8)
            {
                return;
            }

            LatestShadowCacheStats = new ShadowCacheStats(
                data[0],
                data[1],
                data[2],
                data[3],
                data[4],
                data[5],
                data[6],
                data[7]);
        }

        internal bool ShouldRequestShadowCacheGlobalStatsReadback(uint frameIndex)
        {
            if (!enableShadowCacheStats || _shadowCacheGlobalStatsReadbackPending)
            {
                return false;
            }

            int interval = Mathf.Max(1, shadowCacheStatsReadbackInterval);
            return !_hasLastShadowCacheGlobalStatsReadbackFrame ||
                   frameIndex - _lastShadowCacheGlobalStatsReadbackFrame >= interval;
        }

        internal void BeginShadowCacheGlobalStatsReadback(uint epoch, uint frameIndex,
            int maxAge, float varianceThreshold)
        {
            _shadowCacheGlobalStatsReadbackPending = true;
            _shadowCacheGlobalStatsPendingReadbacks = 2;
            _pendingShadowCacheGlobalStatsEpoch = epoch;
            _pendingShadowCacheGlobalStatsFrameIndex = frameIndex;
            _pendingShadowCacheGlobalStatsMaxAge = Mathf.Max(1, maxAge);
            _pendingShadowCacheGlobalStatsVarianceThreshold = Mathf.Max(0f, varianceThreshold);
            _pendingShadowCacheGlobalStatsEntries = null;
            _pendingBrickShadowCacheGlobalStatsEntries = null;
            _lastShadowCacheGlobalStatsReadbackFrame = frameIndex;
            _hasLastShadowCacheGlobalStatsReadbackFrame = true;
        }

        internal void UpdateShadowCacheGlobalStatsEntries(AsyncGPUReadbackRequest request)
        {
            if (!_shadowCacheGlobalStatsReadbackPending)
            {
                return;
            }

            if (!request.hasError)
            {
                var data = request.GetData<ShadowCacheSnapshotEntry>();
                _pendingShadowCacheGlobalStatsEntries = new ShadowCacheSnapshotEntry[data.Length];
                data.CopyTo(_pendingShadowCacheGlobalStatsEntries);
            }

            CompleteShadowCacheGlobalStatsReadback();
        }

        internal void UpdateBrickShadowCacheGlobalStatsEntries(AsyncGPUReadbackRequest request)
        {
            if (!_shadowCacheGlobalStatsReadbackPending)
            {
                return;
            }

            if (!request.hasError)
            {
                var data = request.GetData<BrickShadowCacheSnapshotEntry>();
                _pendingBrickShadowCacheGlobalStatsEntries = new BrickShadowCacheSnapshotEntry[data.Length];
                data.CopyTo(_pendingBrickShadowCacheGlobalStatsEntries);
            }

            CompleteShadowCacheGlobalStatsReadback();
        }

        internal void ClearShadowCacheGlobalStats()
        {
            _shadowCacheGlobalStatsReadbackPending = false;
            _shadowCacheGlobalStatsPendingReadbacks = 0;
            _hasLastShadowCacheGlobalStatsReadbackFrame = false;
            _lastShadowCacheGlobalStatsReadbackFrame = 0;
            _pendingShadowCacheGlobalStatsEntries = null;
            _pendingBrickShadowCacheGlobalStatsEntries = null;
            LatestShadowCacheGlobalStats = default;
            LatestShadowCacheWindowStats = default;
        }

        private void CompleteShadowCacheGlobalStatsReadback()
        {
            if (!_shadowCacheGlobalStatsReadbackPending)
            {
                return;
            }

            _shadowCacheGlobalStatsPendingReadbacks =
                Mathf.Max(0, _shadowCacheGlobalStatsPendingReadbacks - 1);
            if (_shadowCacheGlobalStatsPendingReadbacks > 0)
            {
                return;
            }

            _shadowCacheGlobalStatsReadbackPending = false;
            if (_pendingShadowCacheGlobalStatsEntries == null ||
                _pendingBrickShadowCacheGlobalStatsEntries == null)
            {
                return;
            }

            LatestShadowCacheGlobalStats = BuildShadowCacheStats(
                _pendingShadowCacheGlobalStatsEntries,
                _pendingBrickShadowCacheGlobalStatsEntries,
                null,
                null);

            BuildCurrentWindowShadowCacheIndices(out var windowSurfels, out var windowBricks);
            LatestShadowCacheWindowStats = BuildShadowCacheStats(
                _pendingShadowCacheGlobalStatsEntries,
                _pendingBrickShadowCacheGlobalStatsEntries,
                windowSurfels,
                windowBricks);
        }

        private ShadowCacheGlobalStats BuildShadowCacheStats(
            ShadowCacheSnapshotEntry[] surfelEntries,
            BrickShadowCacheSnapshotEntry[] brickEntries,
            HashSet<int> surfelIndices,
            HashSet<int> brickIndices)
        {
            uint surfelReady = 0;
            uint surfelFresh = 0;
            uint surfelStale = 0;
            uint surfelInvalidEpoch = 0;
            uint surfelUninitialized = 0;
            float surfelShadowSum = 0f;

            int surfelTotal = surfelIndices?.Count ?? surfelEntries.Length;
            if (surfelIndices != null)
            {
                foreach (int surfelIndex in surfelIndices)
                {
                    if (surfelIndex >= 0 && surfelIndex < surfelEntries.Length)
                    {
                        AccumulateSurfel(surfelEntries[surfelIndex]);
                    }
                    else
                    {
                        surfelUninitialized++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < surfelEntries.Length; i++)
                {
                    AccumulateSurfel(surfelEntries[i]);
                }
            }

            uint brickReady = 0;
            uint brickFresh = 0;
            uint brickStale = 0;
            uint brickHighVariance = 0;
            uint brickInvalidEpoch = 0;
            uint brickUninitialized = 0;
            float brickShadowSum = 0f;

            int brickTotal = brickIndices?.Count ?? brickEntries.Length;
            if (brickIndices != null)
            {
                foreach (int brickIndex in brickIndices)
                {
                    if (brickIndex >= 0 && brickIndex < brickEntries.Length)
                    {
                        AccumulateBrick(brickEntries[brickIndex]);
                    }
                    else
                    {
                        brickUninitialized++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < brickEntries.Length; i++)
                {
                    AccumulateBrick(brickEntries[i]);
                }
            }

            return new ShadowCacheGlobalStats(
                _pendingShadowCacheGlobalStatsEpoch,
                _pendingShadowCacheGlobalStatsFrameIndex,
                (uint)surfelTotal,
                surfelReady,
                surfelFresh,
                surfelStale,
                surfelInvalidEpoch,
                surfelUninitialized,
                surfelReady > 0 ? surfelShadowSum / surfelReady : 1f,
                (uint)brickTotal,
                brickReady,
                brickFresh,
                brickStale,
                brickHighVariance,
                brickInvalidEpoch,
                brickUninitialized,
                brickReady > 0 ? brickShadowSum / brickReady : 1f);

            void AccumulateSurfel(ShadowCacheSnapshotEntry entry)
            {
                if (entry.valid == 0)
                {
                    surfelUninitialized++;
                    return;
                }

                if (entry.epoch != _pendingShadowCacheGlobalStatsEpoch)
                {
                    surfelInvalidEpoch++;
                    return;
                }

                surfelReady++;
                surfelShadowSum += Mathf.Clamp01(entry.shadow);
                if (ShadowCacheAge(_pendingShadowCacheGlobalStatsFrameIndex, entry.lastUpdateFrame) >
                    _pendingShadowCacheGlobalStatsMaxAge)
                {
                    surfelStale++;
                }
                else
                {
                    surfelFresh++;
                }
            }

            void AccumulateBrick(BrickShadowCacheSnapshotEntry entry)
            {
                if (entry.epoch == 0)
                {
                    brickUninitialized++;
                    return;
                }

                if (entry.epoch != _pendingShadowCacheGlobalStatsEpoch)
                {
                    brickInvalidEpoch++;
                    return;
                }

                if (entry.variance > _pendingShadowCacheGlobalStatsVarianceThreshold)
                {
                    brickHighVariance++;
                    return;
                }

                brickReady++;
                brickShadowSum += Mathf.Clamp01(entry.meanShadow);
                if (ShadowCacheAge(_pendingShadowCacheGlobalStatsFrameIndex, entry.lastUpdateFrame) >
                    _pendingShadowCacheGlobalStatsMaxAge)
                {
                    brickStale++;
                }
                else
                {
                    brickFresh++;
                }
            }
        }

        private void BuildCurrentWindowShadowCacheIndices(out HashSet<int> surfelIndices, out HashSet<int> brickIndices)
        {
            surfelIndices = new HashSet<int>();
            brickIndices = new HashSet<int>();

            if (_probesInBoundingBox.Count == 0 ||
                _allProbes == null ||
                _allFactors == null ||
                _allBricks == null)
            {
                return;
            }

            for (int probeIndex = 0; probeIndex < _probesInBoundingBox.Count; probeIndex++)
            {
                var probe = _probesInBoundingBox[probeIndex];
                if (probe == null || probe.Index < 0 || probe.Index >= _allProbes.Length)
                {
                    continue;
                }

                var factorIndices = _allProbes[probe.Index];
                for (int factorIndex = factorIndices.start; factorIndex <= factorIndices.end; factorIndex++)
                {
                    if (factorIndex < 0 || factorIndex >= _allFactors.Length)
                    {
                        continue;
                    }

                    int brickIndex = _allFactors[factorIndex].brickIndex;
                    if (brickIndex < 0 || brickIndex >= _allBricks.Length)
                    {
                        continue;
                    }

                    if (!brickIndices.Add(brickIndex))
                    {
                        continue;
                    }

                    var brick = _allBricks[brickIndex];
                    int end = brick.start + brick.count;
                    for (int surfelIndex = brick.start; surfelIndex < end; surfelIndex++)
                    {
                        surfelIndices.Add(surfelIndex);
                    }
                }
            }
        }

        private static uint ShadowCacheAge(uint frameIndex, uint lastUpdateFrame)
        {
            return frameIndex >= lastUpdateFrame ? frameIndex - lastUpdateFrame : 0;
        }

#if UNITY_EDITOR
        internal struct ShadowCacheDebugEntry
        {
            public float shadow;

            public uint status;

            public uint age;

            public uint epoch;
        }

        internal struct BrickShadowCacheDebugEntry
        {
            public float meanShadow;

            public float variance;

            public uint epoch;

            public uint lastUpdateFrame;
        }

        internal struct ShadowCacheProbeDebugSummary
        {
            public bool valid;

            public ShadowCacheDebugStatus status;

            public float meanShadow;

            public uint unknownCount;

            public uint freshHitCount;

            public uint sampledCount;

            public uint fallbackCount;

            public uint uncoveredCount;

            public uint brickHitCount;
        }

        private bool _shadowCacheDebugReadbackPending;

        private int _shadowCacheDebugPendingReadbacks;

        private bool _hasLastShadowCacheDebugReadbackFrame;

        private uint _lastShadowCacheDebugReadbackFrame;

        private uint _pendingShadowCacheDebugEpoch;

        private uint _pendingShadowCacheDebugFrameIndex;

        private ShadowCacheDebugEntry[] _pendingShadowCacheDebugEntries;

        private BrickShadowCacheDebugEntry[] _pendingBrickShadowCacheDebugEntries;

        internal ShadowCacheDebugEntry[] LatestShadowCacheDebugEntries { get; private set; }

        internal BrickShadowCacheDebugEntry[] LatestBrickShadowCacheDebugEntries { get; private set; }

        internal ShadowCacheProbeDebugSummary[] LatestShadowCacheProbeSummaries { get; private set; }

        internal bool HasShadowCacheDebugSnapshot { get; private set; }

        internal uint LatestShadowCacheDebugEpoch { get; private set; }

        internal uint LatestShadowCacheDebugFrameIndex { get; private set; }

        internal double LatestShadowCacheDebugTime { get; private set; }

        internal bool IsShadowCacheDebugActive => debugMode == ProbeVolumeDebugMode.ShadowCache;

        internal bool ShouldRequestShadowCacheDebugReadback(uint frameIndex)
        {
            if (!IsShadowCacheDebugActive || _shadowCacheDebugReadbackPending)
            {
                return false;
            }

            int interval = Mathf.Max(1, shadowCacheDebugReadbackInterval);
            return !_hasLastShadowCacheDebugReadbackFrame ||
                   frameIndex - _lastShadowCacheDebugReadbackFrame >= interval;
        }

        internal void BeginShadowCacheDebugReadback(uint epoch, uint frameIndex)
        {
            _shadowCacheDebugReadbackPending = true;
            _shadowCacheDebugPendingReadbacks = 2;
            _pendingShadowCacheDebugEpoch = epoch;
            _pendingShadowCacheDebugFrameIndex = frameIndex;
            _pendingShadowCacheDebugEntries = null;
            _pendingBrickShadowCacheDebugEntries = null;
            _lastShadowCacheDebugReadbackFrame = frameIndex;
            _hasLastShadowCacheDebugReadbackFrame = true;
        }

        internal void UpdateShadowCacheDebugEntries(AsyncGPUReadbackRequest request)
        {
            if (!_shadowCacheDebugReadbackPending)
            {
                return;
            }

            if (!request.hasError)
            {
                var data = request.GetData<ShadowCacheDebugEntry>();
                _pendingShadowCacheDebugEntries = new ShadowCacheDebugEntry[data.Length];
                data.CopyTo(_pendingShadowCacheDebugEntries);
            }

            CompleteShadowCacheDebugReadback();
        }

        internal void UpdateBrickShadowCacheDebugEntries(AsyncGPUReadbackRequest request)
        {
            if (!_shadowCacheDebugReadbackPending)
            {
                return;
            }

            if (!request.hasError)
            {
                var data = request.GetData<BrickShadowCacheDebugEntry>();
                _pendingBrickShadowCacheDebugEntries = new BrickShadowCacheDebugEntry[data.Length];
                data.CopyTo(_pendingBrickShadowCacheDebugEntries);
            }

            CompleteShadowCacheDebugReadback();
        }

        internal void ClearShadowCacheDebugSnapshot()
        {
            _shadowCacheDebugReadbackPending = false;
            _shadowCacheDebugPendingReadbacks = 0;
            _hasLastShadowCacheDebugReadbackFrame = false;
            _lastShadowCacheDebugReadbackFrame = 0;
            _pendingShadowCacheDebugEntries = null;
            _pendingBrickShadowCacheDebugEntries = null;
            LatestShadowCacheDebugEntries = null;
            LatestBrickShadowCacheDebugEntries = null;
            LatestShadowCacheProbeSummaries = null;
            HasShadowCacheDebugSnapshot = false;
            LatestShadowCacheDebugEpoch = 0;
            LatestShadowCacheDebugFrameIndex = 0;
            LatestShadowCacheDebugTime = 0;
        }

        private void CompleteShadowCacheDebugReadback()
        {
            if (!_shadowCacheDebugReadbackPending)
            {
                return;
            }

            _shadowCacheDebugPendingReadbacks = Mathf.Max(0, _shadowCacheDebugPendingReadbacks - 1);
            if (_shadowCacheDebugPendingReadbacks > 0)
            {
                return;
            }

            _shadowCacheDebugReadbackPending = false;
            if (_pendingShadowCacheDebugEntries == null || _pendingBrickShadowCacheDebugEntries == null)
            {
                return;
            }

            LatestShadowCacheDebugEntries = _pendingShadowCacheDebugEntries;
            LatestBrickShadowCacheDebugEntries = _pendingBrickShadowCacheDebugEntries;
            LatestShadowCacheDebugEpoch = _pendingShadowCacheDebugEpoch;
            LatestShadowCacheDebugFrameIndex = _pendingShadowCacheDebugFrameIndex;
            LatestShadowCacheDebugTime = Time.realtimeSinceStartupAsDouble;
            HasShadowCacheDebugSnapshot = true;
            RebuildShadowCacheProbeSummaries();
        }

        private void RebuildShadowCacheProbeSummaries()
        {
            if (_probeDebugData == null || LatestShadowCacheDebugEntries == null)
            {
                LatestShadowCacheProbeSummaries = null;
                return;
            }

            var summaries = new ShadowCacheProbeDebugSummary[_probeDebugData.Length];
            for (int probeIndex = 0; probeIndex < _probeDebugData.Length; probeIndex++)
            {
                var debugData = _probeDebugData[probeIndex];
                var localSurfelIndices = debugData?.LocalSurfelIndices;
                if (localSurfelIndices == null || localSurfelIndices.Length == 0)
                {
                    continue;
                }

                uint unknownCount = 0;
                uint freshHitCount = 0;
                uint sampledCount = 0;
                uint fallbackCount = 0;
                uint uncoveredCount = 0;
                uint brickHitCount = 0;
                uint validEntryCount = 0;
                float shadowSum = 0f;

                for (int i = 0; i < localSurfelIndices.Length; i++)
                {
                    int surfelIndex = localSurfelIndices[i];
                    if (surfelIndex < 0 || surfelIndex >= LatestShadowCacheDebugEntries.Length)
                    {
                        unknownCount++;
                        continue;
                    }

                    var entry = LatestShadowCacheDebugEntries[surfelIndex];
                    shadowSum += Mathf.Clamp01(entry.shadow);
                    validEntryCount++;

                    switch ((ShadowCacheDebugStatus)entry.status)
                    {
                        case ShadowCacheDebugStatus.FreshHit:
                            freshHitCount++;
                            break;
                        case ShadowCacheDebugStatus.Sampled:
                            sampledCount++;
                            break;
                        case ShadowCacheDebugStatus.FallbackFromCache:
                            fallbackCount++;
                            break;
                        case ShadowCacheDebugStatus.UncoveredNoCache:
                            uncoveredCount++;
                            break;
                        case ShadowCacheDebugStatus.BrickCacheHit:
                            brickHitCount++;
                            break;
                        default:
                            unknownCount++;
                            break;
                    }
                }

                summaries[probeIndex] = new ShadowCacheProbeDebugSummary
                {
                    valid = validEntryCount > 0,
                    status = SelectShadowCacheProbeStatus(
                        unknownCount, freshHitCount, sampledCount, fallbackCount, uncoveredCount, brickHitCount),
                    meanShadow = validEntryCount > 0 ? shadowSum / validEntryCount : 1f,
                    unknownCount = unknownCount,
                    freshHitCount = freshHitCount,
                    sampledCount = sampledCount,
                    fallbackCount = fallbackCount,
                    uncoveredCount = uncoveredCount,
                    brickHitCount = brickHitCount
                };
            }

            LatestShadowCacheProbeSummaries = summaries;
        }

        private static ShadowCacheDebugStatus SelectShadowCacheProbeStatus(
            uint unknownCount, uint freshHitCount, uint sampledCount,
            uint fallbackCount, uint uncoveredCount, uint brickHitCount)
        {
            var status = ShadowCacheDebugStatus.Unknown;
            uint bestCount = unknownCount;
            int bestPriority = 0;

            Select(freshHitCount, ShadowCacheDebugStatus.FreshHit, 1);
            Select(brickHitCount, ShadowCacheDebugStatus.BrickCacheHit, 2);
            Select(sampledCount, ShadowCacheDebugStatus.Sampled, 3);
            Select(fallbackCount, ShadowCacheDebugStatus.FallbackFromCache, 4);
            Select(uncoveredCount, ShadowCacheDebugStatus.UncoveredNoCache, 5);
            return status;

            void Select(uint count, ShadowCacheDebugStatus candidate, int priority)
            {
                if (count > bestCount || count == bestCount && priority > bestPriority)
                {
                    bestCount = count;
                    bestPriority = priority;
                    status = candidate;
                }
            }
        }
#endif

        private void Start()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            if (!IsFeatureEnabled) return;
            _hasStarted = true;
            AllocateProbes();
            TryLoadAsset(asset);

            // Initialize camera reference
            _mainCamera = Camera.main;
            if (!_mainCamera)
            {
                _mainCamera = FindFirstObjectByType<Camera>();
            }
        }

        private void OnEnable()
        {
            IsFeatureEnabled = IllusionRenderingUtils.GetPrecomputedRadianceTransferFeatureEnabled();
            if (!IsFeatureEnabled) return;
            PRTVolumeManager.RegisterProbeVolume(this);
            ResetProbeUpdateRotation();

            if (Application.isPlaying && _hasStarted && !IsProbeValid())
            {
                AllocateProbes();
                TryLoadAsset(asset);
            }
        }

        private void OnDisable()
        {
            PRTVolumeManager.UnregisterProbeVolume(this);
            ReleaseRuntimeData();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!gameObject.scene.IsValid()) return;
            if (!IsFeatureEnabled) return;
            
            // Restart round-robin
            ResetProbeUpdateRotation();
            
            // Recalculate virtual offset
            if (_cachedGeometryBias != geometryBias || _cachedRayOriginBias != rayOriginBias)
            {
                _cachedVirtualOffsetPositions.Clear();
            }
            _cachedGeometryBias = geometryBias;
            _cachedRayOriginBias = rayOriginBias;
        }

        private void Update()
        {
            if (!gameObject.scene.IsValid()) return;
            if (Application.isPlaying) return;
            if (!IsFeatureEnabled) return;
            
            if (!CalculateVoxelGrid().Equals(CurrentVoxelGrid) || !CalculateProbeGrid().Equals(_probeGrid))
            {
                ReleaseProbes();
            }
            
            if (!IsProbeValid())
            {
                AllocateProbes();
                TryLoadAsset(asset);
            }

            if (Probes != null)
            {
                foreach (var probe in Probes)
                {
                    probe.UpdateVisibility();
                }
            }
        }
#endif
        private void OnDestroy()
        {
            ReleaseRuntimeData();
        }

        private void ReleaseRuntimeData()
        {
            ClearShadowCacheGlobalStats();
            ReleaseProbes();
            ReleaseRenderTexture(ref _coefficientVoxelRT);
            ReleaseRenderTexture(ref _validityVoxelRT);

            _globalSurfelBuffer?.Release();
            _globalSurfelBuffer = null;
            _isDataInitialized = false;
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture)
            {
                texture.Release();
            }

            texture = null;
        }

        private Grid CalculateVoxelGrid()
        {
            return new Grid(
                    Mathf.Min(voxelProbeSize.x, probeSizeX),
                    Mathf.Min(voxelProbeSize.y, probeSizeY),
                    Mathf.Min(voxelProbeSize.z, probeSizeZ),
                    probeGridSize);
        }

        private Grid CalculateProbeGrid()
        {
            return new Grid(
                probeSizeX,
                probeSizeY,
                probeSizeZ,
                probeGridSize);
        }

        /// <summary>
        /// Get all bricks
        /// </summary>
        public SurfelIndices[] GetAllBricks() => _allBricks;

        /// <summary>
        /// Get all factors
        /// </summary>
        public BrickFactor[] GetAllFactors() => _allFactors;

        /// <summary>
        /// Get all probes
        /// </summary>
        public FactorIndices[] GetAllProbes() => _allProbes;

        /// <summary>
        /// Check if the probe volume is valid
        /// </summary>
        /// <returns>True if the probe volume is valid</returns>
        private bool IsProbeValid()
        {
            if (Probes == null || !Probes.Any()) return false;
            return _coefficientVoxelRT && _coefficientVoxelRT.IsCreated() &&
                   _validityVoxelRT && _validityVoxelRT.IsCreated();
        }

        /// <summary>
        /// Check if the probe volume is valid
        /// </summary>
        /// <returns>True if the probe volume is valid</returns>
        public bool IsActivate()
        {
            return enabled && IsProbeValid() && _isDataInitialized;
        }

        /// <summary>
        /// load surfel data from <see cref="PRTProbeVolumeAsset"/>
        /// </summary>
        /// <param name="volumeAsset"></param>
        private void TryLoadAsset(PRTProbeVolumeAsset volumeAsset)
        {
            _globalSurfelBuffer?.Release();
            _globalSurfelBuffer = null;
            _isDataInitialized = false;
#if UNITY_EDITOR
            ReleaseProbeDebugData();
#endif

            if (!volumeAsset || !volumeAsset.HasValidData)
            {
                return;
            }

            var cellData = volumeAsset.CellData;
            // Check if we have the correct number of probes
            int probeNum = probeSizeX * probeSizeY * probeSizeZ;
            if (cellData.probes.Length != probeNum)
            {
                Debug.LogWarning($"{nameof(PRTProbeVolumeAsset)} probe count mismatch. " +
                                 $"Expected: {probeNum}, Got: {cellData.probes.Length}");
                return;
            }

            // Initialize all surfels and bricks
            var surfels = cellData.surfels;
            _allBricks = cellData.bricks;
            _allFactors = cellData.factors;
            _allProbes = cellData.probes;
            _validity = cellData.validityMasks;
            _globalSurfelBuffer = new ComputeBuffer(surfels.Length, Surfel.Stride);
            _globalSurfelBuffer.SetData(surfels);

#if UNITY_EDITOR
            // Setup debug data
            for (int i = 0; i < Probes.Length; i++)
            {
                var factorIndices = cellData.probes[i];
                _probeDebugData[i] = new PRTProbeDebugData(factorIndices, cellData.factors, cellData.bricks, surfels);
            }
#endif

            _isDataInitialized = true;
        }

#if UNITY_EDITOR
        private void ReleaseProbeDebugData()
        {
            ClearShadowCacheDebugSnapshot();

            if (_probeDebugData == null)
            {
                return;
            }

            for (int i = 0; i < _probeDebugData.Length; i++)
            {
                _probeDebugData[i]?.Dispose();
                _probeDebugData[i] = null;
            }
        }
#endif

        private void ReleaseProbes()
        {
            if (Probes != null)
            {
                foreach (var probe in Probes)
                {
                    probe?.Dispose();
                }
            }

            Probes = null;
            _probesInBoundingBox.Clear();
            _localProbeIndices.Clear();
            _priorityProbeQueue.Clear();
#if UNITY_EDITOR
            _cachedVirtualOffsetPositions.Clear();
            ReleaseProbeDebugData();
#endif
        }

        /// <summary>
        /// Create probes based on volume current location.
        /// </summary>
        private void AllocateProbes()
        {
            ReleaseProbes();

            _probeGrid = CalculateProbeGrid();
            CurrentVoxelGrid = CalculateVoxelGrid();

            // generate probes
            int probeNum = probeSizeX * probeSizeY * probeSizeZ;
            Probes = new PRTProbe[probeNum];
#if UNITY_EDITOR
            _probeDebugData = new PRTProbeDebugData[probeNum];
#endif
            for (int x = 0; x < probeSizeX; x++)
            {
                for (int y = 0; y < probeSizeY; y++)
                {
                    for (int z = 0; z < probeSizeZ; z++)
                    {
                        Vector3 relativePos = new Vector3(x, y, z) * probeGridSize;

                        // setup probe
                        int index = x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
                        Probes[index] = new PRTProbe(index, relativePos, this);
                    }
                }
            }

            // Create 3D textures for SH coefficients
            InitializeVoxelTexture(CurrentVoxelGrid.X, CurrentVoxelGrid.Z, CurrentVoxelGrid.Y);

            // Initialize validity tracking
            InitializeValidityData();

            // Reset probe update rotation when new probes are generated
            ResetProbeUpdateRotation();
        }

        private void InitializeVoxelTexture(int width, int height, int depth)
        {
            ReleaseRenderTexture(ref _coefficientVoxelRT);
            // Layout: float3[_grid.X, _grid.Z, _grid.Y * 9]
            // Each depth slice corresponds to one RGB component of SH coefficient
            _coefficientVoxelRT = new RenderTexture(width, height, 0, Texture3DFormat)
            {
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = depth * 9,
                name = "CoefficientVoxelTexture"
            };
            _coefficientVoxelRT.Create();

            // Create validity texture (packed 32-bit float: intensity + validity per probe)
            ReleaseRenderTexture(ref _validityVoxelRT);
            _validityVoxelRT = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
            {
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = depth, // One layer per Y slice
                name = "ValidityVoxelTexture"
            };
            _validityVoxelRT.Create();
        }

        private bool EnableMultiFrameRelight()
        {
            return multiFrameRelight;
        }

        public void PrepareRelightCycle(int inputHash)
        {
            if (!relightOnceUntilDirty)
            {
                _hasRelightInputHash = false;
                _lastRelightInputHash = 0;
                return;
            }

            if (_hasRelightInputHash && _lastRelightInputHash == inputHash)
            {
                return;
            }

            _hasRelightInputHash = true;
            _lastRelightInputHash = inputHash;
            _relitProbeIndicesInWindow.Clear();
            _relightWindowCycleComplete = false;
            _currentProbeUpdateIndex = 0;
            _lastRoundRobinProbeCount = 0;
        }

        public Vector3 GetVoxelMinCorner()
        {
            return transform.position;
        }

        /// <summary>
        /// Get probes that need to be updated for performance optimization
        /// </summary>
        /// <returns>Array of probes to update this frame</returns>
        public void GetProbesToUpdate(List<PRTProbe> probes)
        {
            if (Probes == null || Probes.Length == 0)
                return;

            // Update bounding box
            CalculateCameraBoundingBox();

            // If bounding box didn't change, but we have no probes in bounding box,
            // we need to populate it (this can happen when camera starts outside volume)
            if (_boundingBoxChanged || _probesInBoundingBox.Count == 0)
            {
                using (ListPool<PRTProbe>.Get(out var probesInLastBoundingBox))
                {
                    probesInLastBoundingBox.AddRange(_probesInBoundingBox);
                    GetProbesInBoundingBox();

                    // Find NEW probes that entered the bounding box (fixed logic)
                    using (HashSetPool<PRTProbe>.Get(out var oldProbesSet))
                    {
                        foreach (var probe in probesInLastBoundingBox)
                        {
                            oldProbesSet.Add(probe);
                        }

                        for (int i = 0; i < _probesInBoundingBox.Count; i++)
                        {
                            var probe = _probesInBoundingBox[i];
                            if (!oldProbesSet.Contains(probe))
                            {
                                // New probe entered - add to priority queue
                                if (!_priorityProbeQueue.Contains(probe))
                                {
                                    _priorityProbeQueue.Enqueue(probe);
                                }
                            }
                        }
                    }

                    if (_originalBoxMin.x < 0 || _originalBoxMin.y < 0 || _originalBoxMin.z < 0)
                    {
                        _originalBoxMin = _boundingBoxMin;
                    }
                }
            }

            if (!ShouldStopRelightAfterWindowCycle)
            {
                _relitProbeIndicesInWindow.Clear();
                _relightWindowCycleComplete = false;
            }
            else if (_relightWindowCycleComplete)
            {
                return;
            }

            // If multi-frame relight is not needed, relight all probes in bounding box
            if (!EnableMultiFrameRelight())
            {
                probes.AddRange(_probesInBoundingBox);
                TrackRelightWindowCoverage(probes);
                return;
            }

            // Calculate total budget for this frame
            int totalBudget = _probesToUpdateCount + localProbeCount;
            int remainingBudget = totalBudget;

            // Reset round-robin probe count for this frame
            _lastRoundRobinProbeCount = 0;

            using (HashSetPool<PRTProbe>.Get(out var addedProbes))
            {
                // Step 1: Process priority queue first (new probes)
                while (_priorityProbeQueue.Count > 0 && remainingBudget > 0)
                {
                    var probe = _priorityProbeQueue.Dequeue();
                    // Verify probe is still in bounding box
                    if (_probesInBoundingBox.Contains(probe))
                    {
                        probes.Add(probe);
                        addedProbes.Add(probe);
                        remainingBudget--;
                    }
                }

                // Step 2: Add local probes (camera-nearby probes)
                foreach (int localProbeIdx in _localProbeIndices)
                {
                    if (remainingBudget <= 0) break;

                    if (localProbeIdx >= 0 && localProbeIdx < Probes.Length && Probes[localProbeIdx] != null)
                    {
                        var localProbe = Probes[localProbeIdx];
                        if (!addedProbes.Contains(localProbe))
                        {
                            probes.Add(localProbe);
                            addedProbes.Add(localProbe);
                            remainingBudget--;
                        }
                    }
                }

                // Step 3: Fill remaining budget with round-robin probes from bounding box
                if (_probesInBoundingBox.Count > 0)
                {
                    int startIndex = _currentProbeUpdateIndex;
                    int checkedCount = 0;
                    int addedFromRoundRobin = 0;

                    while (remainingBudget > 0 && checkedCount < _probesInBoundingBox.Count)
                    {
                        int index = (startIndex + checkedCount) % _probesInBoundingBox.Count;
                        var probe = _probesInBoundingBox[index];

                        if (!addedProbes.Contains(probe))
                        {
                            probes.Add(probe);
                            addedProbes.Add(probe);
                            remainingBudget--;
                            addedFromRoundRobin++;
                        }

                        checkedCount++;
                    }

                    _lastRoundRobinProbeCount = addedFromRoundRobin;
                }
            }

            TrackRelightWindowCoverage(probes);
        }

        private void TrackRelightWindowCoverage(List<PRTProbe> probes)
        {
            if (!ShouldStopRelightAfterWindowCycle || probes == null || probes.Count == 0)
            {
                return;
            }

            for (int i = 0; i < probes.Count; i++)
            {
                var probe = probes[i];
                if (probe != null && _probesInBoundingBox.Contains(probe))
                {
                    _relitProbeIndicesInWindow.Add(probe.Index);
                }
            }

            _relightWindowCycleComplete = _probesInBoundingBox.Count > 0 &&
                                          _relitProbeIndicesInWindow.Count >= _probesInBoundingBox.Count;
        }

        /// <summary>
        /// Get bricks that need to be updated based on the probes being updated
        /// </summary>
        /// <param name="probesToUpdate">List of probes being updated this frame</param>
        /// <param name="bricksToUpdate">Output list of brick indices that need relighting</param>
        public void GetBricksToUpdate(List<PRTProbe> probesToUpdate, List<int> bricksToUpdate)
        {
            if (probesToUpdate == null || probesToUpdate.Count == 0 || _allFactors == null || _allBricks == null)
                return;

            using (HashSetPool<int>.Get(out var brickIndicesSet))
            {
                foreach (var probe in probesToUpdate)
                {
                    var factorIndices = _allProbes[probe.Index];

                    // Iterate through all factors for this probe
                    for (int factorIndex = factorIndices.start; factorIndex <= factorIndices.end; factorIndex++)
                    {
                        if (factorIndex >= 0 && factorIndex < _allFactors.Length)
                        {
                            var factor = _allFactors[factorIndex];
                            brickIndicesSet.Add(factor.brickIndex);
                        }
                    }
                }

                // Convert to list and sort for consistent ordering
                bricksToUpdate.Clear();
                bricksToUpdate.AddRange(brickIndicesSet);
                bricksToUpdate.Sort();
            }
        }

        public void AdvanceRenderFrame()
        {
            if (EnableMultiFrameRelight())
            {
                if (ShouldStopRelightAfterWindowCycle && _relightWindowCycleComplete)
                {
                    return;
                }

                // Advance the update index for next frame based on actual probes added from round-robin
                // This ensures we don't skip probes when priority queue or local probes consume budget
                if (_lastRoundRobinProbeCount > 0)
                {
                    _currentProbeUpdateIndex += _lastRoundRobinProbeCount;
                }
                else
                {
                    // If no probes were added from round-robin (all budget used by priority/local),
                    // still advance by the expected amount to maintain progress
                    _currentProbeUpdateIndex += _probesToUpdateCount;
                }

                // Wrap around if we've gone past the end
                if (_probesInBoundingBox.Count > 0)
                {
                    _currentProbeUpdateIndex %= _probesInBoundingBox.Count;
                }
                else
                {
                    _currentProbeUpdateIndex = 0;
                }

                // Update local probe indices based on camera position
                UpdateLocalProbeIndices();
            }
            else
            {
                _currentProbeUpdateIndex = 0;
            }
        }

        /// <summary>
        /// Reset probe update rotation to start from beginning
        /// </summary>
        private void ResetProbeUpdateRotation()
        {
            _originalBoxMin = new Vector3Int(-1, -1, -1);
            _currentProbeUpdateIndex = 0;
            _lastRoundRobinProbeCount = 0;
            _probesToUpdateCount = Probes != null ? CalculateProbesPerFrameUpdate(_probesInBoundingBox.Count, probesPerFrameUpdate) : 0;
            _relitProbeIndicesInWindow.Clear();
            _relightWindowCycleComplete = false;
            _lastRelightInputHash = 0;
            _hasRelightInputHash = false;
        }

        /// <summary>
        /// Get the largest divisor of Probes.Length that doesn't exceed probesPerFrameUpdate
        /// This ensures better proper cycling of probe updates
        /// </summary>
        /// <param name="probeLength"></param>
        /// <param name="countPerFrame"></param>
        /// <returns>Valid number of probes to update per frame</returns>
        private static int CalculateProbesPerFrameUpdate(int probeLength, int countPerFrame)
        {
            if (probeLength == 0)
                return 1;

            int maxProbesPerFrame = Mathf.Min(countPerFrame, probeLength);

            // Find the largest divisor of Probes.Length
            for (int i = maxProbesPerFrame; i >= 1; i--)
            {
                if (probeLength % i == 0)
                {
                    return i;
                }
            }

            return 1;
        }

        /// <summary>
        /// Update local probe indices based on camera position
        /// </summary>
        private void UpdateLocalProbeIndices()
        {
            if (!_mainCamera || Probes == null || Probes.Length == 0)
                return;

            Vector3 cameraPos = _mainCamera.transform.position;

            // Only recalculate if camera has moved significantly
            if (Vector3.Distance(cameraPos, _lastCameraPosition) < CameraMovementThreshold)
                return;

            _lastCameraPosition = cameraPos;
            _localProbeIndices.Clear();

            // Convert camera position to probe grid coordinates for more efficient distance calculation
            Vector3 gridPos = (cameraPos - transform.position) / probeGridSize;

            // Calculate distances from camera to all probes using grid coordinates
            using (ListPool<(int index, float distance)>.Get(out var probeDistances))
            {
                for (int i = 0; i < Probes.Length; i++)
                {
                    if (Probes[i] != null)
                    {
                        // Calculate probe position in grid coordinates
                        Vector3 probeGridPos = (Probes[i].Position - transform.position) / probeGridSize;

                        // Use squared distance for efficiency (avoiding sqrt)
                        float sqrDistance = (gridPos - probeGridPos).sqrMagnitude;
                        probeDistances.Add((i, sqrDistance));
                    }
                }

                // Sort by distance and take the closest ones
                probeDistances.Sort(static (a, b) => a.distance.CompareTo(b.distance));

                int count = Mathf.Min(localProbeCount, probeDistances.Count);
                for (int i = 0; i < count; i++)
                {
                    _localProbeIndices.Add(probeDistances[i].index);
                }
            }
        }


        /// <summary>
        /// Calculate bounding box based on camera position
        /// </summary>
        private void CalculateCameraBoundingBox()
        {
            if (!_mainCamera || Probes == null || Probes.Length == 0)
                return;

            Vector3 cameraPos = _mainCamera.transform.position;

            // Convert camera position to grid coordinates relative to Volume corner
            // Volume position is the corner (0,0,0) of the probe grid
            Vector3 gridPos = (cameraPos - transform.position) / probeGridSize;

            // Calculate the maximum valid bounding box position for each axis
            int maxX = Mathf.Max(0, probeSizeX - CurrentVoxelGrid.X);
            int maxY = Mathf.Max(0, probeSizeY - CurrentVoxelGrid.Y);
            int maxZ = Mathf.Max(0, probeSizeZ - CurrentVoxelGrid.Z);

            // Calculate bounding box center (grid 3d coordinates)
            Vector3Int coord3D = new Vector3Int(
                Mathf.RoundToInt(gridPos.x),
                Mathf.RoundToInt(gridPos.y),
                Mathf.RoundToInt(gridPos.z)
            );

            // Calculate bounding box minimum corner
            Vector3Int boundingBoxMin = new Vector3Int(
                coord3D.x - CurrentVoxelGrid.X / 2,
                coord3D.y - CurrentVoxelGrid.Y / 2,
                coord3D.z - CurrentVoxelGrid.Z / 2
            );

            Vector3Int newBoundingBoxMin = FindClosestValidBoundingBox(cameraPos, boundingBoxMin, maxX, maxY, maxZ);

            // Check if bounding box has changed
            int newHash = newBoundingBoxMin.GetHashCode();
            if (newHash != _lastBoundingBoxHash)
            {
                _boundingBoxMin = newBoundingBoxMin;
                _lastBoundingBoxHash = newHash;
                _boundingBoxChanged = true;

                // Update bounding box world coordinates
                Vector3 worldMin = transform.position + new Vector3(
                    _boundingBoxMin.x * probeGridSize,
                    _boundingBoxMin.y * probeGridSize,
                    _boundingBoxMin.z * probeGridSize
                );
                Vector3 worldSize = new Vector3(
                    (CurrentVoxelGrid.X - 1) * probeGridSize,
                    (CurrentVoxelGrid.Y - 1) * probeGridSize,
                    (CurrentVoxelGrid.Z - 1) * probeGridSize
                );
                _currentBoundingBox = new Bounds(worldMin + worldSize * 0.5f, worldSize);
            }
            else
            {
                _boundingBoxChanged = false;
            }
        }

        /// <summary>
        /// Get probes within the current bounding box
        /// </summary>
        private void GetProbesInBoundingBox()
        {
            _probesInBoundingBox.Clear();
            _relitProbeIndicesInWindow.Clear();
            _relightWindowCycleComplete = false;

            for (int x = _boundingBoxMin.x; x < _boundingBoxMin.x + CurrentVoxelGrid.X; x++)
            {
                for (int y = _boundingBoxMin.y; y < _boundingBoxMin.y + CurrentVoxelGrid.Y; y++)
                {
                    for (int z = _boundingBoxMin.z; z < _boundingBoxMin.z + CurrentVoxelGrid.Z; z++)
                    {
                        int index = x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
                        if (index >= 0 && index < Probes.Length && Probes[index] != null)
                        {
                            _probesInBoundingBox.Add(Probes[index]);
                        }
                    }
                }
            }

            _probesToUpdateCount = CalculateProbesPerFrameUpdate(_probesInBoundingBox.Count, probesPerFrameUpdate);
            _currentProbeUpdateIndex = 0;
            _lastRoundRobinProbeCount = 0;
        }

        /// <summary>
        /// Find the closest valid bounding box position when camera is outside volume
        /// </summary>
        private Vector3Int FindClosestValidBoundingBox(Vector3 cameraPos, Vector3Int boundingBoxMin, int maxX, int maxY, int maxZ)
        {
            // Convert camera position to grid coordinates
            Vector3 gridPos = (cameraPos - transform.position) / probeGridSize;

            // Start with a reasonable initial position based on camera direction
            Vector3Int startPosition = new Vector3Int(
                Mathf.Clamp(Mathf.RoundToInt(gridPos.x - CurrentVoxelGrid.X * 0.5f), 0, maxX),
                Mathf.Clamp(Mathf.RoundToInt(gridPos.y - CurrentVoxelGrid.Y * 0.5f), 0, maxY),
                Mathf.Clamp(Mathf.RoundToInt(gridPos.z - CurrentVoxelGrid.Z * 0.5f), 0, maxZ)
            );

            Vector3Int bestPosition = startPosition;
            float bestDistance = float.MaxValue;

            // Use a smart search strategy: start from the initial position and expand outward
            int searchRadius = Mathf.Max(maxX, maxY, maxZ);
            for (int radius = 0; radius <= searchRadius; radius++)
            {
                bool foundBetter = false;

                // Search in a cube around the start position
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            // Skip inner cubes that were already searched
                            if (radius > 0 && Mathf.Abs(dx) < radius && Mathf.Abs(dy) < radius && Mathf.Abs(dz) < radius)
                                continue;

                            Vector3Int candidatePosition = new Vector3Int(
                                startPosition.x + dx,
                                startPosition.y + dy,
                                startPosition.z + dz
                            );

                            // Check if this position is valid
                            if (candidatePosition.x >= 0 && candidatePosition.x <= maxX &&
                                candidatePosition.y >= 0 && candidatePosition.y <= maxY &&
                                candidatePosition.z >= 0 && candidatePosition.z <= maxZ)
                            {
                                // Calculate the center of this bounding box in world coordinates
                                Vector3 boundingBoxCenter = transform.position + new Vector3(
                                    (candidatePosition.x + CurrentVoxelGrid.X * 0.5f) * probeGridSize,
                                    (candidatePosition.y + CurrentVoxelGrid.Y * 0.5f) * probeGridSize,
                                    (candidatePosition.z + CurrentVoxelGrid.Z * 0.5f) * probeGridSize
                                );

                                // Calculate distance from camera to this bounding box center
                                float distance = Vector3.Distance(cameraPos, boundingBoxCenter);

                                // If this bounding box is closer, use it
                                if (distance < bestDistance)
                                {
                                    bestPosition = candidatePosition;
                                    bestDistance = distance;
                                    foundBetter = true;
                                }
                            }
                        }
                    }
                }

                // If we found a better position, and we're not at radius 0, we can stop
                if (foundBetter && radius > 0)
                    break;
            }

#if UNITY_EDITOR
            // Store the result for Gizmos visualization
            _lastClosestBoundingBoxCenter = transform.position + new Vector3(
                (bestPosition.x + CurrentVoxelGrid.X * 0.5f) * probeGridSize,
                (bestPosition.y + CurrentVoxelGrid.Y * 0.5f) * probeGridSize,
                (bestPosition.z + CurrentVoxelGrid.Z * 0.5f) * probeGridSize
            );
            _lastClosestBoundingBoxMin = bestPosition;
#endif

            return bestPosition;
        }

        /// <summary>
        /// Check if camera is inside the volume bounds
        /// </summary>
        private bool IsCameraInsideVolume(Vector3 cameraPos)
        {
            Vector3 volumeMin = transform.position;
            Vector3 volumeMax = transform.position + new Vector3(
                probeSizeX * probeGridSize,
                probeSizeY * probeGridSize,
                probeSizeZ * probeGridSize
            );

            return cameraPos.x >= volumeMin.x && cameraPos.x <= volumeMax.x &&
                   cameraPos.y >= volumeMin.y && cameraPos.y <= volumeMax.y &&
                   cameraPos.z >= volumeMin.z && cameraPos.z <= volumeMax.z;
        }

        /// <summary>
        /// Get intensity scale for a specific probe position
        /// </summary>
        /// <param name="probePosition">World position of the probe</param>
        /// <returns>Combined intensity scale for this probe</returns>
        private static float CalculateProbeIntensityScale(Vector3 probePosition)
        {
            float totalScale = 1f;
            var adjustmentVolumes = PRTVolumeManager.AdjustmentVolumes;
            for (int i = 0; i < adjustmentVolumes.Count; i++)
            {
                var volume = adjustmentVolumes[i];
                if (volume != null && volume.Contains(probePosition))
                {
                    float volumeScale = volume.GetIntensityScale();
                    totalScale *= volumeScale;
                }
            }

            return totalScale;
        }

        /// <summary>
        /// Check if a probe should be invalidated based on adjustment volumes
        /// </summary>
        /// <param name="probePosition">World position of the probe</param>
        /// <returns>True if probe should be invalidated</returns>
        private static bool ShouldInvalidateProbe(Vector3 probePosition)
        {
            var adjustmentVolumes = PRTVolumeManager.AdjustmentVolumes;
            for (int i = 0; i < adjustmentVolumes.Count; i++)
            {
                var volume = adjustmentVolumes[i];
                if (volume && volume.Contains(probePosition))
                {
                    if (volume.ShouldInvalidateProbe())
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Initialize validity data for all probes
        /// </summary>
        private void InitializeValidityData()
        {
            if (Probes == null || Probes.Length == 0)
                return;

            // Initialize validity states array (all valid by default)
            _validity = new float[Probes.Length];
            for (int i = 0; i < _validity.Length; i++)
            {
                _validity[i] = PackIntensityValidity(1f, 1f);
            }

            // Update validity based on adjustment volumes
            UpdateProbeValidityFromVolumes();
        }

        /// <summary>
        /// Update probe validity states based on adjustment volumes
        /// </summary>
        private void UpdateProbeValidityFromVolumes()
        {
            if (Probes == null || _validity == null)
                return;

            for (int i = 0; i < Probes.Length; i++)
            {
                Vector3 probePos = Probes[i].Position;
                float validity = ShouldInvalidateProbe(probePos) ? 0f : 1f;
                float intensity = CalculateProbeIntensityScale(probePos);

                // Pack intensity and validity together
                _validity[i] = PackIntensityValidity(intensity, validity);
            }
        }

        /// <summary>
        /// Get the full validity masks buffer
        /// </summary>
        public float[] GetValidityMasks()
        {
            return _validity;
        }

        /// <summary>
        /// Check if a probe is valid (not invalidated)
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <returns>True if probe is valid, false if invalidated</returns>
        public bool IsProbeValid(int probeIndex)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return !ShouldInvalidateProbe(Probes[probeIndex].Position);
            }
#endif

            // Unpack validity from packed data
            uint packedVal = IllusionRenderingUtils.AsUInt(_validity[probeIndex]);
            uint validityBits = (packedVal >> 24) & 0xFF;
            float validity = validityBits / 255f;

            return validity > 0.5f;
        }


        /// <summary>
        /// Pack intensity (0-5 range) and validity (0-1 range) into a single float for GPU upload
        /// </summary>
        private static float PackIntensityValidity(float intensity, float validity)
        {
            // Normalize intensity from [0, 5] to [0, 1]
            float normalizedIntensity = Mathf.Clamp01(intensity / 5.0f);

            // Pack into 32-bit uint: intensity (bits 0-23) + validity (bits 24-31)
            uint packedIntensity = (uint)(normalizedIntensity * 16777215f); // 2^24 - 1
            uint packedValidity = (uint)(Mathf.Clamp01(validity) * 255f) << 24; // 2^8 - 1, shifted to bits 24-31
            uint packedVal = packedIntensity | packedValidity;

            return IllusionRenderingUtils.AsFloat(packedVal);
        }
    }
}
