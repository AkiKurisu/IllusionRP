#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    // Decoded surfel for debug drawing.
    internal readonly struct PRTDebugSurfel
    {
        public readonly Vector3 position, Normal;
        public PRTDebugSurfel(Vector3 position, Vector3 normal) { this.position = position; Normal = normal; }
    }

    internal sealed class PRTProbeDebugData : IDisposable
    {
        public PRTDebugSurfel[] LocalSurfels { get; private set; } = Array.Empty<PRTDebugSurfel>();
        public int[] LocalSurfelIndices { get; private set; } = Array.Empty<int>();
        private bool _geometryReady;

        public static PRTDebugSurfel Decode(PRTSectorData sector, int index) =>
            new(sector.surfels[index].Position(sector.surfelBounds), sector.surfels[index].Normal);

        public void EnsureGeometry(PRTSectorData sector, PRTProbeData probe)
        {
            if (_geometryReady)
                return;
            _geometryReady = true;
            var geometry = new List<PRTDebugSurfel>();
            var indices = new List<int>();
            for (int i = probe.factorStart; i < probe.factorStart + probe.factorCount; i++)
            {
                SurfelIndices range = sector.bricks[sector.factors[i].BrickIndex];
                for (int index = range.start; index < range.start + range.count; index++)
                {
                    geometry.Add(Decode(sector, index));
                    indices.Add(index);
                }
            }
            LocalSurfels = geometry.ToArray();
            LocalSurfelIndices = indices.ToArray();
        }

        public void Dispose()
        {
            LocalSurfels = null;
            LocalSurfelIndices = null;
        }
    }
}
#endif
