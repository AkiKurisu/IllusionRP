using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public sealed partial class UniversalRenderer
    {
        internal MainLightShadowCasterPass MainLightShadowPass => m_MainLightShadowCasterPass;
        internal AdditionalLightsShadowCasterPass AdditionalLightsShadowPass => m_AdditionalLightsShadowCasterPass;
    }
}

namespace UnityEngine.Rendering.Universal.Internal
{
    public partial class MainLightShadowCasterPass
    {
        internal bool IsEmptyShadowmap => m_CreateEmptyShadowmap;
        internal int CascadeCount => m_ShadowCasterCascadesCount;
        internal ShadowSliceData[] CascadeSlices => m_CascadeSlices;
    }

    public partial class AdditionalLightsShadowCasterPass
    {
        internal bool IsEmptyShadowmap => m_CreateEmptyShadowmap;
        internal short[] VisibleLightIndexToAdditionalLightIndex => m_VisibleLightIndexToAdditionalLightIndex;
        internal bool[] VisibleLightIndexToIsCastingShadows => m_VisibleLightIndexToIsCastingShadows;
        internal Vector4[] AdditionalLightIndexToShadowParams => m_AdditionalLightIndexToShadowParams;
        internal Matrix4x4[] ShadowSliceIndexToWorldShadowMatrix => m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix;
        internal ShadowSliceData[] ShadowSlices => m_AdditionalLightsShadowSlices;
    }
}
