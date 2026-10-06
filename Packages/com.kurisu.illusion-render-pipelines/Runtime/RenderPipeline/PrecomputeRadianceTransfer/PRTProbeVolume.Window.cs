using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        internal Bounds[] CascadeBounds { get; private set; } = System.Array.Empty<Bounds>();

        internal void ObservePublication(Camera camera, Vector3Int[] minimum, Vector3Int[] count,
            RenderTexture coefficients, PRTRelightSolver solver)
        {
            if (!PRTRelightFrame.IsDriver(camera) && _mainCamera) return;
            _coefficientVoxelRT = coefficients;
            _mainCamera = camera;
            CurrentVoxelGrid = new Grid(count[0].x, count[0].y, count[0].z, probeGridSize);
            if (CascadeBounds.Length != minimum.Length) CascadeBounds = new Bounds[minimum.Length];
            for (int level = 0; level < minimum.Length; level++)
            {
                float spacing = probeGridSize * (1 << level);
                Vector3 first = transform.position + (Vector3)minimum[level] * spacing;
                Vector3 size = (Vector3)Vector3Int.Max(Vector3Int.zero, count[level] - Vector3Int.one) * spacing;
                CascadeBounds[level] = new Bounds(first + size / 2, size);
            }
            _currentBoundingBox = CascadeBounds[0];
            if (_boundingBoxMin != minimum[0] || _probesInBoundingBox.Count != count[0].x * count[0].y * count[0].z)
            {
                _probesInBoundingBox.Clear();
                _boundingBoxMin = minimum[0];
                for (int x = minimum[0].x; x < minimum[0].x + count[0].x; x++)
                    for (int y = minimum[0].y; y < minimum[0].y + count[0].y; y++)
                        for (int z = minimum[0].z; z < minimum[0].z + count[0].z; z++)
                            _probesInBoundingBox.Add(Probes[x * probeSizeY * probeSizeZ + y * probeSizeZ + z]);
            }
            PublishedGeneration = solver.Generation;
        }
    }
}
