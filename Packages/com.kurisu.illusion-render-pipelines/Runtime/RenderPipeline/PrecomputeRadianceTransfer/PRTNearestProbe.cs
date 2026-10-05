#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTNearestProbe
    {
        private struct Node { public int id, left, right, axis; public Vector3 position; }
        private readonly Node[] _nodes;
        private readonly PRTProbeGrid _grid;
        private int _count;
        private readonly int _root;

        public PRTNearestProbe(PRTProbeGrid grid, List<int> valid)
        {
            _grid = grid;
            _nodes = new Node[valid.Count];
            _root = Build(valid.ToArray(), 0, valid.Count, 0);
        }

        private int Build(int[] ids, int start, int count, int depth)
        {
            if (count == 0) return -1;
            int axis = depth % 3;
            Array.Sort(ids, start, count, Comparer<int>.Create((a, b) =>
            {
                int order = _grid.GetPosition(a)[axis].CompareTo(_grid.GetPosition(b)[axis]);
                return order != 0 ? order : a.CompareTo(b);
            }));
            int middle = start + count / 2, index = _count++;
            _nodes[index] = new Node { id = ids[middle], position = _grid.GetPosition(ids[middle]), axis = axis,
                left = Build(ids, start, middle - start, depth + 1),
                right = Build(ids, middle + 1, start + count - middle - 1, depth + 1) };
            return index;
        }

        public int Find(Vector3 position)
        {
            int best = -1;
            double distance = double.PositiveInfinity;
            Search(_root, position, ref best, ref distance);
            return best;
        }

        private void Search(int index, Vector3 position, ref int best, ref double distance)
        {
            if (index < 0) return;
            var node = _nodes[index];
            double x = (double)node.position.x - position.x, y = (double)node.position.y - position.y,
                z = (double)node.position.z - position.z;
            double candidate = x * x + y * y + z * z;
            if (candidate < distance || candidate == distance && node.id < best)
            { best = node.id; distance = candidate; }
            double delta = (double)position[node.axis] - node.position[node.axis];
            Search(delta < 0 ? node.left : node.right, position, ref best, ref distance);
            if (delta * delta <= distance)
                Search(delta < 0 ? node.right : node.left, position, ref best, ref distance);
        }
    }
}
#endif
