using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        internal void ObservePublication(Camera camera, Vector3Int minimum, Vector3Int count,
            RenderTexture coefficients, RenderTexture metadata, PRTRelightSolver solver)
        {
            if (!PRTRelightFrame.IsDriver(camera) && _mainCamera) return;
            _coefficientVoxelRT = coefficients;
            _validityVoxelRT = metadata;
            _mainCamera = camera;
            CurrentVoxelGrid = new Grid(count.x, count.y, count.z, probeGridSize);
            if (_boundingBoxMin != minimum || _probesInBoundingBox.Count != count.x * count.y * count.z)
            {
                _probesInBoundingBox.Clear();
                _boundingBoxMin = minimum;
                for (int x = minimum.x; x < minimum.x + count.x; x++)
                    for (int y = minimum.y; y < minimum.y + count.y; y++)
                        for (int z = minimum.z; z < minimum.z + count.z; z++)
                        {
                            int index = x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
                            _probesInBoundingBox.Add(Probes[index]);
                        }
                Vector3 first = transform.position + (Vector3)minimum * probeGridSize;
                Vector3 size = new Vector3(Mathf.Max(0, count.x - 1), Mathf.Max(0, count.y - 1), Mathf.Max(0, count.z - 1)) * probeGridSize;
                _currentBoundingBox = new Bounds(first + size / 2, size);
#if UNITY_EDITOR
                _lastClosestBoundingBoxMin = minimum;
                _lastClosestBoundingBoxCenter = _currentBoundingBox.center;
#endif
            }
            PublishedGeneration = solver.Generation;
        }

        private bool IsCameraInsideVolume(Vector3 position)
        {
            Vector3 coordinate = (position - transform.position) / probeGridSize;
            return coordinate.x >= 0 && coordinate.y >= 0 && coordinate.z >= 0 &&
                   coordinate.x <= probeSizeX - 1 && coordinate.y <= probeSizeY - 1 && coordinate.z <= probeSizeZ - 1;
        }
    }
}
