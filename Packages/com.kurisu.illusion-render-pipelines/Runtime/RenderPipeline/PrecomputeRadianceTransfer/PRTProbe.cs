using System;
using UnityEngine;
using UnityEngine.Rendering;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbe : IDisposable
    {
        /// <summary>
        /// Index in the volume grid
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// World position of this probe
        /// </summary>
        public Vector3 Position => _volume.transform.position + _relativePosition;

        private readonly Vector3 _relativePosition;

        private readonly PRTProbeVolume _volume;

        internal PRTProbe(int index, Vector3 relativePosition, PRTProbeVolume probeVolume)
        {
            Index = index;
            _relativePosition = relativePosition;
            _volume = probeVolume;
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            ReleaseDebugObject();
#endif
        }
    }
}
