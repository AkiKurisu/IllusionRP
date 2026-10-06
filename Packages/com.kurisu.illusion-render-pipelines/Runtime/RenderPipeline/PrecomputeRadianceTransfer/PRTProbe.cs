using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public class PRTProbe
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
    }
}
