using UnityEngine;

namespace Illusion.Rendering
{
    internal static class DLSSNeuralRenderingCameraPolicy
    {
        internal static bool ShouldRender(CameraType cameraType, bool resolveFinalTarget,
            bool postProcessEnabled = true)
        {
            return cameraType == CameraType.Game && resolveFinalTarget && postProcessEnabled;
        }
    }
}
