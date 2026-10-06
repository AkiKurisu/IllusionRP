using System;
using System.Collections.Generic;
using Illusion.Rendering.PRTGI;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    internal sealed class PRTMeshVirtualOffset
    {
        private struct Triangle
        {
            public Vector3 a, edge1, edge2, normal, center;
            public Bounds bounds;
        }
        private struct Node
        {
            public Bounds bounds;
            public int start, count, left, right;
        }
        private sealed class TriangleComparer : IComparer<Triangle>
        {
            internal int axis;
            public int Compare(Triangle a, Triangle b) => a.center[axis].CompareTo(b.center[axis]);
        }
        private readonly List<Triangle> _input = new();
        private readonly List<Node> _nodes = new();
        private readonly TriangleComparer _comparer = new();
        private Triangle[] _triangles;
        private static readonly Vector3[] Directions = BuildDirections();

        internal void AddMesh(Mesh mesh, Matrix4x4 matrix, bool[] solidSubmeshes)
        {
            using var dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh);
            Mesh.MeshData data = dataArray[0];
            using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            data.GetVertices(vertices);
            float orientation = matrix.determinant < 0f ? -1f : 1f;
            for (int submesh = 0; submesh < data.subMeshCount; submesh++)
            {
                if (submesh >= solidSubmeshes.Length || !solidSubmeshes[submesh]) continue;
                SubMeshDescriptor descriptor = data.GetSubMesh(submesh);
                if (descriptor.topology != MeshTopology.Triangles) continue;
                using var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp);
                data.GetIndices(indices, submesh);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                    Vector3 b = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    Vector3 c = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]);
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-16f) continue;
                    Bounds bounds = new(a, Vector3.zero);
                    bounds.Encapsulate(b);
                    bounds.Encapsulate(c);
                    _input.Add(new Triangle { a = a, edge1 = b - a, edge2 = c - a,
                        normal = normal.normalized * orientation, center = (a + b + c) / 3f, bounds = bounds });
                }
            }
        }

        internal void Build()
        {
            _triangles = _input.ToArray();
            _input.Clear();
            if (_triangles.Length > 0) BuildNode(0, _triangles.Length);
        }
        private int BuildNode(int start, int count)
        {
            Bounds bounds = _triangles[start].bounds;
            for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(_triangles[i].bounds);
            int index = _nodes.Count;
            _nodes.Add(new Node { bounds = bounds, start = start, count = count, left = -1, right = -1 });
            if (count <= 8) return index;
            Vector3 size = bounds.size;
            _comparer.axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Array.Sort(_triangles, start, count, _comparer);
            int middle = count / 2;
            int left = BuildNode(start, middle), right = BuildNode(start + middle, count - middle);
            _nodes[index] = new Node { bounds = bounds, start = start, count = 0, left = left, right = right };
            return index;
        }

        internal PRTProbePlacement Place(Vector3 position, float geometryBias, float rayOriginBias, float searchDistance)
        {
            Vector3 current = position;
            for (int step = 0; step < 4; step++)
            {
                int front = 0, back = 0;
                float nearest = float.PositiveInfinity;
                Vector3 exitPosition = current;
                foreach (Vector3 direction in Directions)
                {
                    Ray ray = new(current - direction * rayOriginBias, direction);
                    // Occupancy needs unbounded rays: a probe deeper inside geometry than the search distance would
                    // otherwise see no faces and pass as outside. The search distance only limits the exit offset.
                    if (!Trace(ray, float.PositiveInfinity, out float distance, out Vector3 normal)) continue;
                    if (Vector3.Dot(direction, normal) <= 0f) { front++; continue; }
                    back++;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        exitPosition = ray.GetPoint(distance) + normal * geometryBias;
                    }
                }
                if (back <= front) return new PRTProbePlacement(current - position, true);
                if ((exitPosition - position).sqrMagnitude > searchDistance * searchDistance)
                    return new PRTProbePlacement(current - position, false);
                current = exitPosition;
            }
            return new PRTProbePlacement(current - position, false);
        }

        private bool Trace(Ray ray, float maximum, out float distance, out Vector3 normal)
        {
            distance = maximum;
            normal = Vector3.zero;
            if (_nodes.Count == 0) return false;
            bool found = false;
            TraceNode(0, ray, ref distance, ref normal, ref found);
            return found;
        }
        private void TraceNode(int index, Ray ray, ref float distance, ref Vector3 normal, ref bool found)
        {
            Node node = _nodes[index];
            if (!Intersects(node.bounds, ray, distance)) return;
            if (node.count == 0)
            {
                TraceNode(node.left, ray, ref distance, ref normal, ref found);
                TraceNode(node.right, ray, ref distance, ref normal, ref found);
                return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                Triangle t = _triangles[i];
                Vector3 p = Vector3.Cross(ray.direction, t.edge2);
                float determinant = Vector3.Dot(t.edge1, p);
                if (Mathf.Abs(determinant) < 1e-8f) continue;
                float inverse = 1f / determinant;
                Vector3 delta = ray.origin - t.a;
                float u = Vector3.Dot(delta, p) * inverse;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(delta, t.edge1);
                float v = Vector3.Dot(ray.direction, q) * inverse;
                if (v < 0f || u + v > 1f) continue;
                float d = Vector3.Dot(t.edge2, q) * inverse;
                if (d <= 1e-6f || d >= distance) continue;
                distance = d;
                normal = t.normal;
                found = true;
            }
        }
        private static bool Intersects(Bounds bounds, Ray ray, float maximum)
        {
            Vector3 min = bounds.min, max = bounds.max;
            float near = 0f, far = maximum;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = ray.direction[axis], o = ray.origin[axis];
                if (Mathf.Abs(d) < 1e-8f) { if (o < min[axis] || o > max[axis]) return false; continue; }
                float a = (min[axis] - o) / d, b = (max[axis] - o) / d;
                near = Mathf.Max(near, Mathf.Min(a, b));
                far = Mathf.Min(far, Mathf.Max(a, b));
                if (near > far) return false;
            }
            return true;
        }
        private static Vector3[] BuildDirections()
        {
            var result = new List<Vector3>(26);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (x != 0 || y != 0 || z != 0) result.Add(new Vector3(x, y, z).normalized);
            return result.ToArray();
        }
    }
}
