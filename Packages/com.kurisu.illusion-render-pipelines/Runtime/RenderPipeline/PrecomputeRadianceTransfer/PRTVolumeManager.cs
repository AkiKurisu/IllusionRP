using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public class PRTVolumeManager
    {
        private static PRTProbeVolume _instance;

        /// <summary>
        /// Current probe volume in the scene.
        /// </summary>
        public static PRTProbeVolume ProbeVolume
        {
            get
            {
                if (!_instance) return null;
                return _instance;
            }
            private set => _instance = value;
        }

        /// <summary>
        /// Is any probe volume in baking.
        /// </summary>
        internal static bool IsBaking { get; private protected set; }

        /// <summary>
        /// Registered adjustment volumes for efficient access
        /// </summary>
        private static readonly List<PRTProbeAdjustmentVolume> AdjustmentVolumeList = new();

        /// <summary>
        /// Get all registered <see cref="PRTProbeAdjustmentVolume"/>
        /// </summary>
        public static IReadOnlyList<PRTProbeAdjustmentVolume> AdjustmentVolumes => AdjustmentVolumeList;

        private static readonly Dictionary<ReflectionProbe, ReflectionProbeAdditionalData> ReflectionProbeAdditionalDataDict = new();
        
        /// <summary>
        /// Get all registered <see cref="ReflectionProbeAdditionalData"/>
        /// </summary>
        protected static IReadOnlyCollection<ReflectionProbeAdditionalData> ReflectionProbeAdditionalData => ReflectionProbeAdditionalDataDict.Values;

        /// <summary>
        /// Register a probe volume
        /// </summary>
        /// <param name="volume"></param>
        internal static void RegisterProbeVolume(PRTProbeVolume volume)
        {
            ProbeVolume = volume;
        }

        /// <summary>
        /// Register a probe volume
        /// </summary>
        /// <param name="volume"></param>
        internal static void UnregisterProbeVolume(PRTProbeVolume volume)
        {
            if (ProbeVolume == volume)
            {
                ProbeVolume = null;
            }
        }

        /// <summary>
        /// Register an adjustment volume
        /// </summary>
        /// <param name="volume">Adjustment volume to register</param>
        internal static void RegisterAdjustmentVolume(PRTProbeAdjustmentVolume volume)
        {
            if (volume&& !AdjustmentVolumeList.Contains(volume))
            {
                AdjustmentVolumeList.Add(volume);
            }
        }

        /// <summary>
        /// Unregister an adjustment volume
        /// </summary>
        /// <param name="volume">Adjustment volume to unregister</param>
        internal static void UnregisterAdjustmentVolume(PRTProbeAdjustmentVolume volume)
        {
            if (volume)
            {
                AdjustmentVolumeList.Remove(volume);
            }
        }

        /// <summary>
        /// Register a reflection probe additional data
        /// </summary>
        /// <param name="reflectionProbe"></param>
        /// <param name="additionalData"></param>
        internal static void RegisterReflectionProbeAdditionalData(ReflectionProbe reflectionProbe, ReflectionProbeAdditionalData additionalData)
        {
            if (reflectionProbe)
            {
                ReflectionProbeAdditionalDataDict[reflectionProbe] = additionalData;
            }
        }

        /// <summary>
        /// Unregister a reflection probe additional data
        /// </summary>
        /// <param name="reflectionProbe"></param>
        /// <param name="additionalData"></param>
        internal static void UnregisterReflectionProbeAdditionalData(ReflectionProbe reflectionProbe, ReflectionProbeAdditionalData additionalData)
        {
            if (reflectionProbe && ReflectionProbeAdditionalDataDict.TryGetValue(reflectionProbe, out var data) && data == additionalData)
            {
                ReflectionProbeAdditionalDataDict.Remove(reflectionProbe);
            }
        }

        /// <summary>
        /// Try get <see cref="ReflectionProbeAdditionalData"/> from <see cref="ReflectionProbe"/>
        /// </summary>
        /// <param name="reflectionProbe"></param>
        /// <param name="additionalData"></param>
        /// <returns></returns>
        public static bool TryGetReflectionProbeAdditionalData(ReflectionProbe reflectionProbe, out ReflectionProbeAdditionalData additionalData)
        {
            return ReflectionProbeAdditionalDataDict.TryGetValue(reflectionProbe, out additionalData);
        }
    }
}