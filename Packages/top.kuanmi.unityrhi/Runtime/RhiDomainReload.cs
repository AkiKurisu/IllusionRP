using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityRhi
{
    /// <summary>
    /// Registry of RHI-resource owners that must be disposed deterministically
    /// before a domain reload.
    ///
    /// Unity fires <c>AssemblyReloadEvents.beforeAssemblyReload</c> *before* it
    /// disposes ScriptableRendererFeatures (their OnDisable/OnDestroy run one
    /// step later). The leak checker runs on that beforeAssemblyReload
    /// callback, so without this an owner's entire live working set is still
    /// strongly reachable and reports as a leak even though it is released
    /// microseconds later. Owners register on creation; the editor reload hook
    /// drains the registry first, then counts what genuinely leaked.
    /// </summary>
    public static class RhiDomainReload
    {
        private static readonly List<IDisposable> s_owners = new List<IDisposable>();

        /// <summary>
        /// Track an owner so it is disposed before the next domain reload.
        /// Idempotent; safe to call from Create() that Unity may invoke
        /// repeatedly. Destroyed Unity owners are pruned here.
        /// </summary>
        public static void RegisterOwner(IDisposable owner)
        {
            if (owner == null)
                return;
            PruneDead();
            if (!s_owners.Contains(owner))
                s_owners.Add(owner);
        }

        /// <summary>Stop tracking an owner. Safe to call when never registered.</summary>
        public static void UnregisterOwner(IDisposable owner)
        {
            if (owner != null)
                s_owners.Remove(owner);
        }

        /// <summary>
        /// Dispose every registered owner. Snapshots first because Dispose may
        /// mutate the registry, and clears it so a failed Dispose cannot strand
        /// an owner for a second attempt. Destroyed Unity owners are skipped.
        /// </summary>
        public static void DisposeRegisteredOwners()
        {
            if (s_owners.Count == 0)
                return;
            var snapshot = s_owners.ToArray();
            s_owners.Clear();
            foreach (IDisposable owner in snapshot)
            {
                if (owner is UnityEngine.Object unityObject && unityObject == null)
                    continue;
                try
                {
                    owner.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // Unity "fake-null": a destroyed ScriptableObject compares == null while
        // its managed wrapper lingers. Drop those so the registry does not pin
        // dead owners between reloads.
        private static void PruneDead()
        {
            for (int i = s_owners.Count - 1; i >= 0; --i)
                if (s_owners[i] is UnityEngine.Object unityObject && unityObject == null)
                    s_owners.RemoveAt(i);
        }
    }
}
