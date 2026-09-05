using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.AreaLights
{
    // Reference: UnityEngine.Rendering.HighDefinition.LTCAreaLight
    internal partial class LTCAreaLight
    {
        static LTCAreaLight s_Instance;

        internal static LTCAreaLight instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new LTCAreaLight();

                return s_Instance;
            }
        }

        int m_refCounting;

        // For area lighting - We pack all texture inside a texture array to reduce the number of resource required
        Texture2DArray m_LtcData; // 0: m_LtcGGXMatrix - RGBA, 1: m_LtcDisneyDiffuseMatrix - RGBA

        internal const int k_LtcLUTResolution = 64;

        internal LTCAreaLight()
        {
            m_refCounting = 0;
        }

        internal void Build()
        {
            Debug.Assert(m_refCounting >= 0);

            if (m_refCounting == 0)
            {
                m_LtcData = new Texture2DArray(k_LtcLUTResolution, k_LtcLUTResolution, (int)LTCLightingModel.Count, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = CoreUtils.GetTextureAutoName(k_LtcLUTResolution, k_LtcLUTResolution, GraphicsFormat.R16G16B16A16_SFloat, depth: (int)LTCLightingModel.Count, dim: TextureDimension.Tex2DArray, name: "LTC_LUT")
                };

                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_GGX, 0, (int)LTCLightingModel.GGX);
                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_Disney, 0, (int)LTCLightingModel.DisneyDiffuse);

                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_Charlie, 0, (int)LTCLightingModel.Charlie);

                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_KajiyaKaySpecular, 0, (int)LTCLightingModel.KajiyaKaySpecular);
                // TODO: Generate the Marschner LCT Table

                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_CookTorrance, 0, (int)LTCLightingModel.CookTorrance);
                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_Ward, 0, (int)LTCLightingModel.Ward);
                m_LtcData.SetPixelData(s_LtcMatrixData_BRDF_OrenNayar, 0, (int)LTCLightingModel.OrenNayar);

                m_LtcData.Apply();
                // @IllusionRP: render graph passes bind imported textures, keep an RTHandle wrapper around the LUT.
                m_LtcDataHandle = RTHandles.Alloc(m_LtcData);
            }

            m_refCounting++;
        }

        internal void Cleanup()
        {
            m_refCounting--;

            if (m_refCounting == 0)
            {
                RTHandles.Release(m_LtcDataHandle);
                m_LtcDataHandle = null;
                CoreUtils.Destroy(m_LtcData);
            }

            Debug.Assert(m_refCounting >= 0);
        }

        RTHandle m_LtcDataHandle;

        internal RTHandle ltcDataHandle => m_LtcDataHandle;
    }
}
