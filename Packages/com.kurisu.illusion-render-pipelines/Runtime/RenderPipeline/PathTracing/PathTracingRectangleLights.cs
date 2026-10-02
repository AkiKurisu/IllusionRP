using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingRectangleLights : IDisposable
    {
        private sealed class Entry
        {
            public GameObject Object;
            public MeshRenderer Renderer;
            public Material Material;
        }

        private const string ProxyName = "PathTracingRectangleLight";
        private static readonly Dictionary<int, Entry> Entries = new();
        private static readonly HashSet<int> Active = new();
        private static readonly List<int> Stale = new();
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int LightTarget = Shader.PropertyToID("_PathTracingLightTarget");
        private static Mesh _mesh;
        private static int _owners;
        private readonly Shader _shader;
        private bool _disposed;

        public PathTracingRectangleLights(Shader shader)
        {
            if (!shader) throw new ArgumentNullException(nameof(shader));
            _shader = shader;
            if (_owners++ != 0) return;
            foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (renderer.name != ProxyName || renderer.gameObject.hideFlags != HideFlags.HideAndDontSave || !renderer.sharedMaterial || renderer.sharedMaterial.shader != shader)
                    continue;
                renderer.enabled = false;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh && filter.sharedMesh.name == ProxyName && filter.sharedMesh.hideFlags == HideFlags.HideAndDontSave)
                    CoreUtils.Destroy(filter.sharedMesh);
                CoreUtils.Destroy(renderer.sharedMaterial);
                CoreUtils.Destroy(renderer.gameObject);
            }
            _mesh = new Mesh { name = ProxyName, hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
            _mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            _mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            _mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.RecalculateBounds();
        }

        public void Update(PathTracingLightCollector collector)
        {
            Active.Clear();
            foreach (var light in collector.RectangleLights)
            {
                if (light.areaSize.x <= 0 || light.areaSize.y <= 0) continue;
                int id = light.GetInstanceID(); Active.Add(id);
                if (!Entries.TryGetValue(id, out var entry) || !entry.Object || !entry.Renderer || !entry.Material)
                {
                    if (entry != null) { Destroy(entry); Entries.Remove(id); }
                    var go = new GameObject(ProxyName, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.HideAndDontSave };
                    go.GetComponent<MeshFilter>().sharedMesh = _mesh;
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.DynamicTransform;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var material = CoreUtils.CreateEngineMaterial(_shader);
                    renderer.sharedMaterial = material;
                    entry = new Entry { Object = go, Renderer = renderer, Material = material };
                    Entries.Add(id, entry);
                    PathTracingInstanceTable.RegisterUnsavedRenderer(renderer);
                }
                if (entry.Object.layer != light.gameObject.layer) entry.Object.layer = light.gameObject.layer;
                var transform = entry.Object.transform;
                if (transform.position != light.transform.position || transform.rotation != light.transform.rotation)
                    transform.SetPositionAndRotation(light.transform.position, light.transform.rotation);
                var size = new Vector3(light.areaSize.x, light.areaSize.y, 1);
                if (transform.localScale != size) transform.localScale = size;
                int target = collector.GetTargetIndex(light);
                if (entry.Material.GetInt(LightTarget) != target) entry.Material.SetInt(LightTarget, target);
                var radiance = PathTracingLightCollector.RectangleRadiance(light);
                var emission = new Vector4(radiance.x, radiance.y, radiance.z, 1);
                if (entry.Material.GetVector(EmissionColor) != emission) entry.Material.SetVector(EmissionColor, emission);
            }
            Stale.Clear();
            foreach (int id in Entries.Keys) if (!Active.Contains(id)) Stale.Add(id);
            foreach (int id in Stale) { Destroy(Entries[id]); Entries.Remove(id); }
        }

        private static void Destroy(Entry entry)
        {
            if (entry.Object) entry.Object.SetActive(false);
            PathTracingInstanceTable.UnregisterUnsavedRenderer(entry.Renderer);
            CoreUtils.Destroy(entry.Material);
            CoreUtils.Destroy(entry.Object);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--_owners != 0) return;
            foreach (var entry in Entries.Values) Destroy(entry);
            Entries.Clear(); Active.Clear(); Stale.Clear();
            CoreUtils.Destroy(_mesh); _mesh = null;
        }
    }
}
