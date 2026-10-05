using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTSectorScheduler
    {
        private readonly PRTProbeVolumeAsset _asset;
        private readonly List<int> _requested = new();
        private int _background;
        private bool _backgroundTurn;
        public int NearSector { get; private set; }
        public int BackgroundSector => _background;
        public int UpdatedCount { get; private set; }

        public PRTSectorScheduler(PRTProbeVolumeAsset asset) => _asset = asset;

        public IReadOnlyList<int> Select(Vector3 position, int budget)
        {
            var grid = _asset.Grid;
            var coordinate = (position - grid.origin) / grid.spacing - (Vector3)grid.min;
            int x = Mathf.Clamp(Mathf.FloorToInt(coordinate.x), 0, grid.count.x - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(coordinate.z), 0, grid.count.z - 1);
            NearSector = _asset.SectorIndex(x * grid.count.y * grid.count.z + z);
            _requested.Clear();
            UpdatedCount = 0;
            if (budget == 1)
            {
                _requested.Add(_backgroundTurn ? _background : NearSector);
                _backgroundTurn = !_backgroundTurn;
            }
            else
            {
                _requested.Add(NearSector);
                if (_background != NearSector) _requested.Add(_background);
                for (int offset = 1; _requested.Count < Mathf.Min(budget, _asset.Sectors.Length) && offset < _asset.Sectors.Length; offset++)
                {
                    int index = (_background + offset) % _asset.Sectors.Length;
                    if (!_requested.Contains(index)) _requested.Add(index);
                }
            }
            return _requested;
        }

        public void Updated(int sector)
        {
            UpdatedCount++;
            if (sector == _background) _background = (_background + 1) % _asset.Sectors.Length;
        }
    }
}
