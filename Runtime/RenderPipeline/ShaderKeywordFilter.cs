#if UNITY_EDITOR
#pragma warning disable IDE0051
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.ShaderKeywordFilter;
using UnityEngine;

namespace Illusion.Rendering
{
    public partial class IllusionRendererFeature
    {
        // Notice that prefilter attribute only works for serialized fields and constants
        internal enum PrefilterMode
        {
            Remove,                     // Removes the keyword
            Select,                     // Keeps the keyword
            SelectOnly                  // Selects the keyword and removes others
        }

        // ReSharper disable once UnusedMember.Local
        [ApplyRulesIfNotGraphicsAPI(GraphicsDeviceType.OpenGLES2)]
        [SelectIf(true, overridePriority: true, keywordNames: ShaderKeywordStrings.MainLightShadowScreen)]
        private const bool RequiresScreenSpaceShadowsKeyword = true;
        
        // ReSharper disable once UnusedMember.Local
        // Override priority in UniversalRenderPipelinePrefitering first, then filter it in ShaderVariantStripper
        [SelectIf(true, overridePriority: true, keywordNames: new [] {ShaderKeywordStrings.ScreenSpaceOcclusion})]
        private const bool ScreenSpaceOcclusionPrefilterMode = true;
        
        // ReSharper disable once UnusedMember.Local
        // Override priority in UniversalRenderPipelinePrefitering
        // Prefer Depth Normal, see GroundTruthAmbientOcclusion.cs
        [SelectIf(true, overridePriority: true, keywordNames: ScreenSpaceAmbientOcclusion.k_SourceDepthNormalsKeyword)]
        private const bool RequiresScreenSpaceOcclusionDepthNormals = true;
        
        // ReSharper disable once UnusedMember.Local
        [SelectIf(true, keywordNames: new [] {"", IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED})]
        private const bool RequiresNativeRenderPass = true;
        
        // ReSharper disable once UnusedMember.Local
        [RemoveIf(true, keywordNames: IllusionShaderKeywords._DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT)]
        private const bool StripScreenSpaceShadowMainLightDebug = true;
        
        // ReSharper disable once UnusedMember.Local
        [RemoveIf(true, keywordNames: IllusionShaderKeywords._DEBUG_SCREEN_SPACE_SHADOW_CONTACT)]
        private const bool StripScreenSpaceShadowContactDebug = true;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._SCREEN_SPACE_SSS)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._SCREEN_SPACE_SSS})]
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._SCREEN_SPACE_SSS)]
        [SerializeField]
        internal PrefilterMode screenSpaceSubsurfaceScatteringPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._CONTACT_SHADOWS)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._CONTACT_SHADOWS})]
        // [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._CONTACT_SHADOWS)] // Keep off variant
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: new [] {"", IllusionShaderKeywords._CONTACT_SHADOWS})]
        [SerializeField]
        internal PrefilterMode contactShadowPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._PCSS_SHADOWS)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._PCSS_SHADOWS})]
        // [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._PCSS_SHADOWS)] // Keep off variant
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: new [] {"", IllusionShaderKeywords._PCSS_SHADOWS})]
        [SerializeField]
        internal PrefilterMode percentageCloserSoftShadowsPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION})]
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION)]
        [SerializeField]
        internal PrefilterMode precomputedRadianceTransferGIPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS})]
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS)]
        [SerializeField]
        internal PrefilterMode transparentPerObjectShadowsPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._SCREEN_SPACE_REFLECTION)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._SCREEN_SPACE_REFLECTION})]
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._SCREEN_SPACE_REFLECTION)]
        [SerializeField]
        internal PrefilterMode screenSpaceReflectionPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION})]
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION)]
        [SerializeField]
        internal PrefilterMode screenSpaceGlobalIlluminationPrefilterMode = PrefilterMode.Select;
        
        // ReSharper disable once NotAccessedField.Global
        [RemoveIf(PrefilterMode.Remove,     keywordNames: IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT)]
        [SelectIf(PrefilterMode.Select,     keywordNames: new [] {"", IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT})]
        [SelectIf(PrefilterMode.SelectOnly, keywordNames: IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT)]
        [SerializeField]
        internal PrefilterMode fragmentShadowBiasPrefilterMode = PrefilterMode.Select;
    }
}
#pragma warning restore IDE0051
#endif
