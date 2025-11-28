#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        // Cache for ray-traced virtual offset positions
        private readonly Dictionary<Vector3, Vector3> _cachedVirtualOffsetPositions = new();
        
        private float _cachedGeometryBias;

        private float _cachedRayOriginBias;
        
        /// <summary>
        /// Precompute surfel and bake into <see cref="PRTProbeVolumeAsset"/> using PRTBaker
        /// </summary>
        /// <param name="prtBaker">PRTBaker instance to use for baking</param>
        /// <param name="cancellationToken"></param>
        internal async Task BakeDataAsync(IPRTBaker prtBaker, CancellationToken cancellationToken = default)
        {
            if (!Probes.Any())
            {
                AllocateProbes();
            }

            // Force update to hide debug spheres
            foreach (var probe in Probes)
            {
                probe.UpdateVisibility();
            }
            
            var surfelGrid = new SurfelGrid();

            // Bake probes virtual offset
            BakeProbeVirtualOffset();

            // Capture surfels for each probe
            for (int i = 0; i < Probes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var probe = Probes[i];
                float progress = (float)i / Probes.Length;
                prtBaker.UpdateProgress($"Sampling surfels for probe {i + 1}/{Probes.Length} at {probe.Position}", progress);
                
                Vector3 probeOffset = CalculateProbeVirtualOffset(probe.Position);
                var surfels = prtBaker.BakeSurfelData(probeOffset + probe.Position);
                // Add surfels to grid and mark the probe reference
                foreach (var surfel in surfels)
                {
                    surfelGrid.AddSurfel(surfel, probe);
                }

                // Periodically force garbage collection to prevent memory buildup
                if ((i + 1) % 20 == 0)
                {
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                }

                EditorApplication.QueuePlayerLoopUpdate();
                await Task.Delay(1, cancellationToken);
            }

            asset.CellData = surfelGrid.GenerateCell(Probes);
        }

        /// <summary>
        /// Force to reload bake data in editor
        /// </summary>
        internal void ReloadBakedData()
        {
            AllocateProbes();
            TryLoadAsset(asset);
            PRTVolumeManager.RegisterProbeVolume(this);
        }
        
        /// <summary>
        /// Clear <see cref="PRTProbeVolumeAsset"/> data
        /// </summary>
        internal void ClearBakedData()
        {
            asset.Clear();
            ReleaseProbes();
            _coefficientVoxelRT?.Release();
            _coefficientVoxelRT = null;
            _globalSurfelBuffer?.Release();
            _globalSurfelBuffer = null;
            _isDataInitialized = false;
        }
        
        /// <summary>
        /// Bake probes virtual offset position cache
        /// </summary>
        internal void BakeProbeVirtualOffset()
        {
            _cachedVirtualOffsetPositions.Clear();
            foreach (var probe in Probes)
            {
                // Calculate per-probe virtual offset
                CalculateProbeVirtualOffset(probe.Position);
            }
        }
        
        /// <summary>
        /// Calculate virtual offset for a specific probe position
        /// </summary>
        /// <param name="probePosition">World position of the probe</param>
        /// <returns>Combined virtual offset for this probe</returns>
        private Vector3 CalculateProbeVirtualOffset(Vector3 probePosition)
        {
            if (!enableBakePreprocess) return Vector3.zero;
            Vector3 totalOffset = virtualOffset; // Start with global offset

            float geomBias = geometryBias;
            float rayBias = rayOriginBias;

            var adjustmentVolumes = PRTVolumeManager.AdjustmentVolumes;
            for (int i = 0; i < adjustmentVolumes.Count; i++)
            {
                var volume = adjustmentVolumes[i];
                if (volume && volume.Contains(probePosition))
                {
                    if (volume.mode == PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings)
                    {
                        geomBias = volume.geometryBias;
                        rayBias = volume.rayOriginBias;
                    }
                    else
                    {
                        Vector3 volumeOffset = volume.GetAdditionalVirtualOffset();
                        totalOffset += volumeOffset;
                    }
                }
            }
            
            // Calculate or use cached ray-traced virtual offset position
            Vector3 rayTracedPosition = CalculateVirtualOffsetPosition(probePosition + totalOffset, geomBias, rayBias);
            return rayTracedPosition - probePosition;
        }

        /// <summary>
        /// Calculate virtual offset position using cpu ray tracing
        /// </summary>
        /// <param name="probePosition">World position of the probe</param>
        /// <param name="inGeometryBias"></param>
        /// <param name="inRayOriginBias"></param>
        /// <returns>Virtual offset position for this probe</returns>
        private Vector3 CalculateVirtualOffsetPosition(Vector3 probePosition, float inGeometryBias, float inRayOriginBias)
        {
            // Check cache first
            if (_cachedVirtualOffsetPositions.TryGetValue(probePosition, out Vector3 cachedPosition))
            {
                return cachedPosition;
            }

            // Calculate and cache the result
            Vector3 rayTracedPosition = CalculateRayTracedVirtualOffsetPosition(probePosition, inGeometryBias, inRayOriginBias, probeGridSize);
            _cachedVirtualOffsetPositions[probePosition] = rayTracedPosition;
            return rayTracedPosition;
        }

        /// <summary>
        /// Calculate ray-traced virtual offset position
        /// </summary>
        /// <param name="probePosition">Original probe position</param>
        /// <param name="geometryBias"></param>
        /// <param name="rayOriginBias"></param>
        /// <param name="searchDistance"></param>
        /// <returns>Ray-traced virtual offset position</returns>
        private static Vector3 CalculateRayTracedVirtualOffsetPosition(Vector3 probePosition, 
            float geometryBias, float rayOriginBias, float searchDistance)
        {
            const float DISTANCE_THRESHOLD = 5e-5f;
            const float DOT_THRESHOLD = 1e-2f;
            const float VALIDITY_THRESHOLD = 0.5f; // 50% backface threshold

            Vector3[] sampleDirections = GetSampleDirections();
            Vector3 bestDirection = Vector3.zero;
            float maxDotSurface = -1f;
            float minDistance = float.MaxValue;
            int validHits = 0;

            foreach (Vector3 direction in sampleDirections)
            {
                Vector3 rayOrigin = probePosition + direction * rayOriginBias;
                Vector3 rayDirection = direction;

                // Cast ray to find geometry intersection
                if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, searchDistance))
                {
                    // Skip front faces
                    if (hit.triangleIndex >= 0) // Check if it's a valid hit
                    {
                        // Check if it's a back face by checking normal direction
                        Vector3 hitNormal = hit.normal;
                        float dotSurface = Vector3.Dot(rayDirection, hitNormal);

                        // If it's a front face, skip it
                        if (dotSurface > 0)
                        {
                            validHits++;
                            continue;
                        }

                        float distanceDiff = hit.distance - minDistance;

                        // If distance is within threshold
                        if (distanceDiff < DISTANCE_THRESHOLD)
                        {
                            // If new distance is smaller by at least threshold, or if ray is more colinear with normal
                            if (distanceDiff < -DISTANCE_THRESHOLD || dotSurface - maxDotSurface > DOT_THRESHOLD)
                            {
                                bestDirection = rayDirection;
                                maxDotSurface = dotSurface;
                                minDistance = hit.distance;
                            }
                        }
                    }
                }
            }

            // Calculate validity (percentage of backfaces seen)
            float validity = 1.0f - validHits / (sampleDirections.Length - 1.0f);

            // Disable VO for probes that don't see enough backface
            if (validity <= VALIDITY_THRESHOLD)
                return probePosition;

            if (minDistance == float.MaxValue)
                minDistance = 0f;

            // Calculate final offset position
            float offsetDistance = minDistance * 1.05f + geometryBias;
            return probePosition + bestDirection * offsetDistance;
        }

        /// <summary>
        /// Get sample directions for ray tracing
        /// </summary>
        /// <returns>Array of normalized direction vectors</returns>
        private static Vector3[] GetSampleDirections()
        {
            // 3x3x3 - 1, excluding center
            const float k0 = 0f, k1 = 1f, k2 = 0.70710678118654752440084436210485f, k3 = 0.57735026918962576450914878050196f;

            return new Vector3[]
            {
                // Top layer (y = +1)
                new(-k3, +k3, -k3), // -1  1 -1
                new( k0, +k2, -k2), //  0  1 -1
                new(+k3, +k3, -k3), //  1  1 -1
                new(-k2, +k2,  k0), // -1  1  0
                new( k0, +k1,  k0), //  0  1  0
                new(+k2, +k2,  k0), //  1  1  0
                new(-k3, +k3, +k3), // -1  1  1
                new( k0, +k2, +k2), //  0  1  1
                new(+k3, +k3, +k3), //  1  1  1

                // Middle layer (y = 0)
                new(-k2,  k0, -k2), // -1  0 -1
                new( k0,  k0, -k1), //  0  0 -1
                new(+k2,  k0, -k2), //  1  0 -1
                new(-k1,  k0,  k0), // -1  0  0
                // k0, k0, k0 - skip center position (which would be a zero-length ray)
                new(+k1,  k0,  k0), //  1  0  0
                new(-k2,  k0, +k2), // -1  0  1
                new( k0,  k0, +k1), //  0  0  1
                new(+k2,  k0, +k2), //  1  0  1

                // Bottom layer (y = -1)
                new(-k3, -k3, -k3), // -1 -1 -1
                new( k0, -k2, -k2), //  0 -1 -1
                new(+k3, -k3, -k3), //  1 -1 -1
                new(-k2, -k2,  k0), // -1 -1  0
                new( k0, -k1,  k0), //  0 -1  0
                new(+k2, -k2,  k0), //  1 -1  0
                new(-k3, -k3, +k3), // -1 -1  1
                new( k0, -k2, +k2), //  0 -1  1
                new(+k3, -k3, +k3), //  1 -1  1
            };
        }
    }
}
#endif