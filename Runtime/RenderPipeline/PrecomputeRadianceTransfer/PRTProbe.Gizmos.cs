#if UNITY_EDITOR
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbe
    {
        private readonly MaterialPropertyBlock _matPropBlock;

        /// <summary>
        /// Debug renderer
        /// </summary>
        private readonly MeshRenderer _renderer;
        
        /// <summary>
        /// Update Probe debug visibility based on debug mode
        /// </summary>
        internal void UpdateVisibility()
        {
            // Update position and scale
            _renderer.transform.position = Position;
            float size = _volume.probeHandleSize;
            _renderer.transform.localScale = new Vector3(size, size, size);
            
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
            _renderer.enabled = shouldShowIrradianceSphere;

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
            Object.DestroyImmediate(_renderer.gameObject);
        }
        
        private static class ShaderProperties
        {
            public static readonly int CoefficientSH9 = Shader.PropertyToID("_coefficientSH9");
            public static readonly int TintColor = Shader.PropertyToID("_TintColor");
        }
    }
}
#endif