using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

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
#if UNITY_EDITOR
            var probeObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probeObject.name = $"PRTProbe {index}";
            Object.DestroyImmediate(probeObject.GetComponent<SphereCollider>());
            _renderer = probeObject.GetComponent<MeshRenderer>();
            _renderer.material = CoreUtils.CreateEngineMaterial(IllusionShaders.ProbeSHDebug);
            _renderer.enabled = false;
            probeObject.hideFlags = HideFlags.HideAndDontSave;
            probeObject.transform.SetParent(probeVolume.transform);
            probeObject.transform.position = Position;
            float size = _volume.probeHandleSize;
            probeObject.transform.localScale = new Vector3(size, size, size);
            _matPropBlock = new MaterialPropertyBlock();
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            ReleaseDebugObject();
#endif
        }
    }
}