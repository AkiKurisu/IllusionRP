#if UNITY_EDITOR
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbe
    {
        private MaterialPropertyBlock _matPropBlock;

        /// <summary>
        /// Debug renderer
        /// </summary>
        private MeshRenderer _renderer;
        
        /// <summary>
        /// Update Probe debug visibility based on debug mode
        /// </summary>
        internal void UpdateVisibility()
        {
            bool shouldShowIrradianceSphere = _volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance;
            
            // Hide when is selected and using other debug modes
            bool isSelected = _volume.selectedProbeIndex == Index && Index != -1;
            if (isSelected && _volume.selectedProbeDebugMode != ProbeDebugMode.IrradianceSphere)
            {
                shouldShowIrradianceSphere = false;
            }
            
            // Hide when show surfel brick to prevent hide surfel gizmos
            shouldShowIrradianceSphere &= _volume.selectedProbeDebugMode != ProbeDebugMode.SurfelBrickGrid;
            shouldShowIrradianceSphere &= !PRTVolumeManager.IsBaking;
            if (!shouldShowIrradianceSphere)
            {
                if (_renderer)
                    _renderer.enabled = false;
                return;
            }
            if (!_renderer)
                CreateDebugObject();
            _renderer.transform.position = Position;
            _renderer.transform.localScale = Vector3.one * _volume.probeHandleSize;
            _renderer.enabled = true;

            // Update material properties if sphere is visible
            if (shouldShowIrradianceSphere)
            {
                UpdateIrradianceSphereShader();
            }
        }

        /// <summary>
        /// Update irradiance sphere shader properties
        /// </summary>
        private void UpdateIrradianceSphereShader()
        {
            if (!_renderer || !_volume)
                return;

            var debugData = _volume.GetProbeDebugData(Index);
            if (debugData == null) return;
            
            // Check if probe is invalidated, if so render as black
            if (!_volume.IsProbeValid(Index))
            {
                // Set all SH coefficients to zero to render as black
                _matPropBlock.SetBuffer(ShaderProperties.CoefficientSH9, debugData.CoefficientSH9);
                _matPropBlock.SetColor(ShaderProperties.TintColor, Color.black);
            }
            else
            {
                _matPropBlock.SetBuffer(ShaderProperties.CoefficientSH9, debugData.CoefficientSH9);
                _matPropBlock.SetColor(ShaderProperties.TintColor, Color.white);
            }
            
            _renderer.SetPropertyBlock(_matPropBlock);
        }

        private void ReleaseDebugObject()
        {
            if (!_renderer)
                return;
            UnityEngine.Rendering.CoreUtils.Destroy(_renderer.sharedMaterial);
            UObject.DestroyImmediate(_renderer.gameObject);
            _renderer = null;
        }

        private void CreateDebugObject()
        {
            var probeObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probeObject.name = $"PRTProbe {Index}";
            probeObject.hideFlags = HideFlags.HideAndDontSave;
            probeObject.transform.SetParent(_volume.transform);
            UObject.DestroyImmediate(probeObject.GetComponent<SphereCollider>());
            _renderer = probeObject.GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sharedMaterial = UnityEngine.Rendering.CoreUtils.CreateEngineMaterial(IllusionShaders.ProbeSHDebug);
            _matPropBlock = new MaterialPropertyBlock();
        }
        
        private static class ShaderProperties
        {
            public static readonly int CoefficientSH9 = Shader.PropertyToID("_coefficientSH9");
            public static readonly int TintColor = Shader.PropertyToID("_TintColor");
        }
    }
}
#endif
