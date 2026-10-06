#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTSectorBake
    {
        private readonly PRTProbeGrid _grid;
        private readonly int _width, _columnsZ;
        private readonly SurfelGrid[] _sectors;
        private readonly List<int>[] _ids;
        private readonly int[] _localIds;
        public PRTProbeMetadata[] Metadata { get; }

        public PRTSectorBake(PRTProbeGrid grid, int width, float mergeDistance)
        {
            _grid = grid;
            _width = width;
            _columnsZ = (grid.count.z + width - 1) / width;
            int count = (grid.count.x + width - 1) / width * _columnsZ;
            _ids = new List<int>[count];
            _sectors = new SurfelGrid[count];
            _localIds = new int[grid.ProbeCount];
            Metadata = new PRTProbeMetadata[grid.ProbeCount];
            for (int i = 0; i < count; i++) _ids[i] = new List<int>();
            for (int i = 0; i < grid.ProbeCount; i++)
            {
                var ids = _ids[SectorIndex(i)];
                _localIds[i] = ids.Count;
                ids.Add(i);
            }
            for (int i = 0; i < count; i++) _sectors[i] = new SurfelGrid(_ids[i].Count, mergeDistance);
        }

        private int SectorIndex(int id) => id / (_grid.count.y * _grid.count.z) / _width * _columnsZ
            + id % _grid.count.z / _width;

        // Each sector accumulates its own probes in id order on one thread, so results do not depend on scheduling.
        public void AddProbes(int first, PRTProbeBakeSamples[] captures, Vector4[] directions, Vector3[] offsets, uint[] validity)
        {
            for (int i = 0; i < captures.Length; i++)
                Metadata[first + i] = new PRTProbeMetadata { captureOffset = offsets[i], validity = validity[i] };
            Run(() => Parallel.ForEach(Enumerable.Range(0, captures.Length).GroupBy(i => SectorIndex(first + i)), group =>
            {
                foreach (int i in group)
                    _sectors[group.Key].AddProbe(_localIds[first + i], captures[i].surfels, directions, offsets[i], validity[i]);
            }));
        }

        private static void Run(Action action)
        {
            try { action(); }
            catch (AggregateException exception) { ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw(); }
        }

        public PRTSectorData[] Complete()
        {
            var valid = new List<int>();
            for (int id = 0; id < Metadata.Length; id++)
                if ((Metadata[id].validity >> 24) != 0) valid.Add(id);
            var nearest = new PRTNearestProbe(_grid, valid);
            var result = new PRTSectorData[_sectors.Length];
            Run(() => Parallel.For(0, result.Length, i =>
            {
                var sector = _sectors[i].GenerateSector(nearest.Find);
                sector.coordinate = new Vector2Int(i / _columnsZ, i % _columnsZ);
                sector.probeIds = _ids[i].ToArray();
                if (sector.surfels.Length == 0)
                    sector.surfelBounds = new Bounds(_grid.GetPosition(sector.probeIds[0]), Vector3.zero);
                result[i] = sector;
            }));
            return result;
        }
    }
}
#endif
