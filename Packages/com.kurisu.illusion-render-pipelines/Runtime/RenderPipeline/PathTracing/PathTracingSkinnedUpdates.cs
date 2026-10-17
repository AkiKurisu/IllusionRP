using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingSkinnedUpdates : IDisposable
    {
        private static readonly Dictionary<SkinnedMeshRenderer, (int Owners, bool Original)> Shared = new();

        private readonly HashSet<SkinnedMeshRenderer> _owned = new();

        private readonly Dictionary<PathTracingInstanceTable, int> _appliedVersions = new();

        public PathTracingSkinnedUpdates()
        {
#if UNITY_EDITOR
            EditorSceneManager.sceneSaving += OnSceneSaving;
#endif
        }

        public void Keep(PathTracingInstanceTable instances)
        {
            if (_appliedVersions.TryGetValue(instances, out int version) && version == instances.Version)
                return;
            _appliedVersions[instances] = instances.Version;
            foreach (var renderer in instances.Renderers)
            {
                if (renderer is not SkinnedMeshRenderer skinned || !_owned.Add(skinned))
                    continue;
                if (Shared.TryGetValue(skinned, out var entry))
                {
                    Shared[skinned] = (entry.Owners + 1, entry.Original);
                    continue;
                }
                Shared[skinned] = (1, skinned.updateWhenOffscreen);
                skinned.updateWhenOffscreen = true;
            }
        }

        public void Restore()
        {
            foreach (var skinned in _owned)
            {
                var entry = Shared[skinned];
                if (entry.Owners > 1)
                {
                    Shared[skinned] = (entry.Owners - 1, entry.Original);
                    continue;
                }
                Shared.Remove(skinned);
                if (skinned)
                    skinned.updateWhenOffscreen = entry.Original;
            }
            _owned.Clear();
            _appliedVersions.Clear();
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            EditorSceneManager.sceneSaving -= OnSceneSaving;
#endif
            Restore();
        }

#if UNITY_EDITOR
        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            Restore();
        }
#endif
    }
}
