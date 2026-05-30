#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal class PRTProbeDebugData : IDisposable
    {
        public static readonly int[] CoefficientClearValue;
        
        static PRTProbeDebugData()
        {
            CoefficientClearValue = new int[27];
            for (int i = 0; i < 27; i++)
            {
                CoefficientClearValue[i] = 0;
            }
        }
        
        // Local surfel array for this probe
        public Surfel[] LocalSurfels { get; private set; }

        public int[] LocalSurfelIndices { get; private set; }

        public ComputeBuffer CoefficientSH9 { get; private set; } // GPU side SH9 coefficient, size: 9x3=27

        public PRTProbeDebugData(FactorIndices factorIndices, 
            BrickFactor[] factors, SurfelIndices[] brickIndices, Surfel[] allSurfels)
        {
            // Build surfel list from factors
            var surfelList = new List<Surfel>();
            var surfelIndexList = new List<int>();
        
            // Iterate through all factors for this probe
            for (int factorIndex = factorIndices.start; factorIndex <= factorIndices.end; factorIndex++)
            {
                var factor = factors[factorIndex];
                var brickIndex = factor.brickIndex;
                var indices = brickIndices[brickIndex];
            
                // Add all surfels from this brick
                for (int surfelIndex = indices.start; surfelIndex <= indices.start + indices.count - 1; surfelIndex++)
                {
                    surfelList.Add(allSurfels[surfelIndex]);
                    surfelIndexList.Add(surfelIndex);
                }
            }
        
            // Resize local surfel array to match actual count
            int surfelCount = surfelList.Count;
            LocalSurfels = new Surfel[surfelCount];
            LocalSurfelIndices = new int[surfelCount];
        
            // Copy all surfels (no truncation)
            for (int i = 0; i < surfelCount; i++)
            {
                LocalSurfels[i] = surfelList[i];
                LocalSurfelIndices[i] = surfelIndexList[i];
            }
            
            CoefficientSH9 = new ComputeBuffer(27, sizeof(float));
            CoefficientSH9.SetData(CoefficientClearValue);
        }

        public void Dispose()
        {
            LocalSurfels = null;
            LocalSurfelIndices = null;
            CoefficientSH9?.Release();
            CoefficientSH9 = null;
        }
    }
    
    public partial class PRTProbeVolume
    {
        [SerializeField, HideInInspector] 
        internal ProbeVolumeDebugMode debugMode;

        [SerializeField, HideInInspector] 
        internal int selectedProbeIndex = -1;
        
        [SerializeField, HideInInspector] 
        internal ProbeDebugMode selectedProbeDebugMode = ProbeDebugMode.IrradianceSphere;
        
        [Range(0.01f, 10.0f), SerializeField, HideInInspector] 
        internal float probeHandleSize = 1.0f;

        [Min(1), SerializeField, HideInInspector]
        internal int shadowCacheDebugReadbackInterval = 10;

        [SerializeField, HideInInspector]
        internal bool shadowCacheDebugShowLabels = true;

        [Range(0.005f, 1.0f), SerializeField, HideInInspector]
        internal float shadowCacheDebugSurfelSize = 0.05f;
        
        [SerializeField, HideInInspector] 
        internal PRTBakeResolution bakeResolution = PRTBakeResolution._256;

        private PRTProbeDebugData[] _probeDebugData;
        
        // Debug visualization settings
        private const float SphereSize = 0.025f;
        
        // Debug colors
        private static readonly Color DefaultColor = Color.yellow;

        private static readonly Color SkyColor = Color.blue;

        private static readonly Color NormalColor = Color.green;
        
        private const float SurfelSize = 0.05f;

        private const float NormalLength = 0.25f;

        private const float SkyRayLength = 25.0f;

        private const float SkyMaskThreshold = 0.9f;

        private const int MaxShadowCacheBrickLabels = 8;
                
        private Vector3? _lastClosestBoundingBoxCenter;

        private Vector3Int? _lastClosestBoundingBoxMin;
        
        private void OnDrawGizmos()
        {
            if (!gameObject.scene.IsValid()) return;
            if (!IsFeatureEnabled) return;

            if (!IsProbeValid())
            {
                return;
            }
            
            if (debugMode == ProbeVolumeDebugMode.None)
                return;

            if (_mainCamera)
            {
                // Draw camera position and local probes
                DrawCameraAndLocalProbes();
                if (IsCameraInsideVolume(_mainCamera.transform.position))
                {
                    // Draw bounding box
                    DrawBoundingBox();
                }
                else
                {
                    // Draw the closest bounding box result
                    DrawClosestBoundingBoxResult();
                }
            }

            for (int i = 0; i < Probes.Length; i++)
            {
                if (Probes[i] == null)
                    continue;

                Vector3 probePos = Probes[i].Position;

                // Draw probe index
                if (debugMode != ProbeVolumeDebugMode.ShadowCache)
                {
                    DrawProbeIndex(i, probePos);
                }

                // Draw debug visualization based on mode
                if (debugMode == ProbeVolumeDebugMode.ProbeGrid)
                {
                    DrawProbeGrid(i, probePos);
                }
                else if (debugMode == ProbeVolumeDebugMode.ProbeGridWithVirtualOffset)
                {
                    DrawProbeGridWithVirtualOffset(i, probePos);
                }
                else if (debugMode == ProbeVolumeDebugMode.ProbeRadiance)
                {
                    DrawProbeRadiance(i, probePos);
                }
                else if (debugMode == ProbeVolumeDebugMode.ShadowCache)
                {
                    DrawShadowCacheProbe(i, probePos);
                }
            }

            if (debugMode == ProbeVolumeDebugMode.ShadowCache)
            {
                DrawShadowCacheVolumeSummary();
            }

            if (selectedProbeIndex >= Probes.Length)
            {
                selectedProbeIndex = -1;
            }

            if (selectedProbeIndex != -1)
            {
                var probe = Probes[selectedProbeIndex];
                var probeDebugData = GetProbeDebugData(selectedProbeIndex);
                var probePos = probe.Position + virtualOffset;

                if (debugMode == ProbeVolumeDebugMode.ShadowCache)
                {
                    DrawSelectedProbeShadowCache(probe, probePos, probeDebugData);
                    return;
                }

                if (debugMode != ProbeVolumeDebugMode.ProbeRadiance) return;
                
                if (selectedProbeDebugMode == ProbeDebugMode.SurfelBrickGrid)
                {
                    DrawProbeSurfelBrickGrid(probe, probePos);
                }
                else if (selectedProbeDebugMode != ProbeDebugMode.IrradianceSphere)
                {
                    DrawDebugVisualization(selectedProbeDebugMode, probePos, probeDebugData);
                }
            }
        }

        internal PRTProbeDebugData GetProbeDebugData(int index)
        {
            return _probeDebugData[index];
        }

        /// <summary>
        /// Draw camera position and local probes for debugging
        /// </summary>
        private void DrawCameraAndLocalProbes()
        {
            if (!_mainCamera || _localProbeIndices == null)
                return;

            // Draw camera position with red color
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_mainCamera.transform.position, 0.5f);
            
            // Draw camera label
            Handles.Label(_mainCamera.transform.position + Vector3.up * 0.8f, "Camera");
            
            // Draw line from camera to probe with yellow color
            Gizmos.color = Color.yellow;
            foreach (int probeIndex in _localProbeIndices)
            {
                if (probeIndex >= 0 && probeIndex < Probes.Length)
                {
                    Vector3 probePos = Probes[probeIndex].Position;
                    Gizmos.DrawLine(_mainCamera.transform.position, probePos);
                }
            }
        }

        /// <summary>
        /// Draw probe index in scene view
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private static void DrawProbeIndex(int probeIndex, Vector3 probePos)
        {
            // Draw probe index label
            Handles.Label(probePos + Vector3.up * 0.5f, probeIndex.ToString());
        }

        /// <summary>
        /// Draw probe grid visualization
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeGrid(int probeIndex, Vector3 probePos)
        {
            // Check if probe is invalidated, if so render as black
            if (!IsProbeValid(probeIndex))
            {
                Gizmos.color = Color.black;
            }
            else
            {
                Gizmos.color = Color.cyan;
            }
            
            Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.1f);

            // Draw connections to neighboring probes
            DrawProbeConnections(probeIndex, probePos);
        }

        /// <summary>
        /// Draw probe grid visualization with virtual offset applied
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeGridWithVirtualOffset(int probeIndex, Vector3 probePos)
        {
            // Draw original probe position in cyan
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.1f);

            // Calculate per-probe virtual offset (includes adjustment volumes)
            Vector3 totalVirtualOffset = CalculateProbeVirtualOffset(probePos);
            Vector3 virtualOffsetPos = probePos + totalVirtualOffset;
            
            // Draw virtual offset probe position in magenta
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(virtualOffsetPos, probeHandleSize * 0.1f);

            // Draw line from original to virtual offset position
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(probePos, virtualOffsetPos);

            // Draw arrow to show offset direction
            DrawOffsetArrow(probePos, virtualOffsetPos);

            // Draw connections to neighboring probes at virtual offset positions
            DrawVirtualOffsetProbeConnections(probeIndex, virtualOffsetPos);

            // Draw adjustment volume effects
            DrawAdjustmentVolumeEffects(probePos, totalVirtualOffset);
        }

        /// <summary>
        /// Draw probe radiance visualization
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeRadiance(int probeIndex, Vector3 probePos)
        {
            // This would show radiance data if available
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(probePos, probeHandleSize * 0.08f);
        }

        private void DrawShadowCacheProbe(int probeIndex, Vector3 probePos)
        {
            if (!HasShadowCacheDebugSnapshot ||
                LatestShadowCacheProbeSummaries == null ||
                probeIndex < 0 ||
                probeIndex >= LatestShadowCacheProbeSummaries.Length ||
                !LatestShadowCacheProbeSummaries[probeIndex].valid)
            {
                Gizmos.color = Color.gray;
                Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.08f);
                return;
            }

            var summary = LatestShadowCacheProbeSummaries[probeIndex];
            Gizmos.color = GetShadowCacheStatusColor(summary.status, summary.meanShadow);
            float radius = probeHandleSize * Mathf.Lerp(0.06f, 0.14f, 1f - Mathf.Clamp01(summary.meanShadow));
            Gizmos.DrawSphere(probePos, radius);

            if (probeIndex == selectedProbeIndex)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.22f);
            }
        }

        private void DrawShadowCacheVolumeSummary()
        {
            if (!shadowCacheDebugShowLabels ||
                !HasShadowCacheDebugSnapshot ||
                LatestShadowCacheProbeSummaries == null)
            {
                return;
            }

            int validProbeCount = 0;
            uint sampledCount = 0;
            uint fallbackCount = 0;
            uint hitCount = 0;
            uint uncoveredCount = 0;
            uint unknownCount = 0;

            for (int i = 0; i < LatestShadowCacheProbeSummaries.Length; i++)
            {
                var summary = LatestShadowCacheProbeSummaries[i];
                if (!summary.valid)
                {
                    continue;
                }

                validProbeCount++;
                sampledCount += summary.sampledCount;
                fallbackCount += summary.fallbackCount;
                hitCount += summary.freshHitCount + summary.brickHitCount;
                uncoveredCount += summary.uncoveredCount;
                unknownCount += summary.unknownCount;
            }

            Handles.Label(GetShadowCacheSummaryLabelPosition(),
                $"ShadowCache Frame:{LatestShadowCacheDebugFrameIndex} Epoch:{LatestShadowCacheDebugEpoch}\n" +
                $"Probes:{validProbeCount}/{LatestShadowCacheProbeSummaries.Length} " +
                $"S:{sampledCount} F:{fallbackCount} H:{hitCount} U:{uncoveredCount} ?:{unknownCount}");
        }

        private Vector3 GetShadowCacheSummaryLabelPosition()
        {
            if (_currentBoundingBox.size != Vector3.zero)
            {
                return _currentBoundingBox.center +
                       Vector3.up * (_currentBoundingBox.extents.y + Mathf.Max(probeGridSize, 1f) * 0.5f);
            }

            return transform.position + Vector3.up * Mathf.Max(probeGridSize, 1f);
        }

        private void DrawSelectedProbeShadowCache(PRTProbe probe, Vector3 probePos, PRTProbeDebugData debugData)
        {
            if (!HasShadowCacheDebugSnapshot || debugData == null)
            {
                Handles.Label(probePos + Vector3.up * probeHandleSize * 0.8f,
                    "No shadow cache snapshot");
                return;
            }

            DrawSelectedProbeShadowCacheSummary(probe.Index, probePos);
            DrawSelectedProbeShadowCacheSurfels(debugData);
            DrawSelectedProbeShadowCacheBricks(probe, probePos);
        }

        private void DrawSelectedProbeShadowCacheSummary(int probeIndex, Vector3 probePos)
        {
            if (!shadowCacheDebugShowLabels ||
                LatestShadowCacheProbeSummaries == null ||
                probeIndex < 0 ||
                probeIndex >= LatestShadowCacheProbeSummaries.Length)
            {
                return;
            }

            var summary = LatestShadowCacheProbeSummaries[probeIndex];
            uint hitCount = summary.freshHitCount + summary.brickHitCount;
            Handles.Label(probePos + Vector3.up * probeHandleSize * 0.85f,
                $"ShadowCache F:{LatestShadowCacheDebugFrameIndex} E:{LatestShadowCacheDebugEpoch} " +
                $"Avg:{summary.meanShadow:0.00} S:{summary.sampledCount} F:{summary.fallbackCount} H:{hitCount} U:{summary.uncoveredCount}");
        }

        private void DrawSelectedProbeShadowCacheSurfels(PRTProbeDebugData debugData)
        {
            if (LatestShadowCacheDebugEntries == null ||
                debugData.LocalSurfels == null ||
                debugData.LocalSurfelIndices == null)
            {
                return;
            }

            float surfelSize = Mathf.Max(0.001f, shadowCacheDebugSurfelSize);
            int count = Mathf.Min(debugData.LocalSurfels.Length, debugData.LocalSurfelIndices.Length);
            for (int i = 0; i < count; i++)
            {
                int surfelIndex = debugData.LocalSurfelIndices[i];
                if (surfelIndex < 0 || surfelIndex >= LatestShadowCacheDebugEntries.Length)
                {
                    Gizmos.color = GetShadowCacheStatusColor(ShadowCacheDebugStatus.Unknown, 1f);
                    Gizmos.DrawWireSphere(debugData.LocalSurfels[i].position, surfelSize);
                    continue;
                }

                var entry = LatestShadowCacheDebugEntries[surfelIndex];
                var status = (ShadowCacheDebugStatus)entry.status;
                Gizmos.color = GetShadowCacheStatusColor(status, entry.shadow);
                Gizmos.DrawSphere(debugData.LocalSurfels[i].position, surfelSize);
            }
        }

        private void DrawSelectedProbeShadowCacheBricks(PRTProbe probe, Vector3 probePos)
        {
            if (!asset || !_isDataInitialized || LatestBrickShadowCacheDebugEntries == null)
            {
                return;
            }

            var cellData = asset.CellData;
            var factorIndices = cellData.probes[probe.Index];
            if (factorIndices.start < 0)
            {
                return;
            }

            int brickLabelsDrawn = 0;
            for (int factorIndex = factorIndices.start; factorIndex <= factorIndices.end; factorIndex++)
            {
                var factor = cellData.factors[factorIndex];
                int brickIndex = factor.brickIndex;
                if (brickIndex < 0 ||
                    brickIndex >= cellData.bricks.Length ||
                    brickIndex >= LatestBrickShadowCacheDebugEntries.Length)
                {
                    continue;
                }

                var brick = cellData.bricks[brickIndex];
                var bounds = CalculateBrickBounds(cellData.surfels, brick);
                var brickCache = LatestBrickShadowCacheDebugEntries[brickIndex];
                Gizmos.color = GetBrickShadowCacheColor(brickCache);
                Gizmos.DrawWireCube(bounds.center, bounds.size);
                Gizmos.DrawLine(probePos, bounds.center);

                if (shadowCacheDebugShowLabels && brickLabelsDrawn < MaxShadowCacheBrickLabels)
                {
                    uint age = LatestShadowCacheDebugFrameIndex >= brickCache.lastUpdateFrame
                        ? LatestShadowCacheDebugFrameIndex - brickCache.lastUpdateFrame
                        : 0;
                    Handles.Label(bounds.center,
                        $"B:{brickIndex} m:{brickCache.meanShadow:0.00} v:{brickCache.variance:0.000} a:{age}");
                    brickLabelsDrawn++;
                }
            }
        }

        private static Bounds CalculateBrickBounds(Surfel[] surfels, SurfelIndices brick)
        {
            if (brick.count <= 0 || surfels == null || brick.start < 0 || brick.start >= surfels.Length)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            int end = Mathf.Min(brick.start + brick.count, surfels.Length);
            var bounds = new Bounds(surfels[brick.start].position, Vector3.zero);
            for (int i = brick.start + 1; i < end; i++)
            {
                bounds.Encapsulate(surfels[i].position);
            }

            bounds.Expand(0.1f);
            return bounds;
        }

        private Color GetBrickShadowCacheColor(BrickShadowCacheDebugEntry brickCache)
        {
            bool epochValid = brickCache.epoch == LatestShadowCacheDebugEpoch;
            uint age = LatestShadowCacheDebugFrameIndex >= brickCache.lastUpdateFrame
                ? LatestShadowCacheDebugFrameIndex - brickCache.lastUpdateFrame
                : 0;

            if (!epochValid)
            {
                return new Color(0.45f, 0.45f, 0.45f, 0.6f);
            }

            if (age > Mathf.Max(1, shadowCacheMaxAge))
            {
                return new Color(1f, 0.75f, 0.15f, 0.75f);
            }

            return GetShadowCacheStatusColor(ShadowCacheDebugStatus.BrickCacheHit, brickCache.meanShadow);
        }

        private static Color GetShadowCacheStatusColor(ShadowCacheDebugStatus status, float shadow)
        {
            Color baseColor = status switch
            {
                ShadowCacheDebugStatus.FreshHit => new Color(0.1f, 0.9f, 0.25f, 0.85f),
                ShadowCacheDebugStatus.Sampled => new Color(0f, 0.85f, 1f, 0.85f),
                ShadowCacheDebugStatus.FallbackFromCache => new Color(0.1f, 0.35f, 1f, 0.9f),
                ShadowCacheDebugStatus.UncoveredNoCache => new Color(1f, 0.15f, 0.1f, 0.9f),
                ShadowCacheDebugStatus.BrickCacheHit => new Color(0.05f, 0.75f, 0.45f, 0.85f),
                _ => new Color(0.45f, 0.45f, 0.45f, 0.75f)
            };

            float brightness = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(shadow));
            baseColor.r *= brightness;
            baseColor.g *= brightness;
            baseColor.b *= brightness;
            return baseColor;
        }

        /// <summary>
        /// Draw connections between neighboring probes
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeConnections(int probeIndex, Vector3 probePos)
        {
            var (x, y, z) = IndexToCoordinate(probeIndex);

            // Draw lines to neighboring probes
            Gizmos.color = Color.cyan * 0.5f;

            // X neighbors
            if (x > 0)
            {
                int neighborIndex = CoordinateToIndex(x - 1, y, z);
                Gizmos.DrawLine(probePos, Probes[neighborIndex].Position);
            }

            // Y neighbors
            if (y > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y - 1, z);
                Gizmos.DrawLine(probePos, Probes[neighborIndex].Position);
            }

            // Z neighbors
            if (z > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y, z - 1);
                Gizmos.DrawLine(probePos, Probes[neighborIndex].Position);
            }
        }

        /// <summary>
        /// Draw arrow to show offset direction and magnitude
        /// </summary>
        /// <param name="startPos">Start position of the arrow</param>
        /// <param name="endPos">End position of the arrow</param>
        private static void DrawOffsetArrow(Vector3 startPos, Vector3 endPos)
        {
            Vector3 direction = endPos - startPos;
            float distance = direction.magnitude;
            
            // Only draw arrow if there's a significant offset
            if (distance < 0.01f) return;
            
            direction.Normalize();
            
            // Draw main arrow line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(startPos, endPos);
            
            // Calculate arrow head size based on distance
            float arrowHeadSize = Mathf.Min(distance * 0.3f, 0.2f);
            Vector3 arrowHeadPos = endPos - direction * arrowHeadSize;
            
            // Draw arrow headlines
            Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
            if (right.magnitude < 0.1f) // If direction is parallel to up vector
            {
                right = Vector3.Cross(direction, Vector3.right).normalized;
            }
            Vector3 up = Vector3.Cross(right, direction).normalized;
            
            // Draw arrow head
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(endPos, arrowHeadPos + right * arrowHeadSize * 0.3f);
            Gizmos.DrawLine(endPos, arrowHeadPos - right * arrowHeadSize * 0.3f);
            Gizmos.DrawLine(endPos, arrowHeadPos + up * arrowHeadSize * 0.3f);
            Gizmos.DrawLine(endPos, arrowHeadPos - up * arrowHeadSize * 0.3f);
        }

        /// <summary>
        /// Draw connections between neighboring probes at virtual offset positions
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="virtualOffsetPos">Virtual offset position of the probe</param>
        private void DrawVirtualOffsetProbeConnections(int probeIndex, Vector3 virtualOffsetPos)
        {
            var (x, y, z) = IndexToCoordinate(probeIndex);

            // Draw lines to neighboring probes at their virtual offset positions
            Gizmos.color = Color.magenta * 0.5f;

            // X neighbors
            if (x > 0)
            {
                int neighborIndex = CoordinateToIndex(x - 1, y, z);
                Vector3 neighborVirtualPos = Probes[neighborIndex].Position + CalculateProbeVirtualOffset(Probes[neighborIndex].Position);
                Gizmos.DrawLine(virtualOffsetPos, neighborVirtualPos);
            }

            // Y neighbors
            if (y > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y - 1, z);
                Vector3 neighborVirtualPos = Probes[neighborIndex].Position + CalculateProbeVirtualOffset(Probes[neighborIndex].Position);
                Gizmos.DrawLine(virtualOffsetPos, neighborVirtualPos);
            }

            // Z neighbors
            if (z > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y, z - 1);
                Vector3 neighborVirtualPos = Probes[neighborIndex].Position + CalculateProbeVirtualOffset(Probes[neighborIndex].Position);
                Gizmos.DrawLine(virtualOffsetPos, neighborVirtualPos);
            }
        }

        /// <summary>
        /// Draw adjustment volume effects for a probe
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        /// <param name="totalOffset">Total virtual offset applied to this probe</param>
        private void DrawAdjustmentVolumeEffects(Vector3 probePos, Vector3 totalOffset)
        {
            // Use direct access to manager's volumes to avoid array allocation
            var adjustmentVolumes = PRTVolumeManager.AdjustmentVolumes;
            for (int i = 0; i < adjustmentVolumes.Count; i++)
            {
                var volume = adjustmentVolumes[i];
                if (volume != null && volume.Contains(probePos))
                {
                    // Draw volume influence indicator
                    Gizmos.color = volume.mode switch
                    {
                        PRTProbeAdjustmentMode.ApplyVirtualOffset => Color.cyan * 0.3f,
                        PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings => Color.yellow * 0.3f,
                        PRTProbeAdjustmentMode.IntensityScale => Color.green * 0.3f,
                        PRTProbeAdjustmentMode.InvalidateProbes => Color.red * 0.3f,
                        _ => Color.white * 0.3f
                    };

                    // Draw small indicator sphere at probe position
                    Gizmos.DrawSphere(probePos, probeHandleSize * 0.05f);

                    // Draw line from probe to volume center to show influence
                    Gizmos.DrawLine(probePos, volume.transform.position);

                    // Draw ray-traced virtual offset position for OverrideVirtualOffsetSettings mode
                    if (volume.mode == PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings)
                    {
                        Vector3 rayTracedPosition = CalculateProbeVirtualOffset(probePos);
                        
                        // Draw ray-traced position
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireSphere(rayTracedPosition, probeHandleSize * 0.08f);
                        
                        // Draw line from original to ray-traced position
                        Gizmos.color = Color.red;
                        Gizmos.DrawLine(probePos, rayTracedPosition);
                        
                        // Draw ray directions used for ray tracing
                        DrawRayTraceDirections(probePos, volume);
                    }
                }
            }
        }

        /// <summary>
        /// Draw ray trace directions for visualization
        /// </summary>
        /// <param name="probePos">Probe position</param>
        /// <param name="volume">Adjustment volume</param>
        private static void DrawRayTraceDirections(Vector3 probePos, PRTProbeAdjustmentVolume volume)
        {
            Gizmos.color = Color.yellow * 0.3f;
            
            Vector3[] sampleDirections = GetSampleDirections();
            
            foreach (Vector3 direction in sampleDirections)
            {
                Vector3 rayOrigin = probePos + direction * volume.rayOriginBias;
                Vector3 rayEnd = rayOrigin + direction * 2f;
                
                // Cast ray to see if it hits geometry
                if (Physics.Raycast(rayOrigin, direction, out RaycastHit hit, float.MaxValue))
                {
                    // Color based on hit type
                    Vector3 hitNormal = hit.normal;
                    float dotSurface = Vector3.Dot(direction, hitNormal);
                    
                    if (dotSurface > 0) // Front face
                    {
                        Gizmos.color = Color.red * 0.5f; // Front face - red
                    }
                    else // Back face
                    {
                        Gizmos.color = Color.green * 0.5f; // Back face - green
                    }
                    
                    // Draw line to hit point
                    Gizmos.DrawLine(rayOrigin, hit.point);
                    
                    // Draw small sphere at hit point
                    Gizmos.DrawSphere(hit.point, 0.01f);
                }
                else
                {
                    // No hit - draw full ray
                    Gizmos.color = Color.yellow * 0.3f;
                    Gizmos.DrawLine(rayOrigin, rayEnd);
                }
                
                // Draw small sphere at ray origin
                Gizmos.color = Color.cyan * 0.7f;
                Gizmos.DrawSphere(rayOrigin, 0.01f);
            }
        }

        /// <summary>
        /// Convert probe index to 3D coordinates
        /// </summary>
        /// <param name="index">Probe index</param>
        /// <returns>3D coordinates (x, y, z)</returns>
        private (int x, int y, int z) IndexToCoordinate(int index)
        {
            int x = index / (probeSizeY * probeSizeZ);
            int remainder = index % (probeSizeY * probeSizeZ);
            int y = remainder / probeSizeZ;
            int z = remainder % probeSizeZ;
            return (x, y, z);
        }

        /// <summary>
        /// Convert 3D coordinates to probe index
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="z">Z coordinate</param>
        /// <returns>Probe index</returns>
        private int CoordinateToIndex(int x, int y, int z)
        {
            return x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
        }

        /// <summary>
        /// Draw surfel and brick grid visualization for a specific probe
        /// </summary>
        private void DrawProbeSurfelBrickGrid(PRTProbe probe, Vector3 probePos)
        {
            if (!asset || !_isDataInitialized) return;

            var cellData = asset.CellData;
            var factorIndices = cellData.probes[probe.Index];
            if (factorIndices.start < 0) return;

            // Draw surfels and bricks for this probe using Factor structure
            for (int factorIndex = factorIndices.start; factorIndex <= factorIndices.end; factorIndex++)
            {
                var factor = cellData.factors[factorIndex];
                var brickIndex = factor.brickIndex;
                var weight = factor.weight;
                var brick = cellData.bricks[brickIndex];
                
                // Draw surfels in this brick
                Gizmos.color = Color.yellow;
                const float surfelSize = 0.05f;
                for (int j = brick.start; j <= brick.start + brick.count - 1; j++)
                {
                    var surfel = cellData.surfels[j];
                    // Draw surfel position
                    Gizmos.DrawSphere(surfel.position, surfelSize);
                    // Draw normal
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(surfel.position, surfel.position + surfel.normal * 0.2f);
                    Gizmos.color = Color.yellow;
                }
                
                // Calculate brick bounds
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                
                // Find brick bounds from its surfels
                for (int j = brick.start; j <= brick.start + brick.count - 1; j++)
                {
                    var surfel = cellData.surfels[j];
                    minX = Mathf.Min(minX, surfel.position.x);
                    minY = Mathf.Min(minY, surfel.position.y);
                    minZ = Mathf.Min(minZ, surfel.position.z);
                    maxX = Mathf.Max(maxX, surfel.position.x);
                    maxY = Mathf.Max(maxY, surfel.position.y);
                    maxZ = Mathf.Max(maxZ, surfel.position.z);
                }
                
                // Add some padding
                const float padding = 0.1f;
                minX -= padding; minY -= padding; minZ -= padding;
                maxX += padding; maxY += padding; maxZ += padding;
                
                // Create brick bounds
                var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
                var size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
                
                // Draw brick wireframe with weight-based alpha and color
                var mainDir = GetMainDirection(cellData.surfels[brick.start].normal);
                var baseColor = GetDirectionColor(mainDir);
                
                // Modulate color intensity based on weight
                baseColor.a = Mathf.Lerp(0.3f, 1.0f, weight); // Higher weight = more opaque
                Gizmos.color = baseColor;
                Gizmos.DrawWireCube(center, size);

                // Draw line from probe to brick center with weight-based thickness
                // Use different colors to represent weight visually
                if (weight > 0.7f)
                    Gizmos.color = Color.green; // High weight
                else if (weight > 0.3f)
                    Gizmos.color = Color.yellow; // Medium weight
                else
                    Gizmos.color = Color.red; // Low weight
                    
                Gizmos.DrawLine(probePos, center);
            }
        }

        private static Color GetDirectionColor(SurfelDirection direction)
        {
            return direction switch
            {
                SurfelDirection.PosX => new Color(1f, 0.2f, 0.2f, 0.5f), // Red
                SurfelDirection.NegX => new Color(1f, 0.5f, 0.5f, 0.5f), // Light Red
                SurfelDirection.PosY => new Color(0.2f, 1f, 0.2f, 0.5f), // Green
                SurfelDirection.NegY => new Color(0.5f, 1f, 0.5f, 0.5f), // Light Green
                SurfelDirection.PosZ => new Color(0.2f, 0.2f, 1f, 0.5f), // Blue
                SurfelDirection.NegZ => new Color(0.5f, 0.5f, 1f, 0.5f), // Light Blue
                _ => Color.white
            };
        }

        /// <summary>
        /// Draw bounding box for debugging
        /// </summary>
        private void DrawBoundingBox()
        {
            if (_currentBoundingBox.size == Vector3.zero)
                return;
            
            // Draw filled bounding box with transparency
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.4f);
            Gizmos.color = transparentRed;
            Gizmos.DrawCube(_currentBoundingBox.center, _currentBoundingBox.size);
        }

        /// <summary>
        /// Draw the result of FindClosestValidBoundingBox for debugging
        /// </summary>
        private void DrawClosestBoundingBoxResult()
        {
            if (!_lastClosestBoundingBoxCenter.HasValue || !_lastClosestBoundingBoxMin.HasValue) return;
            
            // Draw the closest bounding box center as a large sphere
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_lastClosestBoundingBoxCenter.Value, 0.3f);
            
            // Draw a wireframe sphere around it
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_lastClosestBoundingBoxCenter.Value, 0.4f);
            
            // Draw a line from camera to the closest bounding box center
            if (_mainCamera)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(_mainCamera.transform.position, _lastClosestBoundingBoxCenter.Value);
            }
            
            // Draw the closest bounding box as a wireframe cube
            Vector3 closestBoundingBoxSize = new Vector3(
                (CurrentVoxelGrid.X - 1) * probeGridSize,
                (CurrentVoxelGrid.Y - 1) * probeGridSize,
                (CurrentVoxelGrid.Z - 1) * probeGridSize
            );
            Vector3 closestBoundingBoxMin = transform.position + new Vector3(
                _lastClosestBoundingBoxMin.Value.x * probeGridSize,
                _lastClosestBoundingBoxMin.Value.y * probeGridSize,
                _lastClosestBoundingBoxMin.Value.z * probeGridSize
            );
            Vector3 closestBoundingBoxCenter = closestBoundingBoxMin + closestBoundingBoxSize * 0.5f;
            
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(closestBoundingBoxCenter, closestBoundingBoxSize);
        }

        private static SurfelDirection GetMainDirection(Vector3 normal)
        {
            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            if (absX >= absY && absX >= absZ)
                return normal.x >= 0 ? SurfelDirection.PosX : SurfelDirection.NegX;
            if (absY >= absX && absY >= absZ)
                return normal.y >= 0 ? SurfelDirection.PosY : SurfelDirection.NegY;
            return normal.z >= 0 ? SurfelDirection.PosZ : SurfelDirection.NegZ;
        }
        
        private void DrawDebugVisualization(ProbeDebugMode inDebugMode, Vector3 probePos, PRTProbeDebugData debugData)
        {
            // Draw based on debug mode
            switch (inDebugMode)
            {
                case ProbeDebugMode.SphereDistribution:
                    DrawSphereDistribution(probePos, debugData);
                    break;
                case ProbeDebugMode.SampleDirection:
                    DrawSampleDirections(probePos, debugData);
                    break;
                case ProbeDebugMode.Surfel:
                    DrawSurfels(probePos, debugData);
                    break;
            }
        }

        /// <summary>
        /// Draw sphere distribution debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        /// <param name="debugData"></param>
        private void DrawSphereDistribution(Vector3 probePos, PRTProbeDebugData debugData)
        {
            for (int i = 0; i < debugData.LocalSurfels.Length; i++)
            {
                Vector3 dir = GetSurfelDirection(debugData.LocalSurfels[i], probePos);
                bool isSky = IsSky(debugData.LocalSurfels[i]);

                Gizmos.color = isSky ? SkyColor : DefaultColor;
                Gizmos.DrawSphere(dir + probePos, SphereSize * probeHandleSize);
            }
        }

        /// <summary>
        /// Draw sample directions debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        /// <param name="debugData"></param>
        private static void DrawSampleDirections(Vector3 probePos, PRTProbeDebugData debugData)
        {
            for (int i = 0; i < debugData.LocalSurfels.Length; i++)
            {
                Surfel surfel = debugData.LocalSurfels[i];
                Vector3 dir = GetSurfelDirection(surfel, probePos);
                bool isSky = IsSky(surfel);

                Gizmos.color = isSky ? SkyColor : DefaultColor;

                if (isSky)
                {
                    Gizmos.DrawLine(probePos, probePos + dir * SkyRayLength);
                }
                else
                {
                    Gizmos.DrawLine(probePos, surfel.position);
                    Gizmos.DrawSphere(surfel.position, SurfelSize);
                }
            }
        }

        /// <summary>
        /// Draw surfels debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        /// <param name="debugData"></param>
        private static void DrawSurfels(Vector3 probePos, PRTProbeDebugData debugData)
        {
            for (int i = 0; i < debugData.LocalSurfels.Length; i++)
            {
                Surfel surfel = debugData.LocalSurfels[i];
                bool isSky = IsSky(surfel);

                Gizmos.color = isSky ? SkyColor : DefaultColor;

                // Draw surfel position
                Gizmos.DrawSphere(surfel.position, SurfelSize);

                // Draw normal
                Gizmos.color = NormalColor;
                Gizmos.DrawLine(surfel.position, surfel.position + surfel.normal * NormalLength);
                Gizmos.color = DefaultColor;
            }
        }

        /// <summary>
        /// Get normalized direction from probe to surfel
        /// </summary>
        /// <param name="surfel">Surfel data</param>
        /// <param name="probePos">Probe position</param>
        /// <returns>Normalized direction vector</returns>
        private static Vector3 GetSurfelDirection(Surfel surfel, Vector3 probePos)
        {
            Vector3 dir = surfel.position - probePos;
            return dir.normalized;
        }

        /// <summary>
        /// Check if surfel represents sky
        /// </summary>
        /// <param name="surfel">Surfel data</param>
        /// <returns>True if surfel is sky</returns>
        private static bool IsSky(Surfel surfel)
        {
            return surfel.skyMask >= SkyMaskThreshold;
        }
    }
}
#endif
