#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
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

        private const int MaxShadowCacheBrickLabels = 8;
                
        private Vector3? _lastClosestBoundingBoxCenter;

        private Vector3Int? _lastClosestBoundingBoxMin;
        
        private void OnDrawGizmos()
        {
            if (!gameObject.scene.IsValid()) return;
            if (!IsFeatureEnabled) return;

            if (Probes == null || Probes.Length == 0)
            {
                return;
            }
            
            if (debugMode == ProbeVolumeDebugMode.None)
                return;

            if (_mainCamera)
            {
                // Draw camera position and local probes
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
            if (!_isDataInitialized || _probeDebugData == null || index < 0 || index >= _probeDebugData.Length)
                return null;
            var data = _probeDebugData[index] ??= new PRTProbeDebugData();
            if (selectedProbeIndex == index && (debugMode == ProbeVolumeDebugMode.ShadowCache || selectedProbeDebugMode != ProbeDebugMode.IrradianceSphere))
            {
                var sector = asset.Sectors[asset.SectorIndex(index)];
                data.EnsureGeometry(sector.probes[asset.LocalProbeIndex(index)], sector.factors, sector.bricks, sector.surfels);
            }
            return data;
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

            var cellData = asset.Sectors[asset.SectorIndex(probe.Index)];
            var factorIndices = cellData.probes[asset.LocalProbeIndex(probe.Index)];
            if (factorIndices.factorStart < 0) return;

            // Draw surfels and bricks for this probe using Factor structure
            for (int factorIndex = factorIndices.factorStart; factorIndex < factorIndices.factorStart + factorIndices.factorCount; factorIndex++)
            {
                var factor = cellData.factors[factorIndex];
                var brickIndex = factor.brickIndex;
                var weight = factor.sh0 / 3.544907702f;
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
            return (surfel.flags & Surfel.SkyMiss) != 0;
        }
    }
}
#endif
