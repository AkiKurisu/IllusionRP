#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        [SerializeField, Min(16)] internal int bakeSampleCount = 2048;
        [SerializeField, Range(0.05f, 1f)] internal float surfelMergeDistance = 0.25f;
        [SerializeField] internal uint bakeSeed;
        private readonly Dictionary<Vector3, Vector3> _cachedVirtualOffsetPositions = new();
        private static readonly ProfilerMarker TransferMarker = new("PRT Bake Direction Transfer");

        internal async Task BakeDataAsync(IPRTBaker baker, CancellationToken token = default)
        {
            PRTProbeGrid grid = GetBakeGrid();
            Hash128 settings = GetBakeSettingsSignature();
            Hash128 authoringInputs = GetBakeAuthoringInputsSignature();
            var placement = await BakePlacementAsync(baker, token);
            Vector4[] directions = PRTBakeSampling.GenerateDirections(bakeSampleCount, bakeSeed);
            var partition = new PRTSectorBake(grid, sectorWidth, surfelMergeDistance);
            int batchSize = Math.Max(1, (1 << 19) / directions.Length);
            for (int start = 0; start < grid.ProbeCount; start += batchSize)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(batchSize, grid.ProbeCount - start);
                var positions = new Vector3[count];
                for (int i = 0; i < count; i++) positions[i] = grid.GetPosition(start + i) + placement[start + i].offset;
                baker.UpdateProgress($"Capture probes {start + 1}–{start + count}/{grid.ProbeCount}", 0.1f + 0.8f * start / grid.ProbeCount);
                PRTProbeBakeSamples[] captures = await baker.CaptureProbesAsync(positions, directions, token);
                using var scope = TransferMarker.Auto();
                var offsets = new Vector3[count];
                var validity = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    offsets[i] = captures[i].capturePosition - grid.GetPosition(start + i);
                    validity[i] = PRTProbeValidity.Pack(1f, placement[start + i].valid ? 1f : 0f);
                }
                partition.AddProbes(start, captures, directions, offsets, validity);
            }
            token.ThrowIfCancellationRequested();
            if (!grid.Equals(GetBakeGrid()) || authoringInputs != GetBakeAuthoringInputsSignature())
                throw new InvalidOperationException("Probe bake settings changed during capture; the asset was not replaced.");
            baker.UpdateProgress("Serialize direction transfer", 0.95f);
            var data = partition.Complete();
            var signature = new PRTBakeSignature { geometry = baker.GeometrySignature, materials = baker.MaterialSignature,
                settings = settings, backend = baker.BackendName, sampleCount = directions.Length, seed = bakeSeed,
                sceneTime = baker.SceneTime, authoringInputs = authoringInputs };
            asset.SetBakedData(grid, signature, baker.GeometryBounds, sectorWidth, partition.Metadata, data);
        }

        internal async Task<PRTProbePlacement[]> BakePlacementAsync(IPRTBaker baker, CancellationToken token)
        {
            PRTProbeGrid grid = GetBakeGrid();
            var result = new PRTProbePlacement[grid.ProbeCount];
            PRTProbeAdjustmentVolume[] adjustmentVolumes = GetPlacementVolumes().ToArray();
            _cachedVirtualOffsetPositions.Clear();
            var offsets = new Vector3[result.Length];
            var positions = new Vector3[result.Length];
            var biases = new Vector2[result.Length];
            for (int i = 0; i < result.Length; i++)
            {
                Vector3 nominal = grid.GetPosition(i);
                Vector2 bias = new(geometryBias, rayOriginBias);
                if (enableBakePreprocess)
                {
                    offsets[i] = virtualOffset;
                    foreach (PRTProbeAdjustmentVolume volume in adjustmentVolumes)
                    {
                        if (!volume || !volume.Contains(nominal)) continue;
                        if (volume.mode == PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings)
                            bias = new Vector2(volume.geometryBias, volume.rayOriginBias);
                        else offsets[i] += volume.GetAdditionalVirtualOffset();
                    }
                }
                positions[i] = nominal + offsets[i];
                biases[i] = bias;
            }
            PRTProbePlacement[] adjusted = null;
            if (enableBakePreprocess)
            {
                baker.UpdateProgress("Place probes", 0f);
                adjusted = await baker.PlaceProbesAsync(positions, biases, probeGridSize, token);
            }
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = adjusted == null ? new PRTProbePlacement(offsets[i], true)
                    : new PRTProbePlacement(offsets[i] + adjusted[i].offset, adjusted[i].valid);
                _cachedVirtualOffsetPositions[grid.GetPosition(i)] = result[i].offset;
            }
            return result;
        }

        private PRTProbeGrid GetBakeGrid()
        {
            if (transform.rotation != Quaternion.identity || transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("PRT probe volumes require an unrotated, unscaled world-space grid.");
            return new PRTProbeGrid { origin = transform.position, min = Vector3Int.zero,
                count = new Vector3Int(probeSizeX, probeSizeY, probeSizeZ), spacing = probeGridSize };
        }
        private Vector3 CalculateProbeVirtualOffset(Vector3 nominal)
        {
            if (_cachedVirtualOffsetPositions.TryGetValue(nominal, out Vector3 offset)) return offset;
            if (!asset || !asset.HasValidData) return Vector3.zero;
            PRTProbeGrid grid = asset.Grid;
            Vector3 coordinate = (nominal - grid.origin) / grid.spacing - (Vector3)grid.min;
            var c = new Vector3Int(Mathf.RoundToInt(coordinate.x), Mathf.RoundToInt(coordinate.y), Mathf.RoundToInt(coordinate.z));
            if (c.x < 0 || c.y < 0 || c.z < 0 || c.x >= grid.count.x || c.y >= grid.count.y || c.z >= grid.count.z)
                return Vector3.zero;
            return asset.Probes[c.x * grid.count.y * grid.count.z + c.y * grid.count.z + c.z].captureOffset;
        }
        internal Hash128 GetBakeSettingsSignature()
        {
            Hash128 hash = default;
            hash.Append(enableBakePreprocess ? 1 : 0);
            AppendVector(ref hash, virtualOffset);
            hash.Append(geometryBias); hash.Append(rayOriginBias);
            hash.Append(bakeSampleCount); hash.Append(unchecked((int)bakeSeed));
            hash.Append(sectorWidth);
            hash.Append(SurfelGrid.DefaultBrickSize); hash.Append(surfelMergeDistance);
            hash.Append(Surfel.Stride); hash.Append(BrickFactor.Stride);
            foreach (PRTProbeAdjustmentVolume volume in GetPlacementVolumes())
            {
                if (!volume || volume.mode is PRTProbeAdjustmentMode.IntensityScale or PRTProbeAdjustmentMode.InvalidateProbes) continue;
                hash.Append((int)volume.mode); hash.Append((int)volume.shape);
                for (int i = 0; i < 16; i++) hash.Append(volume.transform.localToWorldMatrix[i]);
                AppendVector(ref hash, volume.size);
                hash.Append(volume.radius); hash.Append(volume.geometryBias); hash.Append(volume.rayOriginBias);
                hash.Append(volume.virtualOffsetRotation); hash.Append(volume.virtualOffsetDistance);
            }
            return hash;
        }
        internal Hash128 GetBakeAuthoringInputsSignature()
        {
            Hash128 hash = GetBakeSettingsSignature();
            foreach (PRTProbeAdjustmentVolume volume in PRTVolumeManager.AdjustmentVolumes.Where(v => v && v.isActiveAndEnabled)
                .OrderBy(v => GlobalObjectId.GetGlobalObjectIdSlow(v).ToString(), StringComparer.Ordinal))
            {
                hash.Append(GlobalObjectId.GetGlobalObjectIdSlow(volume).ToString());
                hash.Append((int)volume.mode); hash.Append((int)volume.shape);
                for (int i = 0; i < 16; i++) hash.Append(volume.transform.localToWorldMatrix[i]);
                AppendVector(ref hash, volume.size);
                hash.Append(volume.radius); hash.Append(volume.intensityScale);
                hash.Append(volume.geometryBias); hash.Append(volume.rayOriginBias);
                hash.Append(volume.virtualOffsetRotation); hash.Append(volume.virtualOffsetDistance);
            }
            return hash;
        }
        private static IEnumerable<PRTProbeAdjustmentVolume> GetPlacementVolumes() =>
            PRTVolumeManager.AdjustmentVolumes.Where(v => v &&
                v.mode is PRTProbeAdjustmentMode.ApplyVirtualOffset or PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings)
                .OrderBy(v => GlobalObjectId.GetGlobalObjectIdSlow(v).ToString(), StringComparer.Ordinal);
        private static void AppendVector(ref Hash128 hash, Vector3 value)
        { hash.Append(value.x); hash.Append(value.y); hash.Append(value.z); }
        internal void ReloadBakedData()
        {
            AllocateProbes();
            TryLoadAsset(asset);
            PRTVolumeManager.RegisterProbeVolume(this);
        }
        internal void ClearBakedData()
        {
            asset.Clear();
            ReleaseRuntimeData();
        }
    }
}
#endif
