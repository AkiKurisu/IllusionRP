using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public class SurfelGrid
    {
        public const float DefaultBrickSize = 4f;
        public const float MergeDistance = 0.1f;
        private readonly Dictionary<BrickKey, int> _brickLookup = new();
        private readonly List<Brick> _bricks = new();
        private readonly Dictionary<int, BrickFactor>[] _probeFactors;
        private readonly List<PRTSkySample>[] _probeSky;
        private readonly PRTProbeData[] _probes;
        private readonly bool[] _captured;

        private readonly struct BrickKey : IEquatable<BrickKey>
        {
            private readonly Vector3Int _cell;
            private readonly int _direction;
            private readonly uint _material, _renderingLayers, _objectLayer;
            public BrickKey(Surfel surfel)
            {
                _cell = new Vector3Int(Mathf.FloorToInt(surfel.position.x / DefaultBrickSize),
                    Mathf.FloorToInt(surfel.position.y / DefaultBrickSize), Mathf.FloorToInt(surfel.position.z / DefaultBrickSize));
                Vector3 n = surfel.normal;
                Vector3 a = new(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                _direction = a.x >= a.y && a.x >= a.z ? (n.x >= 0 ? 0 : 1) :
                    a.y >= a.z ? (n.y >= 0 ? 2 : 3) : (n.z >= 0 ? 4 : 5);
                _material = surfel.materialKey;
                _renderingLayers = surfel.renderingLayerMask;
                _objectLayer = surfel.objectLayerMask;
            }
            public bool Equals(BrickKey other) => _cell == other._cell && _direction == other._direction &&
                _material == other._material && _renderingLayers == other._renderingLayers && _objectLayer == other._objectLayer;
            public override bool Equals(object obj) => obj is BrickKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_cell, _direction, _material, _renderingLayers, _objectLayer);
        }

        private class Brick
        {
            public readonly Dictionary<Vector3Int, int> lookup = new();
            public readonly List<Surfel> surfels = new();
            public readonly List<int> samples = new();
            public void Add(Surfel surfel)
            {
                var key = new Vector3Int(Mathf.RoundToInt(surfel.position.x / MergeDistance),
                    Mathf.RoundToInt(surfel.position.y / MergeDistance), Mathf.RoundToInt(surfel.position.z / MergeDistance));
                if (!lookup.TryGetValue(key, out int index))
                {
                    lookup.Add(key, surfels.Count);
                    surfels.Add(surfel);
                    samples.Add(1);
                    return;
                }
                Surfel merged = surfels[index];
                int n = samples[index];
                merged.position = (merged.position * n + surfel.position) / (n + 1);
                merged.albedo = (merged.albedo * n + surfel.albedo) / (n + 1);
                merged.normal += surfel.normal;
                surfels[index] = merged;
                samples[index] = n + 1;
            }
        }

        public SurfelGrid(int probeCount)
        {
            _probes = new PRTProbeData[probeCount];
            _captured = new bool[probeCount];
            _probeFactors = new Dictionary<int, BrickFactor>[probeCount];
            _probeSky = new List<PRTSkySample>[probeCount];
        }
        public void AddProbe(int probeIndex, Surfel[] samples, Vector4[] directions, Vector3 captureOffset, uint validity)
        {
            if (samples.Length != directions.Length || _captured[probeIndex])
                throw new ArgumentException("PRT probe samples must match the direction table and be supplied once.");
            var factors = new Dictionary<int, BrickFactor>();
            var sky = new List<PRTSkySample>();
            for (int i = 0; i < samples.Length; i++)
            {
                Surfel surfel = samples[i];
                Vector4 direction = directions[i];
                if ((surfel.flags & Surfel.SkyMiss) != 0)
                {
                    sky.Add(new PRTSkySample { direction = direction, weight = direction.w });
                    continue;
                }
                if (!PRTDataValidation.IsFinite(surfel.position) || !PRTDataValidation.IsFinite(surfel.normal) ||
                    !PRTDataValidation.IsFinite(surfel.albedo) || surfel.normal.sqrMagnitude < 0.5f || surfel.materialKey == 0)
                    throw new InvalidOperationException($"PRT probe {probeIndex} has an invalid geometry sample at {i}: " +
                        $"position={surfel.position.ToString("R")}, normal={surfel.normal.ToString("R")}, " +
                        $"albedo={surfel.albedo.ToString("R")}, materialKey={surfel.materialKey}, flags={surfel.flags}, " +
                        $"renderingLayers=0x{surfel.renderingLayerMask:X8}, objectLayer=0x{surfel.objectLayerMask:X8}, " +
                        $"directionAndWeight={direction.ToString("R")}, normalSquared={surfel.normal.sqrMagnitude:R}.");
                var key = new BrickKey(surfel);
                if (!_brickLookup.TryGetValue(key, out int brickIndex))
                {
                    brickIndex = _bricks.Count;
                    _brickLookup.Add(key, brickIndex);
                    _bricks.Add(new Brick());
                }
                _bricks[brickIndex].Add(surfel);
                factors.TryGetValue(brickIndex, out BrickFactor factor);
                factor.brickIndex = brickIndex;
                factor.AddDirection(direction);
                factors[brickIndex] = factor;
            }
            _probeFactors[probeIndex] = factors;
            _probeSky[probeIndex] = sky;
            _probes[probeIndex] = new PRTProbeData { captureOffset = captureOffset, validity = validity };
            _captured[probeIndex] = true;
        }
        public PRTSectorData GenerateSector()
        {
            var surfels = new List<Surfel>();
            var bricks = new SurfelIndices[_bricks.Count];
            for (int i = 0; i < _bricks.Count; i++)
            {
                Brick brick = _bricks[i];
                bricks[i] = new SurfelIndices { start = surfels.Count, count = brick.surfels.Count };
                foreach (Surfel raw in brick.surfels)
                {
                    Surfel surfel = raw;
                    surfel.normal.Normalize();
                    surfels.Add(surfel);
                }
            }
            var factors = new List<BrickFactor>();
            var skySamples = new List<PRTSkySample>();
            for (int i = 0; i < _probes.Length; i++)
            {
                if (!_captured[i]) throw new InvalidOperationException($"PRT probe {i} was not captured.");
                PRTProbeData probe = _probes[i];
                probe.factorStart = factors.Count;
                probe.factorCount = _probeFactors[i].Count;
                var keys = new List<int>(_probeFactors[i].Keys);
                keys.Sort();
                foreach (int key in keys) factors.Add(_probeFactors[i][key]);
                probe.skyStart = skySamples.Count;
                probe.skyCount = _probeSky[i].Count;
                skySamples.AddRange(_probeSky[i]);
                _probes[i] = probe;
            }
            return new PRTSectorData { surfels = surfels.ToArray(), bricks = bricks, factors = factors.ToArray(),
                probes = _probes, skySamples = skySamples.ToArray() };
        }
    }
}
