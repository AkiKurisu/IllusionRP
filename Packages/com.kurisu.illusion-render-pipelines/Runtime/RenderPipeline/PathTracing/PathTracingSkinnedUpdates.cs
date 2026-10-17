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
        private readonly List<SkinnedMeshRenderer> _forced = new();

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
                if (renderer is SkinnedMeshRenderer skinned && !skinned.updateWhenOffscreen)
                {
                    skinned.updateWhenOffscreen = true;
                    _forced.Add(skinned);
                }
            }
        }

        public void Restore()
        {
            foreach (var skinned in _forced)
            {
                if (skinned)
                    skinned.updateWhenOffscreen = false;
            }
            _forced.Clear();
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
