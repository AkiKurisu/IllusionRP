#if CERES_INSTALL
using Ceres.Capture;
using Illusion.Rendering.Shadows;
using UnityEngine;
using UnityEngine.Scripting;

namespace Illusion.Rendering
{
    [Preserve]
    internal sealed class IllusionCeresCaptureHooks : IRenderPipelineCaptureHooks
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            RenderPipelineCaptureHooks.Current ??= new IllusionCeresCaptureHooks();
        }

        public bool TryGetTemporalCaptureStatus(Camera camera, out TemporalCaptureStatus status)
        {
            var rendererData = IllusionRendererData.Active;
            if (rendererData == null || !rendererData.TryGetTemporalCaptureStatus(camera, out var source))
            {
                status = default;
                return false;
            }

            status = new TemporalCaptureStatus(source.HasCameraState, source.FrameCount, source.RecommendedWarmupFrames,
                source.IsReady, source.Blockers.ToString());
            return true;
        }

        public void CopyCameraData(Camera source, Camera target)
        {
            var sourceData = source.GetComponent<PerObjectShadowLightSource>();
            var targetData = target.GetComponent<PerObjectShadowLightSource>();
            if (!sourceData)
            {
                if (targetData)
                {
                    targetData.Source = null;
                    targetData.enabled = false;
                }

                return;
            }

            if (!targetData)
            {
                targetData = target.gameObject.AddComponent<PerObjectShadowLightSource>();
            }

            targetData.Source = sourceData.Source;
            targetData.enabled = sourceData.isActiveAndEnabled;
        }
    }
}
#endif
