using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public static class IllusionShaders
    {
        public const string ScreenSpaceShadows = "Hidden/ScreenSpaceShadows";

        public const string PenumbraMask = "Hidden/PenumbraMask";

        public const string ScreenSpaceReflection = "Hidden/ScreenSpaceReflection";

        public const string GroundTruthAmbientOcclusion = "Hidden/GroundTruthAmbientOcclusion";

        public const string SubsurfaceScattering = "Hidden/SubsurfaceScattering";

        public const string WeightedBlendedOITComposite = "Hidden/WeightedBlendedOITComposite";

        public const string DownsampleDepth = "Hidden/DownsampleDepth";

        public const string VolumetricFog = "Hidden/VolumetricFog";

        public const string ConvolutionBloomBrightMask = "Hidden/ConvolutionBloom/BrightMask";

        public const string ConvolutionBloomBlend = "Hidden/ConvolutionBloom/Blend";

        public const string ConvolutionBloomPsfRemap = "Hidden/ConvolutionBloom/PsfRemap";

        public const string ConvolutionBloomPsfGenerator = "Hidden/ConvolutionBloom/PsfGenerator";

        public const string ApplyExposure = "Hidden/ApplyExposure";

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal const string DebugExposure = "Hidden/DebugExposure";
        
        internal const string DebugMotionVectors = "Hidden/DebugMotionVectors";
        
        internal const string ProbeGBuffer = "Hidden/ProbeGBuffer";
        
        internal const string ProbeSHDebug = "Hidden/ProbeSHDebug";
#endif
    }

    public static class IllusionShaderKeywords
    {
        public const string _CONTACT_SHADOWS = "_CONTACT_SHADOWS";

        public const string _TRANSPARENT_PER_OBJECT_SHADOWS = "_TRANSPARENT_PER_OBJECT_SHADOWS";

        public const string _SHADOW_BIAS_FRAGMENT = "_SHADOW_BIAS_FRAGMENT";

        public const string _SCREEN_SPACE_SSS = "_SCREEN_SPACE_SSS";

        public const string _DEFERRED_RENDERING_PATH = "_DEFERRED_RENDERING_PATH";

        public const string _USE_LIGHT_FACING_NORMAL = "_USE_LIGHT_FACING_NORMAL";

        public const string _SCREEN_SPACE_REFLECTION = "_SCREEN_SPACE_REFLECTION";

        public const string _SCREEN_SPACE_GLOBAL_ILLUMINATION = "_SCREEN_SPACE_GLOBAL_ILLUMINATION";

        public const string _PRT_GLOBAL_ILLUMINATION = "_PRT_GLOBAL_ILLUMINATION";

        /// <summary> Keyword used for IllusionRP RenderPass. </summary>
        public const string _ILLUSION_RENDER_PASS_ENABLED = "_ILLUSION_RENDER_PASS_ENABLED";

        public const string _PCSS_SHADOWS = "_PCSS_SHADOWS";

        public const string _DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT = "_DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT";

        public const string _DEBUG_SCREEN_SPACE_SHADOW_CONTACT = "_DEBUG_SCREEN_SPACE_SHADOW_CONTACT";
    }

    public static class IllusionShaderPasses
    {
        public const string SubsurfaceDiffuse = "SubsurfaceDiffuse";

        public const string OIT = "OIT";
    }

    public static class IllusionShaderProperties
    {
        public static readonly int _PerObjShadowCasterId = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _StencilTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _BlitTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _BlitScaleBias = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _BlitMipLevel = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _Size = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _Source = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _Destination = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _CameraNormalsTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _CameraDepthTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ContactShadowMap = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _MotionVectorTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ColorPyramidTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _HistoryColorTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _AmbientProbeData = MemberNameHelpers.ShaderPropertyID();

        // Pre-integrated texture name
        public static readonly int _PreIntegratedFGD_GGXDisneyDiffuse = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _PreIntegratedFGD_CharlieAndFabric = MemberNameHelpers.ShaderPropertyID();

        public static readonly int DepthPyramidConstants = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _DepthMipChain = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _DepthPyramid = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _DepthPyramidMipLevelOffsets = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ColorPyramidUvScaleAndLimitPrevFrame = MemberNameHelpers.ShaderPropertyID();

        public static readonly int OrderIndependent = Shader.PropertyToID("_OrderIndependent");

        public static readonly int CastPerObjectShadow = Shader.PropertyToID("_CastPerObjectShadow");

        public static readonly int ScreenSpaceReflections = Shader.PropertyToID("_ScreenSpaceReflections");

        public static readonly int ScreenSpaceAmbientOcclusion = Shader.PropertyToID("_ScreenSpaceAmbientOcclusion");

        public static readonly int StencilRefDepth = Shader.PropertyToID("_StencilRefDepth");

        public static readonly int StencilWriteMaskDepth = Shader.PropertyToID("_StencilWriteMaskDepth");

        public static readonly int StencilRefGBuffer = Shader.PropertyToID("_StencilRefGBuffer");

        public static readonly int StencilWriteMaskGBuffer = Shader.PropertyToID("_StencilWriteMaskGBuffer");

        public static readonly int _OwenScrambledTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ScramblingTileXSPP = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _RankingTileXSPP = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ScramblingTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ClearValue = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _Buffer2D = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _BufferSize = MemberNameHelpers.ShaderPropertyID();

        public static readonly int ShaderVariablesGlobal = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _ExposureTexture = MemberNameHelpers.ShaderPropertyID();

        public static readonly int _PrevExposureTexture = MemberNameHelpers.ShaderPropertyID();
        
        public static readonly int _MainLightShadowCascadeBiases = MemberNameHelpers.ShaderPropertyID();
    }

    /// <summary>
    /// Define global stencil usage in IllusionRP
    /// </summary>
    public static class IllusionStencilUsage
    {
        // Prepass (Forward+ => Attachment, Forward => DepthTexture)
        public static uint NotReceiveAmbientOcclusion = 1 << 0;

        // Forward (Attachment)
        public static uint CharacterSkin = 1 << 0;

        // Forward (Attachment)
        public static uint CharacterHair = 1 << 1;

        // GBuffer (DepthTexture)
        public static uint TraceReflectionRay = 1 << 2;
    }

    public enum IllusionFrameHistoryType
    {
        /// <summary>
        /// Color buffer mip chain.
        /// </summary>
        ColorBufferMipChain,
        /// <summary>
        /// Exposure buffer.
        /// </summary>
        Exposure,
        /// <summary>
        /// Screen Space Reflection Accumulation.
        /// </summary>
        ScreenSpaceReflectionAccumulation,
        /// <summary>
        /// Depth buffer for temporal effects.
        /// </summary>
        Depth,
        /// <summary>
        /// Normal buffer for temporal effects.
        /// </summary>
        Normal,
        /// <summary>
        /// Screen Space Global Illumination history buffer for temporal denoising.
        /// </summary>
        ScreenSpaceGlobalIllumination,
        /// <summary>
        /// Screen Space Global Illumination second history buffer for second denoiser pass.
        /// </summary>
        ScreenSpaceGlobalIllumination2
    }

    public enum IllusionGraphicsFenceEvent
    {
        AmbientOcclusion,
        ScreenSpaceReflection
    }

    /// <summary>
    /// Define render pass event order in IllusionRP, only works in Forward and Forward+ rendering path.
    /// </summary>
    /// <remarks>Passes with same event should not require each other.</remarks>
    public static class IllusionRenderPassEvent
    {
        public const RenderPassEvent SetGlobalVariablesPass = RenderPassEvent.AfterRenderingPrePasses + 0;

        // ================================= Depth Prepass ================================================ //
        public const RenderPassEvent TransparentDepthNormalPostPass = RenderPassEvent.AfterRenderingPrePasses + 0;

        // Copy pre-depth should before depth only post pass.
        public const RenderPassEvent TransparentCopyPreDepthPass = RenderPassEvent.AfterRenderingPrePasses + 1;

        // Screen space effect need ignore transparent post depth since normal is not matched with depth.
        public const RenderPassEvent DepthPyramidPass = RenderPassEvent.AfterRenderingPrePasses + 1;

        public const RenderPassEvent TransparentDepthOnlyPostPass = RenderPassEvent.AfterRenderingPrePasses + 2;
        // ================================= Depth Prepass ================================================ //

        public const RenderPassEvent ForwardGBufferPass = RenderPassEvent.AfterRenderingPrePasses + 3;

        public const RenderPassEvent MotionVectorPrepass = RenderPassEvent.AfterRenderingPrePasses + 3;

        // ============================== Screen Space Lighting ============================================ //
        // Async Compute
        public const RenderPassEvent AmbientOcclusionPass = RenderPassEvent.AfterRenderingPrePasses + 4;

        // Async Compute
        public const RenderPassEvent ScreenSpaceReflectionPass = RenderPassEvent.AfterRenderingPrePasses + 5;
        // ============================== Screen Space Lighting ============================================ //

        // ====================================== Shadows ================================================== //
        public const RenderPassEvent LightsShadowCasterPass = RenderPassEvent.AfterRenderingPrePasses + 6;

        public const RenderPassEvent PerObjectShadowCasterPass = RenderPassEvent.AfterRenderingPrePasses + 6;

        public const RenderPassEvent ContactShadowsPass = RenderPassEvent.AfterRenderingPrePasses + 6;
        // ====================================== Shadows ================================================== //

        // Require main shadow pass
        public const RenderPassEvent PrecomputedRadianceTransferRelightPass = RenderPassEvent.AfterRenderingPrePasses + 7;

        public const RenderPassEvent DiffuseShadowDenoisePass = RenderPassEvent.AfterRenderingPrePasses + 7;

        public const RenderPassEvent ScreenSpaceGlobalIlluminationPass = RenderPassEvent.AfterRenderingPrePasses + 8;

        // Composite shadows to Screen Space Shadows
        public const RenderPassEvent ScreenSpaceShadowsPass = RenderPassEvent.AfterRenderingGbuffer;

        public const RenderPassEvent SubsurfaceScatteringPass = RenderPassEvent.AfterRenderingGbuffer;

        public const RenderPassEvent ScreenSpaceShadowsPostPass = RenderPassEvent.AfterRenderingOpaques;

        // ==================================== Transparency =============================================== //
        public const RenderPassEvent OrderIndependentTransparentPass = RenderPassEvent.AfterRenderingTransparents + 1;

        // Copy post-depth before overdrawing.
        public const RenderPassEvent TransparentCopyPostDepthPass = RenderPassEvent.AfterRenderingTransparents + 2;

        public const RenderPassEvent TransparentOverdrawPass = RenderPassEvent.AfterRenderingTransparents + 3;
        // ==================================== Transparency =============================================== //

        public const RenderPassEvent ColorPyramidPass = RenderPassEvent.AfterRenderingTransparents + 4;

        // =================================== Post Processing ============================================= //
        public const RenderPassEvent VolumetricFogPass = RenderPassEvent.BeforeRenderingPostProcessing - 3;

        // Automatic exposure should consider volumetric fog.
        public const RenderPassEvent ExposurePass = RenderPassEvent.BeforeRenderingPostProcessing - 2;

        public const RenderPassEvent CustomPostProcessPass = RenderPassEvent.BeforeRenderingPostProcessing - 1;
        // =================================== Post Processing ============================================= //

        public const RenderPassEvent MotionVectorDebugPass = RenderPassEvent.BeforeRenderingPostProcessing;

        public const RenderPassEvent PostProcessPostPass = RenderPassEvent.AfterRenderingPostProcessing;

        public const RenderPassEvent FullScreenDebugPass = RenderPassEvent.AfterRendering + 2; // Ensure after Final Blit.
    }
}