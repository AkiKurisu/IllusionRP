using UnityEngine;

namespace Illusion.Rendering
{
    public partial class IllusionRendererFeature
    {
        [SerializeField, Tooltip("Enable DLSS Neural Rendering when its Volume and optional UnityRHI backend are available.")]
        public bool dlssNeuralRendering = true;

        private IDLSSNeuralRenderingBackend _dlssNeuralRenderingBackend;

        public bool IsDLSSNeuralRenderingAvailable => dlssNeuralRendering && _dlssNeuralRenderingBackend is { IsAvailable: true };

        private void CreateDLSSNeuralRenderingBackend()
        {
            SafeDispose(ref _dlssNeuralRenderingBackend);
#if ILLUSION_DLSSNR_EXPERIMENTAL
            if (_renderPipelineResources != null && _renderPipelineResources.dlssNeuralRenderingPrepareInputsShader != null)
                _dlssNeuralRenderingBackend = DLSSNeuralRenderingBackendLoader.Create(
                    _renderPipelineResources.dlssNeuralRenderingPrepareInputsShader);
#endif
        }

        public void ResetDLSSNeuralRenderingHistory()
        {
            _dlssNeuralRenderingBackend?.ResetHistory();
        }
    }
}
