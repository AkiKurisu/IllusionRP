using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    /// <summary>
    /// Surfel structure: contains position, normal, albedo, and sky mask
    /// </summary>
    [Serializable]
    public struct Surfel
    {
        public Vector3 position;

        public Vector3 normal;

        public Vector3 albedo;

        public float skyMask;

        public const int Stride = 40;   // float3 * 3 + float = 40 bytes
    }

    /// <summary>
    /// Represents the main normal direction of a Surfel
    /// </summary>
    public enum SurfelDirection
    {
        PosX,
        NegX,
        PosY,
        NegY,
        PosZ,
        NegZ
    }

    /// <summary>
    /// Represents the indices of a Surfel
    /// </summary>
    [Serializable]
    public struct SurfelIndices
    {
        public int start;

        public int count;

        public const int Stride = 8;    // int * 2 = 8 bytes
    }

    /// <summary>
    /// Factor structure: contains Brick index and the contribution weight of that Brick to the Probe
    /// </summary>
    [Serializable]
    public struct BrickFactor
    {
        public int brickIndex;          // Index of the Brick in the global array

        public float weight;            // Contribution weight of this Brick to the Probe [0,1]

        public const int Stride = 8;    // int + float = 8 bytes
    }

    /// <summary>
    /// Factor range: each Probe stores the range of Factors it uses
    /// </summary>
    [Serializable]
    public struct FactorIndices
    {
        public int start;         // Start index in the Factor array

        public int end;           // End index in the Factor array
    }

    /// <summary>
    /// Represents a 4x4x4 brick containing merged Surfels
    /// </summary>
    public class SurfelBrick
    {
        public const float BrickSize = 4.0f; // 4x4x4 meters

        public readonly List<int> SurfelIndices = new(); // Store indices instead of actual surfels

        public readonly HashSet<PRTProbe> ReferencedProbes = new(); // Store probes that reference this brick

        public int Index { get; } // Global index in the brick array

        public SurfelBrick(int index)
        {
            Index = index;
        }
    }

    [Serializable]
    public class CellData
    {
        // Surfel data
        public Surfel[] surfels;

        // Brick data
        public SurfelIndices[] bricks;

        // Factor data
        public BrickFactor[] factors;

        // Probe data
        public FactorIndices[] probes;

        // Packed validity data: intensity (24 bits, 0-5 range) + validity mask (8 bits, 0-1 range)
        public float[] validityMasks;

        private CellData()
        {

        }

        public CellData(Surfel[] inSurfels, SurfelIndices[] surfelIndices, BrickFactor[] brickFactors, FactorIndices[] factorIndices, float[] validity)
        {
            surfels = inSurfels;
            bricks = surfelIndices;
            factors = brickFactors;
            probes = factorIndices;
            validityMasks = validity;
        }

        public static CellData GeDefault()
        {
            return new CellData
            {
                surfels = Array.Empty<Surfel>(),
                bricks = Array.Empty<SurfelIndices>(),
                factors = Array.Empty<BrickFactor>(),
                probes = Array.Empty<FactorIndices>(),
                validityMasks = Array.Empty<float>()
            };
        }
    }

    /// <summary>
    /// Manages the grid of Surfel bricks and handles Surfel organization
    /// </summary>
    public class SurfelGrid
    {
        private readonly Dictionary<ulong, SurfelBrick> _bricks = new();

        private int _nextBrickIndex; // Counter for assigning unique brick indices

        private readonly List<Surfel> _allSurfels = new(); // Store all surfels in a central list

        /// <summary>
        /// Get the main direction of a normal vector
        /// </summary>
        private static SurfelDirection GetSurfelDirection(Vector3 normal)
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

        /// <summary>
        /// Convert world position to grid position
        /// </summary>
        private static Vector3Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector3Int(
                Mathf.FloorToInt(worldPos.x / SurfelBrick.BrickSize),
                Mathf.FloorToInt(worldPos.y / SurfelBrick.BrickSize),
                Mathf.FloorToInt(worldPos.z / SurfelBrick.BrickSize)
            );
        }

        /// <summary>
        /// Generate a hash key from grid position and direction
        /// </summary>
        private static ulong GenerateHashKey(Vector3Int gridPos, SurfelDirection dir)
        {
            // Use 16 bits for each coordinate and 4 bits for direction
            // This allows for a grid of ±32,768 in each dimension
            ulong key = ((ulong)(gridPos.x + 32768) << 36) |
                       ((ulong)(gridPos.y + 32768) << 20) |
                       ((ulong)(gridPos.z + 32768) << 4) |
                       ((ulong)dir);
            return key;
        }

        /// <summary>
        /// Calculate the weight of a brick's contribution to a probe
        /// Uses the average normal of all surfels in the brick
        /// </summary>
        private static float CalculateBrickWeight(Vector3 probePosition, Vector3 brickCenter, Vector3 brickAverageNormal)
        {
            // Calculate direction from brick center to probe
            Vector3 brickToProbe = (probePosition - brickCenter).normalized;
            // Calculate normal-based weight: how well the brick's average normal faces the probe
            // Surfels that face towards the probe contribute more to its lighting
            float normalDot = Vector3.Dot(brickAverageNormal, brickToProbe);
            float normalWeight = Mathf.Max(0.0f, normalDot);
            return normalWeight;
        }

        /// <summary>
        /// Generate a hash key for precise position-based merging within a brick
        /// </summary>
        private static ulong GeneratePositionHash(Vector3 position, float precision)
        {
            int x = Mathf.FloorToInt(position.x / precision + 0.5f);
            int y = Mathf.FloorToInt(position.y / precision + 0.5f);
            int z = Mathf.FloorToInt(position.z / precision + 0.5f);

            ulong hash = 1469598103934665603UL;
            hash ^= (uint)x; hash *= 1099511628211UL;
            hash ^= (uint)y; hash *= 1099511628211UL;
            hash ^= (uint)z; hash *= 1099511628211UL;
            return hash;
        }

        /// <summary>
        /// Merge multiple surfels at the same position
        /// </summary>
        private static Surfel MergeSurfels(List<Surfel> surfelsToMerge)
        {
            if (surfelsToMerge.Count == 1)
                return surfelsToMerge[0];

            Vector3 avgPosition = Vector3.zero;
            Vector3 avgNormal = Vector3.zero;
            Vector3 avgAlbedo = Vector3.zero;
            float avgSkyMask = 0f;

            foreach (var surfel in surfelsToMerge)
            {
                avgPosition += surfel.position;
                avgAlbedo += surfel.albedo;
                avgSkyMask += surfel.skyMask;
                avgNormal += surfel.normal;
            }

            int count = surfelsToMerge.Count;
            avgPosition /= count;
            avgAlbedo /= count;
            avgSkyMask /= count;
            avgNormal = (avgNormal / count).normalized;


            return new Surfel
            {
                position = avgPosition,
                normal = avgNormal,
                albedo = avgAlbedo,
                skyMask = avgSkyMask
            };
        }

        /// <summary>
        /// Perform batch merging of surfels within a brick based on position
        /// </summary>
        private static List<Surfel> MergeSurfelsInBrick(List<Surfel> surfels)
        {
            const float mergeDistance = 0.1f;

            // Group surfels by position hash for initial clustering
            var positionGroups = new Dictionary<ulong, List<Surfel>>();

            foreach (var surfel in surfels)
            {
                ulong posHash = GeneratePositionHash(surfel.position, mergeDistance);
                if (!positionGroups.TryGetValue(posHash, out var group))
                {
                    group = new List<Surfel>();
                    positionGroups[posHash] = group;
                }
                group.Add(surfel);
            }

            var mergedSurfels = new List<Surfel>();

            // Process each surfel group
            foreach (var group in positionGroups.Values)
            {
                mergedSurfels.Add(MergeSurfels(group));
            }

            return mergedSurfels;
        }

        /// <summary>
        /// Add a surfel to the grid
        /// </summary>
        public void AddSurfel(Surfel surfel, PRTProbe probe)
        {
            Vector3Int gridPos = WorldToGrid(surfel.position);
            SurfelDirection dir = GetSurfelDirection(surfel.normal);
            ulong key = GenerateHashKey(gridPos, dir);

            if (!_bricks.TryGetValue(key, out SurfelBrick brick))
            {
                brick = new SurfelBrick(_nextBrickIndex++);
                _bricks[key] = brick;
            }

            // Simply add the surfel without merging - merging will be done in batch during GenerateCell
            int surfelIndex = _allSurfels.Count;
            _allSurfels.Add(surfel);
            brick.SurfelIndices.Add(surfelIndex);
            brick.ReferencedProbes.Add(probe);
        }

        /// <summary>
        /// Generate cell data in the grid using Factor-based approach with batch merging
        /// </summary>
        /// <param name="probeGrid">Probes grid (should be ordered before) contributed to.</param>
        /// <param name="validityMask"></param>
        /// <returns>Tuple containing surfels, brick indices, factors, probe indices</returns>
        public CellData GenerateCell(PRTProbe[] probeGrid, float[] validityMask)
        {
            // First get all unique bricks and sort them by their index to ensure consistent ordering
            var uniqueBricks = _bricks.Values.OrderBy(b => b.Index).ToArray();

            // Perform batch merging for each brick and collect merged surfels
            var allMergedSurfels = new List<Surfel>();
            var brickIndices = new SurfelIndices[uniqueBricks.Length];

            // Process each unique brick to merge surfels and create surfel data
            for (int brickIndex = 0; brickIndex < uniqueBricks.Length; brickIndex++)
            {
                var brick = uniqueBricks[brickIndex];

                // Collect all surfels for this brick
                var brickSurfels = new List<Surfel>();
                foreach (int surfelIndex in brick.SurfelIndices)
                {
                    brickSurfels.Add(_allSurfels[surfelIndex]);
                }

                // Perform batch merging for this brick
                var mergedSurfels = MergeSurfelsInBrick(brickSurfels);

                // Create indices for this brick's merged surfels
                var indices = new SurfelIndices
                {
                    start = allMergedSurfels.Count,
                    count = mergedSurfels.Count
                };

                // Add merged surfels to the global list
                allMergedSurfels.AddRange(mergedSurfels);
                brickIndices[brickIndex] = indices;
            }

            // Convert to array for final output
            var reorderedSurfels = allMergedSurfels.ToArray();

            // Create factors list, probe indices, and sky visibility using Factor-based approach
            var allFactors = new List<BrickFactor>();
            var probeIndices = new FactorIndices[probeGrid.Length];

            // For each probe, create factors for all bricks that reference it
            for (int probeIndex = 0; probeIndex < probeGrid.Length; probeIndex++)
            {
                var probe = probeGrid[probeIndex];
                var probePosition = probe.Position;

                int factorStartIndex = allFactors.Count;

                // Find all bricks that reference this probe and calculate their weights
                var probeBrickFactors = new List<(int brickIndex, float weight)>();

                for (int brickIndex = 0; brickIndex < uniqueBricks.Length; brickIndex++)
                {
                    var brick = uniqueBricks[brickIndex];

                    if (!brick.ReferencedProbes.Contains(probe))
                        continue;

                    // Calculate brick center position and average normal from merged surfels in the brick
                    Vector3 brickCenter = Vector3.zero;
                    Vector3 brickAverageNormal = Vector3.zero;
                    var brickSurfelIndices = brickIndices[brickIndex];
                    int surfelCount = brickSurfelIndices.count;

                    for (int i = 0; i < surfelCount; i++)
                    {
                        var surfel = reorderedSurfels[brickSurfelIndices.start + i];
                        brickCenter += surfel.position;
                        brickAverageNormal += surfel.normal;
                    }

                    if (surfelCount > 0)
                    {
                        brickCenter /= surfelCount;
                        brickAverageNormal = (brickAverageNormal / surfelCount).normalized;
                    }

                    // Calculate weight for this brick
                    float weight = CalculateBrickWeight(probePosition, brickCenter, brickAverageNormal);
                    probeBrickFactors.Add((brickIndex, weight));
                }

                // Normalize weights so they sum to 1.0
                float totalWeight = probeBrickFactors.Sum(f => f.weight);
                for (int i = 0; i < probeBrickFactors.Count; i++)
                {
                    var (brickIndex, weight) = probeBrickFactors[i];
                    float normalizedWeight = weight / totalWeight;
                    probeBrickFactors[i] = (brickIndex, normalizedWeight);
                }

                // Sort factors by brick index for consistent ordering
                probeBrickFactors.Sort((a, b) => a.brickIndex.CompareTo(b.brickIndex));

                // Add factors to the global list
                foreach (var (brickIndex, weight) in probeBrickFactors)
                {
                    allFactors.Add(new BrickFactor
                    {
                        brickIndex = brickIndex,
                        weight = weight
                    });
                }

                // Set probe indices
                probeIndices[probeIndex] = new FactorIndices
                {
                    start = factorStartIndex,
                    end = allFactors.Count - 1
                };
            }

            return new CellData(reorderedSurfels, brickIndices, allFactors.ToArray(), probeIndices, validityMask);
        }
    }
}
