using System;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        private int _assetStateHash;
        private bool _hasAssetState;

        private void Start() => EnsureRuntimeData();

        private void OnEnable()
        {
            IsFeatureEnabled = IllusionRenderingUtils.GetPrecomputedRadianceTransferFeatureEnabled();
            PRTVolumeManager.RegisterProbeVolume(this);
            EnsureRuntimeData();
#if UNITY_EDITOR
            EnableRadianceDebug();
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            DisableRadianceDebug();
#endif
            PRTVolumeManager.UnregisterProbeVolume(this);
            ReleaseRuntimeData();
            _hasAssetState = false;
        }

        private void OnDestroy() => ReleaseRuntimeData();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!gameObject.scene.IsValid())
                return;
            _cachedVirtualOffsetPositions.Clear();
            EnsureRuntimeData();
        }
#endif

        private void Update()
        {
            if (!gameObject.scene.IsValid())
                return;
            IsFeatureEnabled = IllusionRenderingUtils.GetPrecomputedRadianceTransferFeatureEnabled();
            EnsureRuntimeData();
        }

        internal void EnsureRuntimeData()
        {
            if (!gameObject.scene.IsValid())
                return;
            var grid = Layout;
            int state = HashCode.Combine(asset ? asset.GetInstanceID() : 0, asset ? asset.Sectors : null,
                grid.origin, grid.count, grid.spacing, transform.rotation, transform.lossyScale);
#if UNITY_EDITOR
            state = HashCode.Combine(state, GetBakeSettingsSignature());
#endif
            if (_hasAssetState && state == _assetStateHash)
            {
                RefreshRuntimeValidity();
                return;
            }
            ReleaseRuntimeData();
            _hasAssetState = true;
            _assetStateHash = state;
            if (grid.count.x <= 0 || grid.count.y <= 0 || grid.count.z <= 0 || !float.IsFinite(grid.spacing) || grid.spacing <= 0)
            {
                AssetInvalidReason = "Probe grid dimensions and spacing must be positive and finite.";
                return;
            }
            AllocateProbes();
            TryLoadAsset(asset);
        }

        public bool IsActivate()
        {
            EnsureRuntimeData();
            return isActiveAndEnabled && _isDataInitialized;
        }

        private void TryLoadAsset(PRTProbeVolumeAsset volumeAsset)
        {
            _isDataInitialized = false;
            if (!volumeAsset)
            {
                AssetInvalidReason = "Assign a PRT volume asset and bake its transport.";
                return;
            }
            if (!volumeAsset.TryValidate(out string reason))
            {
                AssetInvalidReason = reason;
                return;
            }
            if (!volumeAsset.Grid.Equals(Layout))
            {
                AssetInvalidReason = "Probe placement changed; rebake the transport asset.";
                return;
            }
            if (Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.001f ||
                (transform.lossyScale - Vector3.one).sqrMagnitude > 1e-8f)
            {
                AssetInvalidReason = "World-space PRT transport requires identity volume rotation and scale; rebake with the intended origin.";
                return;
            }
#if UNITY_EDITOR
            if (volumeAsset.Signature.settings != GetBakeSettingsSignature())
            {
                AssetInvalidReason = "Capture settings changed. Rebake the PRT transport asset.";
                return;
            }
#endif
            _allProbes = volumeAsset.Probes;
            _isDataInitialized = true;
            AssetInvalidReason = null;
            _hasMetadataState = false;
            RefreshRuntimeValidity();
        }

        private void ReleaseRuntimeData()
        {
            RuntimeDataId++;
            ClearShadowCacheGlobalStats();
            ReleaseProbes();
#if UNITY_EDITOR
            _radianceSolver = null;
#endif
            _coefficientVoxelRT = null;
            _allProbes = null;
            _isDataInitialized = false;
            PublishedGeneration = 0;
            UpdatedSectors = ResidentSectors = UploadingSectors = EvictingSectors = FrameUploadBytes = SolverNonFiniteCount = 0;
            ResidentBytes = PeakResidentBytes = FixedGpuBytes = 0;
            ResidencyPressure = null;
            SectorUpdateFrames = null;
            SolverResidual = SolverAbsoluteResidual = float.PositiveInfinity;
        }

        private void ReleaseProbes()
        {
            Probes = null;
            _probesInBoundingBox.Clear();
            _mainCamera = null;
            _currentBoundingBox = default;
            CascadeBounds = Array.Empty<Bounds>();
#if UNITY_EDITOR
            _cachedVirtualOffsetPositions.Clear();
            ReleaseProbeDebugData();
#endif
        }

#if UNITY_EDITOR
        private void ReleaseProbeDebugData()
        {
            ClearShadowCacheDebugSnapshot();
            if (_probeDebugData == null)
                return;
            foreach (var data in _probeDebugData)
                data?.Dispose();
            _probeDebugData = null;
        }
#endif

        private Grid CalculateVoxelGrid() => new(
            Mathf.Clamp(voxelProbeSize.x, 1, probeSizeX), Mathf.Clamp(voxelProbeSize.y, 1, probeSizeY),
            Mathf.Clamp(voxelProbeSize.z, 1, probeSizeZ), probeGridSize);

        private void AllocateProbes()
        {
            ReleaseProbes();
            CurrentVoxelGrid = CalculateVoxelGrid();
            Probes = new PRTProbe[probeSizeX * probeSizeY * probeSizeZ];
#if UNITY_EDITOR
            _probeDebugData = new PRTProbeDebugData[Probes.Length];
#endif
            for (int x = 0; x < probeSizeX; x++)
                for (int y = 0; y < probeSizeY; y++)
                    for (int z = 0; z < probeSizeZ; z++)
                    {
                        int index = x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
                        Probes[index] = new PRTProbe(index, new Vector3(x, y, z) * probeGridSize, this);
                    }
            InitializeValidityData();
        }
    }
}
