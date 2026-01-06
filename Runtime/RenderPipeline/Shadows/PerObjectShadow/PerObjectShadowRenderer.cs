using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Illusion.Rendering.Shadows
{
    [ExecuteAlways, DisallowMultipleComponent]
    public class PerObjectShadowRenderer : MonoBehaviour
    {
        [Serializable]
        private class ShadowCasterCluster : IShadowCaster
        {
            private readonly PerObjectShadowRenderer _renderer;

            private int _shadowCasterId = -1;

            int IShadowCaster.Id { get => _shadowCasterId; set => _shadowCasterId = value; }

            float IShadowCaster.Priority { get; set; }

            private readonly ShadowRendererList _shadowRendererList = new();

            ShadowRendererList.ReadOnly IShadowCaster.RendererList => _shadowRendererList.AsReadOnly();
            
            public readonly List<Renderer> Renderers = new();

            private readonly Lazy<MaterialPropertyBlock> _propertyBlock = new();

            Transform IShadowCaster.Transform => _renderer.transform;

            public ShadowCasterCluster(PerObjectShadowRenderer renderer, Renderer renderObject)
            {
                _renderer = renderer;
                _shadowRendererList.RenderObject = renderObject;
                _shadowRendererList.BoundType = ShadowBoundType.Calculated;
            }

            bool IShadowCaster.CanCastShadow()
            {
                if (!_renderer) return false;
                
                if (!_renderer.isActiveAndEnabled)
                {
                    return false;
                }

                return _renderer.isCastingShadow;
            }

            public void Update()
            {
                _shadowRendererList.Clear();
                foreach (var renderer in Renderers)
                {
                    if (renderer)
                    {
                        _shadowRendererList.Add(renderer);
                    }
                }
            }

            public void UpdateMaterialProperties()
            {
                using (UnityEngine.Pool.ListPool<(int, float)>.Get(out var floats))
                {
                    floats.Add((IllusionShaderProperties._PerObjShadowCasterId, _shadowCasterId));

                    foreach (var r in Renderers)
                    {
                        if (!r) continue;
                        IllusionRenderingUtils.SetMaterialProperties(r, _propertyBlock, floats);
                    }
                }
            }

            public bool TryGetWorldBounds(out Bounds worldBounds, ICollection<int> outAppendRendererIndices = null)
            {
                return _shadowRendererList.TryGetWorldBounds(out worldBounds, outAppendRendererIndices);
            }

            public void Reset()
            {
                Renderers.Clear();
            }
        }
        
        [Serializable]
        public class ShadowCasterClusterData
        {
            public Renderer renderObject;

            public Renderer[] renderers;
        }

        public bool isCastingShadow = true;

        public ShadowCasterClusterData[] clusterData;

        public uint renderingLayerMask;

        private readonly Lazy<MaterialPropertyBlock> _propertyBlock = new();
        
        private ShadowCasterCluster[] _casterClusters;

        private void Awake()
        {
            AllocateClusters();
            SetupRenderingLayers(renderingLayerMask);
        }

        private void OnEnable()
        {
            SafeCheck_Editor();
            RegisterClusters();
        }

        private void OnDisable()
        {
            UnregisterClusters();
            if (_propertyBlock.IsValueCreated)
            {
                _propertyBlock.Value.Clear();
            }
        }

        private void AllocateClusters()
        {
            if (clusterData == null || clusterData.Length == 0) return;
            
            _casterClusters = clusterData.Select(item =>
            {
                var cluster = new ShadowCasterCluster(this, item.renderObject);
                if (!item.renderers.Contains(item.renderObject))
                {
                    cluster.Renderers.Add(item.renderObject);
                }
                cluster.Renderers.AddRange(item.renderers);
                cluster.Update();
                return cluster;
            }).ToArray();
        }

        private void RegisterClusters()
        {
            if (_casterClusters == null) return;
            
            foreach (var group in _casterClusters)
            {
                ShadowCasterManager.Register(group);
            }
        }

        private void UnregisterClusters()
        {
            if (_casterClusters == null) return;
            
            foreach (var group in _casterClusters)
            {
                ShadowCasterManager.Unregister(group);
            }
        }

        [Conditional("UNITY_EDITOR")]
        private void OnValidate()
        {
            UnregisterClusters();
            AllocateClusters();
            SetupRenderingLayers(renderingLayerMask);
            if (enabled)
            {
                RegisterClusters();
            }
        }

        [Conditional("UNITY_EDITOR")]
        private void OnDrawGizmosSelected()
        {
            SafeCheck_Editor();

            foreach (var cluster in _casterClusters)
            {
                if (!cluster.TryGetWorldBounds(out Bounds bounds))
                {
                    return;
                }

                Color color = Gizmos.color;
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
                Gizmos.color = color;
            }
        }

        [Conditional("UNITY_EDITOR")]
        private void SafeCheck_Editor()
        {
            if (Application.isPlaying) return;

            bool reallocate = _casterClusters == null;
            if (_casterClusters != null)
            {
                foreach (var cluster in _casterClusters)
                {
                    reallocate |= cluster.Renderers == null;
                }
            }

            if (reallocate)
            {
                AllocateClusters();
            }
        }
        
        private void SetupRenderingLayers(uint inRenderingLayerMask)
        {
            if (_casterClusters == null) return;
            
            foreach (var cluster in _casterClusters)
            {
                foreach (var renderer in cluster.Renderers)
                {
                    if (!renderer) continue;
                    renderer.renderingLayerMask = inRenderingLayerMask;
                }
            }
        }
    }
}
