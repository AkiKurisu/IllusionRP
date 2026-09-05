using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    internal static class IllusionRendererFeatureUtility
    {
        /// <summary>
        /// Finds the Illusion renderer feature on the default renderer of the active URP asset.
        /// </summary>
        public static bool TryGetDefault(out IllusionRendererFeature feature)
        {
            feature = null;
            var asset = UniversalRenderPipeline.asset;
            if (!asset || asset.m_RendererDataList == null)
                return false;

            int index = asset.m_DefaultRendererIndex;
            if (index < 0 || index >= asset.m_RendererDataList.Length)
                return false;

            var rendererData = asset.m_RendererDataList[index];
            if (!rendererData || rendererData.rendererFeatures == null)
                return false;

            foreach (var candidate in rendererData.rendererFeatures)
            {
                if (candidate is IllusionRendererFeature illusionFeature)
                {
                    feature = illusionFeature;
                    return true;
                }
            }

            return false;
        }
    }
}
