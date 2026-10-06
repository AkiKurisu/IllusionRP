using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public class SurfelGrid
    {
        public const float DefaultBrickSize = 4f;
        private readonly Dictionary<BrickKey, int> _brickLookup = new();
        private readonly List<Brick> _bricks = new();
        private readonly Dictionary<int, BrickTransfer>[] _probeFactors;
        private readonly uint[][] _probeSky;
        private readonly PRTProbeData[] _probes;
        private readonly bool[] _captured;
        private readonly float _mergeDistance;

        private readonly struct BrickKey : IEquatable<BrickKey>
        {
            private readonly Vector3Int _cell;
            private readonly int _direction;
            private readonly uint _material, _renderingLayers, _objectLayer;
            public BrickKey(PRTCaptureSample sample)
            {
                _cell = new Vector3Int(Mathf.FloorToInt(sample.position.x / DefaultBrickSize),
                    Mathf.FloorToInt(sample.position.y / DefaultBrickSize), Mathf.FloorToInt(sample.position.z / DefaultBrickSize));
                Vector3 n = sample.normal;
                Vector3 a = new(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                _direction = a.x >= a.y && a.x >= a.z ? (n.x >= 0 ? 0 : 1) :
                    a.y >= a.z ? (n.y >= 0 ? 2 : 3) : (n.z >= 0 ? 4 : 5);
                _material = sample.materialKey;
                _renderingLayers = sample.renderingLayerMask;
                _objectLayer = sample.objectLayerMask;
            }
            public bool Equals(BrickKey other) => _cell == other._cell && _direction == other._direction &&
                _material == other._material && _renderingLayers == other._renderingLayers && _objectLayer == other._objectLayer;
            public override bool Equals(object obj) => obj is BrickKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_cell, _direction, _material, _renderingLayers, _objectLayer);
        }

        private struct MergedSurfel
        {
            public Vector3 position, normal, albedo;
            public int samples;
        }

        private class Brick
        {
            public readonly Dictionary<Vector3Int, int> lookup = new();
            public readonly List<MergedSurfel> surfels = new();
            public uint renderingLayerMask, objectLayerMask;
            public void Add(PRTCaptureSample sample, float mergeDistance)
            {
                var key = new Vector3Int(Mathf.RoundToInt(sample.position.x / mergeDistance),
                    Mathf.RoundToInt(sample.position.y / mergeDistance), Mathf.RoundToInt(sample.position.z / mergeDistance));
                if (!lookup.TryGetValue(key, out int index))
                {
                    lookup.Add(key, surfels.Count);
                    surfels.Add(new MergedSurfel { position = sample.position, normal = sample.normal, albedo = sample.albedo, samples = 1 });
                    return;
                }
                MergedSurfel merged = surfels[index];
                int n = merged.samples;
                merged.position = (merged.position * n + sample.position) / (n + 1);
                merged.albedo = (merged.albedo * n + sample.albedo) / (n + 1);
                merged.normal += sample.normal;
                merged.samples = n + 1;
                surfels[index] = merged;
            }
        }

        public SurfelGrid(int probeCount, float mergeDistance)
        {
            _mergeDistance = mergeDistance;
            _probes = new PRTProbeData[probeCount];
            _captured = new bool[probeCount];
            _probeFactors = new Dictionary<int, BrickTransfer>[probeCount];
            _probeSky = new uint[probeCount][];
        }
        public void AddProbe(int probeIndex, PRTCaptureSample[] samples, Vector4[] directions, Vector3 captureOffset, uint validity)
        {
            if (samples.Length != directions.Length || _captured[probeIndex])
                throw new ArgumentException("PRT probe samples must match the direction table and be supplied once.");
            var factors = new Dictionary<int, BrickTransfer>();
            var sky = new uint[PRTSkyVisibility.Words(directions.Length)];
            for (int i = 0; i < samples.Length; i++)
            {
                PRTCaptureSample sample = samples[i];
                Vector4 direction = directions[i];
                if ((sample.flags & PRTCaptureSample.SkyMiss) != 0)
                {
                    sky[i >> 5] |= 1u << (i & 31);
                    continue;
                }
                if (!PRTDataValidation.IsFinite(sample.position) || !PRTDataValidation.IsFinite(sample.normal) ||
                    !PRTDataValidation.IsFinite(sample.albedo) || sample.normal.sqrMagnitude < 0.5f || sample.materialKey == 0)
                    throw new InvalidOperationException($"PRT probe {probeIndex} has an invalid geometry sample at {i}: " +
                        $"position={sample.position.ToString("R")}, normal={sample.normal.ToString("R")}, " +
                        $"albedo={sample.albedo.ToString("R")}, materialKey={sample.materialKey}, flags={sample.flags}, " +
                        $"renderingLayers=0x{sample.renderingLayerMask:X8}, objectLayer=0x{sample.objectLayerMask:X8}, " +
                        $"directionAndWeight={direction.ToString("R")}, normalSquared={sample.normal.sqrMagnitude:R}.");
                var key = new BrickKey(sample);
                if (!_brickLookup.TryGetValue(key, out int brickIndex))
                {
                    brickIndex = _bricks.Count;
                    _brickLookup.Add(key, brickIndex);
                    _bricks.Add(new Brick { renderingLayerMask = sample.renderingLayerMask, objectLayerMask = sample.objectLayerMask });
                }
                _bricks[brickIndex].Add(sample, _mergeDistance);
                factors.TryGetValue(brickIndex, out BrickTransfer factor);
                factor.brickIndex = brickIndex;
                factor.AddDirection(direction);
                factors[brickIndex] = factor;
            }
            _probeFactors[probeIndex] = factors;
            _probeSky[probeIndex] = sky;
            _probes[probeIndex] = new PRTProbeData { captureOffset = captureOffset, validity = validity };
            _captured[probeIndex] = true;
        }
        public PRTSectorData GenerateSector(Func<Vector3, int> nearestProbe)
        {
            if (_bricks.Count > BrickFactor.MaxBricks)
                throw new InvalidOperationException($"PRT sector has {_bricks.Count} bricks; at most {BrickFactor.MaxBricks} are supported. " +
                    "Reduce the sector width.");
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (Brick brick in _bricks)
                foreach (MergedSurfel merged in brick.surfels)
                {
                    if (!hasBounds) bounds = new Bounds(merged.position, Vector3.zero);
                    else bounds.Encapsulate(merged.position);
                    hasBounds = true;
                }
            var surfels = new List<Surfel>();
            var bricks = new SurfelIndices[_bricks.Count];
            for (int i = 0; i < _bricks.Count; i++)
            {
                Brick brick = _bricks[i];
                bricks[i] = new SurfelIndices
                {
                    start = surfels.Count, count = brick.surfels.Count,
                    renderingLayerMask = brick.renderingLayerMask, objectLayerMask = brick.objectLayerMask
                };
                foreach (MergedSurfel merged in brick.surfels)
                    surfels.Add(Surfel.Create(merged.position, merged.normal.normalized, merged.albedo, bounds,
                        nearestProbe(merged.position)));
            }
            var factors = new List<BrickFactor>();
            var sky = new List<uint>();
            for (int i = 0; i < _probes.Length; i++)
            {
                if (!_captured[i]) throw new InvalidOperationException($"PRT probe {i} was not captured.");
                PRTProbeData probe = _probes[i];
                probe.factorStart = factors.Count;
                probe.factorCount = _probeFactors[i].Count;
                var keys = new List<int>(_probeFactors[i].Keys);
                keys.Sort();
                foreach (int key in keys) factors.Add(_probeFactors[i][key].Encode());
                sky.AddRange(_probeSky[i]);
                _probes[i] = probe;
            }
            return new PRTSectorData { surfelBounds = bounds, surfels = surfels.ToArray(), bricks = bricks,
                factors = factors.ToArray(), probes = _probes, skyVisibility = sky.ToArray() };
        }
    }
}
