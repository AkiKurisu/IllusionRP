#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTProbeDebugData : IDisposable
    {
        public Surfel[] LocalSurfels { get; private set; } = Array.Empty<Surfel>();
        public int[] LocalSurfelIndices { get; private set; } = Array.Empty<int>();
        public GraphicsBuffer CoefficientSH9 { get; }
        private bool _geometryReady;

        public PRTProbeDebugData()
        {
            CoefficientSH9 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 27, 4);
            CoefficientSH9.SetData(new float[27]);
        }

        public void EnsureGeometry(PRTProbeData probe, BrickFactor[] factors, SurfelIndices[] bricks, Surfel[] surfels)
        {
            if (_geometryReady)
                return;
            _geometryReady = true;
            var geometry = new List<Surfel>();
            var indices = new List<int>();
            for (int i = probe.factorStart; i < probe.factorStart + probe.factorCount; i++)
            {
                SurfelIndices range = bricks[factors[i].brickIndex];
                for (int index = range.start; index < range.start + range.count; index++)
                {
                    geometry.Add(surfels[index]);
                    indices.Add(index);
                }
            }
            LocalSurfels = geometry.ToArray();
            LocalSurfelIndices = indices.ToArray();
        }

        public void Dispose()
        {
            CoefficientSH9.Dispose();
            LocalSurfels = null;
            LocalSurfelIndices = null;
        }
    }
}
#endif
