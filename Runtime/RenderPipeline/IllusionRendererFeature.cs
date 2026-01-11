using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Illusion.Rendering.Shadows;
using Illusion.Rendering.PostProcessing;
using Illusion.Rendering.PRTGI;
using UnityEngine.Assertions;

namespace Illusion.Rendering
{
    [DisallowMultipleRendererFeature("Illusion Graphics")]
    public partial class IllusionRendererFeature : ScriptableRendererFeature
    {
        #region General

        /// <summary>
        /// If this is enabled, the camera copies the last rendered view so it can be accessed at next frame in the pipeline.
        /// When temporal anti-aliasing is on, history color will fetch accumulation buffer directly
        /// </summary>
        [SerializeField]
        internal bool requireHistoryColor = true;

        /// <summary>
        /// Enable to draw motion vector earlier than drawing objects.
        /// </summary>
        [SerializeField]
        internal bool requireEarlyMotionVector = true;

        /// <summary>
        /// Whether prefer to calculating effects in compute shader if possible.
        /// </summary>
        [SerializeField]
        internal bool preferComputeShader = true;

        /// <summary>
        /// Enables IllusionRP to use RenderPass API. Has no effect on OpenGLES2.
        /// </summary>
        [SerializeField]
        internal bool nativeRenderPass = true;

        #endregion General

        #region Transparency

        /// <summary>
        /// Enable Weighted Blended Order-Independent Transparency which will solve transparent objects rendering order problem
        /// but may lead to inaccurate final blend color when alpha is nearly one.
        /// </summary>
        public bool orderIndependentTransparency = true;

        /// <summary>
        /// Configure the rendering objects LayerMask for the oit passes.
        /// </summary>
        public LayerMask oitFilterLayer = -1;

        /// <summary>
        /// Override the stencil state for the transparent overdraw pass.
        /// </summary>
        public TransparentOverdrawStencilStateData oitOverrideStencil = new();

        /// <summary>
        /// Enable to write transparent depth after depth prepass.
        /// </summary>
        public bool transparentDepthPostPass = true;

        /// <summary>
        /// Enable to overdraw universal transparent objects after rendering OIT objects, used to fix background color bleed-through.
        /// </summary>
        public bool oitTransparentOverdrawPass;

        #endregion Transparency

        #region Lighting

        /// <summary>
        /// Enable Screen Space Subsurface Scattering.
        /// </summary>
        public bool subsurfaceScattering = true;

        #endregion Lighting

        #region Shadows

        [SerializeField]
        internal RenderingLayerMask perObjectShadowRenderingLayer;

        /// <summary>
        /// When enabled, transparent objects will sample per object shadow which will decrease performance."
        /// </summary>
        public bool transparentReceivePerObjectShadows;

        /// <summary>
        /// Get and set enable contact shadows feature
        /// </summary>
        public bool contactShadows;

        public bool pcssShadows;

        public bool fragmentShadowBias;

        #endregion Shadows

        #region Ambient Occlusion

        public bool groundTruthAO = true;

        #endregion Ambient Occlusion

        #region Global Illumination

        public bool screenSpaceReflection;

        public bool screenSpaceGlobalIllumination;

        public bool precomputedRadianceTransferGI;

        public bool enableIndirectDiffuseRenderingLayers;

        #endregion Global Illumination

        #region Post Processing

        /// <summary>
        /// Enable high-quality bloom effect using Fast Fourier Transform convolution.
        /// </summary>
        public bool convolutionBloom = true;

        /// <summary>
        /// Enable volumetric fog effect.
        /// </summary>
        public bool volumetricFog = true;

        #endregion Post Processing

        private ShadowCasterManager _sceneShadowCasterManager;

        private SetKeywordPass _enableTransparentPerObjectShadowsPass;

        private SetKeywordPass _disableTransparentPerObjectShadowsPass;
        
        private SetKeywordPass _enableFragmentShadowBiasPass;

        private SetKeywordPass _disableFragmentShadowBiasPass;

        private PerObjectShadowCasterPass _perObjShadowPass;

        private PerObjectShadowCasterPreviewPass _perObjShadowPreviewPass;

        private ScreenSpaceShadowsPass _screenSpaceShadowsPass;

        private DiffuseShadowDenoisePass _diffuseShadowDenoisePass;

        private ScreenSpaceShadowsPostPass _screenSpaceShadowsPostPass;

        private SubsurfaceScatteringPass _subsurfaceScatteringPass;

        private GroundTruthAmbientOcclusionPass _groundTruthAmbientOcclusionPass;
        
        private SetKeywordPass _enableSSAOPass;

        private SetKeywordPass _disableSSAOPass;

        private ContactShadowsPass _contactShadowsPass;

        private WeightedBlendedOITPass _weightedBlendedOitPass;

        private TransparentCopyPreDepthPass _transparentCopyPreDepthPass;

        private TransparentCopyPostDepthPass _transparentCopyPostDepthPass;

        private TransparentDepthNormalPostPass _transparentDepthNormalPostPass;

        private TransparentDepthOnlyPostPass _transparentDepthOnlyPass;

        private TransparentOverdrawPass _transparentOverdrawPass;

        private ColorPyramidPass _colorPyramidPass;

        private SetKeywordPass _enableScreenSpaceSubsurfaceScatteringPass;

        private SetKeywordPass _disableScreenSpaceSubsurfaceScatteringPass;

        private IllusionRendererData _rendererData;

        private IllusionRenderPipelineResources _renderPipelineResources;

        private PreIntegratedFGDPass _ggxAndDisneyDiffusePass;

        private PreIntegratedFGDPass _charlieAndFabricLambertPass;

        private ScreenSpaceReflectionPass _screenSpaceReflectionPass;

        private SyncGraphicsFencePass _screenSpaceReflectionSyncFencePass;

        private SetKeywordPass _enableScreenSpaceReflectionPass;

        private SetKeywordPass _disableScreenSpaceReflectionPass;

        private ScreenSpaceGlobalIlluminationPass _screenSpaceGlobalIlluminationPass;
        
        private SetKeywordPass _enableScreenSpaceGlobalIlluminationPass;

        private SetKeywordPass _disableScreenSpaceGlobalIlluminationPass;

        private ForwardGBufferPass _forwardGBufferPass;

        private CopyHistoryColorPass _copyHistoryColorPass;

        private SetGlobalVariablesPass _setGlobalVariablesPass;

        private DepthPyramidPass _depthPyramidPass;

        private SetKeywordPass _enableDeferredPass;

        private SetKeywordPass _disableDeferredPass;

        private ConvolutionBloomPass _convolutionBloomPass;

        private VolumetricFogPass _volumetricFogPass;

        private VolumetricLightManager _volumetricLightManager;

        private SetupPass _setupPass;

        private AdvancedTonemappingPass _advancedTonemappingPass;

        private ExposurePass _exposurePass;

        private PostProcessingPostPass _processingPostPass;

        private PRTRelightPass _prtRelightPass;

        private SetKeywordPass _enablePRTGIPass;

        private SetKeywordPass _disablePRTGIPass;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private ExposureDebugPass _exposureDebugPass;
        
        private MotionVectorsDebugPass _motionVectorsDebugPass;
#endif

        public override void Create()
        {
            // Try to release resources before creating render passes. Required in editor.
            Release();

            if (!_renderPipelineResources)
            {
                _renderPipelineResources = Resources.Load<IllusionRenderPipelineResources>(nameof(IllusionRenderPipelineResources));
            }

            if (Application.isPlaying)
            {
                Assert.IsTrue((bool)_renderPipelineResources, $"[IllusionRP] Missing {nameof(IllusionRenderPipelineResources)} which is not expected!");
            }
            else
            {
                // Do not block rendering in editor
                if (!_renderPipelineResources) return;
            }
            _rendererData = new IllusionRendererData(_renderPipelineResources);
            UpdateRenderDataSettings();

            _sceneShadowCasterManager = new ShadowCasterManager();

            _ggxAndDisneyDiffusePass = new PreIntegratedFGDPass(_rendererData, PreIntegratedFGD.FGDIndex.FGD_GGXAndDisneyDiffuse);
            _charlieAndFabricLambertPass = new PreIntegratedFGDPass(_rendererData, PreIntegratedFGD.FGDIndex.FGD_CharlieAndFabricLambert);
            _enableTransparentPerObjectShadowsPass = new SetKeywordPass(IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS, true, RenderPassEvent.BeforeRendering);
            _disableTransparentPerObjectShadowsPass = new SetKeywordPass(IllusionShaderKeywords._TRANSPARENT_PER_OBJECT_SHADOWS, false, RenderPassEvent.BeforeRendering);

            _enableFragmentShadowBiasPass = new SetKeywordPass(IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT, true, RenderPassEvent.BeforeRendering);
            _disableFragmentShadowBiasPass = new SetKeywordPass(IllusionShaderKeywords._SHADOW_BIAS_FRAGMENT, false, RenderPassEvent.BeforeRendering);

            _perObjShadowPass = new PerObjectShadowCasterPass(_rendererData);
            _perObjShadowPreviewPass = new PerObjectShadowCasterPreviewPass();

            _contactShadowsPass = new ContactShadowsPass(_rendererData);
            _screenSpaceShadowsPass = new ScreenSpaceShadowsPass(_rendererData);
            _diffuseShadowDenoisePass = new DiffuseShadowDenoisePass(_rendererData);
            _screenSpaceShadowsPostPass = new ScreenSpaceShadowsPostPass();
            _subsurfaceScatteringPass = new SubsurfaceScatteringPass(_rendererData);
            _groundTruthAmbientOcclusionPass = new GroundTruthAmbientOcclusionPass(_rendererData);
            
            _enableSSAOPass = new SetKeywordPass(ShaderKeywordStrings.ScreenSpaceOcclusion, true, RenderPassEvent.BeforeRendering);
            _disableSSAOPass = new SetKeywordPass(ShaderKeywordStrings.ScreenSpaceOcclusion, false, RenderPassEvent.BeforeRendering);


            _prtRelightPass = new PRTRelightPass(_rendererData);
            _enablePRTGIPass = new SetKeywordPass(IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION, true, RenderPassEvent.BeforeRendering);
            _disablePRTGIPass = new SetKeywordPass(IllusionShaderKeywords._PRT_GLOBAL_ILLUMINATION, false, RenderPassEvent.BeforeRendering);

            _weightedBlendedOitPass = new WeightedBlendedOITPass(oitFilterLayer);
            _transparentOverdrawPass = TransparentOverdrawPass.Create(oitOverrideStencil);
            _transparentDepthNormalPostPass = new TransparentDepthNormalPostPass();
            _transparentDepthOnlyPass = new TransparentDepthOnlyPostPass();
            _transparentCopyPreDepthPass = new TransparentCopyPreDepthPass(_rendererData);
            _transparentCopyPostDepthPass = new TransparentCopyPostDepthPass();

            _screenSpaceReflectionPass = new ScreenSpaceReflectionPass(_rendererData);
            _screenSpaceReflectionSyncFencePass = new SyncGraphicsFencePass(RenderPassEvent.BeforeRenderingOpaques, IllusionGraphicsFenceEvent.ScreenSpaceReflection, _rendererData);
            _enableScreenSpaceReflectionPass = new SetKeywordPass(IllusionShaderKeywords._SCREEN_SPACE_REFLECTION, true, RenderPassEvent.BeforeRendering);
            _disableScreenSpaceReflectionPass = new SetKeywordPass(IllusionShaderKeywords._SCREEN_SPACE_REFLECTION, false, RenderPassEvent.BeforeRendering);

            _screenSpaceGlobalIlluminationPass = new ScreenSpaceGlobalIlluminationPass(_rendererData);
            _enableScreenSpaceGlobalIlluminationPass = new SetKeywordPass(IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION, true, RenderPassEvent.BeforeRendering);
            _disableScreenSpaceGlobalIlluminationPass = new SetKeywordPass(IllusionShaderKeywords._SCREEN_SPACE_GLOBAL_ILLUMINATION, false, RenderPassEvent.BeforeRendering);

            _enableDeferredPass = new SetKeywordPass(IllusionShaderKeywords._DEFERRED_RENDERING_PATH, true, RenderPassEvent.BeforeRendering);
            _disableDeferredPass = new SetKeywordPass(IllusionShaderKeywords._DEFERRED_RENDERING_PATH, false, RenderPassEvent.BeforeRendering);
            _forwardGBufferPass = new ForwardGBufferPass(_rendererData);
            _depthPyramidPass = new DepthPyramidPass(_rendererData);
            _colorPyramidPass = new ColorPyramidPass(_rendererData);
            _copyHistoryColorPass = CopyHistoryColorPass.Create(_rendererData);
            _setGlobalVariablesPass = new SetGlobalVariablesPass(_rendererData);

            _enableScreenSpaceSubsurfaceScatteringPass = new SetKeywordPass(IllusionShaderKeywords._SCREEN_SPACE_SSS, true, RenderPassEvent.BeforeRendering);
            _disableScreenSpaceSubsurfaceScatteringPass = new SetKeywordPass(IllusionShaderKeywords._SCREEN_SPACE_SSS, false, RenderPassEvent.BeforeRendering);

            _convolutionBloomPass = new ConvolutionBloomPass(_rendererData);
            _volumetricFogPass = new VolumetricFogPass(_rendererData);
            _volumetricLightManager = new VolumetricLightManager();
            _advancedTonemappingPass = new AdvancedTonemappingPass();
            _exposurePass = new ExposurePass(_rendererData);
            _processingPostPass = new PostProcessingPostPass(_rendererData);

            _setupPass = new SetupPass(this, _rendererData);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _exposureDebugPass = new ExposureDebugPass(_rendererData);
            _motionVectorsDebugPass = new MotionVectorsDebugPass(_rendererData);
#endif
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var config = IllusionRuntimeRenderingConfig.Get();
            bool isPreviewCamera = renderingData.cameraData.cameraType == CameraType.Preview;
            bool isGameCamera = renderingData.cameraData.cameraType == CameraType.Game;
            bool enableSceneShadow = !isPreviewCamera;
            bool isPostProcessEnabled = renderingData.postProcessingEnabled && renderingData.cameraData.postProcessEnabled;
            var isDeferred = UniversalRenderingUtility.GetRenderingModeActual(renderer) == RenderingMode.Deferred;

            var contactShadowParam = VolumeManager.instance.stack.GetComponent<ContactShadows>();
            bool useContactShadows = config.EnableContactShadows
                                    && contactShadows && !isPreviewCamera 
                                    && contactShadowParam.enable.value;

            bool useTransparentShadow = transparentReceivePerObjectShadows && !isPreviewCamera;

            var ambientOcclusionParam = VolumeManager.instance.stack.GetComponent<GroundTruthAmbientOcclusion>();
            bool useAmbientOcclusion = config.EnableScreenSpaceAmbientOcclusion
                                       && groundTruthAO && !isPreviewCamera && ambientOcclusionParam.enable.value;

            var screenSpaceReflectionParam = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            bool useScreenSpaceReflection = config.EnableScreenSpaceReflection
                                            && screenSpaceReflection && !isPreviewCamera
                                            && screenSpaceReflectionParam.enable.value;

            var screenSpaceGlobalIlluminationParam = VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();
            bool useScreenSpaceGlobalIllumination = config.EnableScreenSpaceGlobalIllumination 
                                                    && screenSpaceGlobalIllumination && !isPreviewCamera
                                                    && screenSpaceGlobalIlluminationParam.enable.value;
            
            // ========================================= Post Processing ============================================================ //
            var convolutionBloomParam = VolumeManager.instance.stack.GetComponent<ConvolutionBloom>();
            bool useConvolutionBloom = convolutionBloom 
                                       && config.EnableConvolutionBloom
                                       && isPostProcessEnabled 
                                       && !isPreviewCamera 
                                       && convolutionBloomParam.IsActive();

            var volumetricFogParam = VolumeManager.instance.stack.GetComponent<VolumetricFog>();
            bool useVolumetricFog = config.EnableVolumetricFog
                                    && volumetricFog 
                                    && isPostProcessEnabled 
                                    && !isPreviewCamera 
                                    && volumetricFogParam.IsActive();
            // ========================================= Post Processing ============================================================ //

            bool usePrecomputedRadianceTransfer = config.EnablePrecomputedRadianceTransferGlobalIllumination
                                                  && precomputedRadianceTransferGI 
                                                  && !isPreviewCamera;
            bool hasValidVolume = PRTVolumeManager.ProbeVolume && PRTVolumeManager.ProbeVolume.IsActivate();
            _rendererData.SampleProbeVolumes = usePrecomputedRadianceTransfer && hasValidVolume;

            bool useForwardGBuffer = useScreenSpaceReflection && !isDeferred;
            bool useDepthPyramid = useAmbientOcclusion || useScreenSpaceReflection || useScreenSpaceGlobalIllumination;
            bool useTAA = renderingData.cameraData.IsTemporalAAEnabled(); // Disable in scene view
            bool useMotionVectorPrepass = requireEarlyMotionVector;
            bool needHistoryColor = requireHistoryColor && !useTAA;
            bool useColorPyramid = useScreenSpaceReflection || useScreenSpaceGlobalIllumination;

            bool useDepthPostPass = transparentDepthPostPass && !isPreviewCamera;
            bool useTransparentOverdrawPass = orderIndependentTransparency && oitTransparentOverdrawPass && !isPreviewCamera;

            bool isOffscreenDepth = UniversalRenderingUtility.IsOffscreenDepthTexture(in renderingData.cameraData);

            // Setup pass must run first (handles configuration for both Unity 2022 and 2023)
            renderer.EnqueuePass(_setupPass);

            // BeforeRendering
            renderer.EnqueuePass(_ggxAndDisneyDiffusePass);
            renderer.EnqueuePass(_charlieAndFabricLambertPass);
            renderer.EnqueuePass(groundTruthAO ? _enableSSAOPass : _disableSSAOPass);
            renderer.EnqueuePass(useTransparentShadow ? _enableTransparentPerObjectShadowsPass : _disableTransparentPerObjectShadowsPass);
            renderer.EnqueuePass(subsurfaceScattering ? _enableScreenSpaceSubsurfaceScatteringPass : _disableScreenSpaceSubsurfaceScatteringPass);
            renderer.EnqueuePass(screenSpaceReflection ? _enableScreenSpaceReflectionPass : _disableScreenSpaceReflectionPass);
            renderer.EnqueuePass(screenSpaceGlobalIllumination ? _enableScreenSpaceGlobalIlluminationPass : _disableScreenSpaceGlobalIlluminationPass);
            renderer.EnqueuePass(isDeferred ? _enableDeferredPass : _disableDeferredPass);
            renderer.EnqueuePass(precomputedRadianceTransferGI ? _enablePRTGIPass : _disablePRTGIPass);
            renderer.EnqueuePass(fragmentShadowBias ? _enableFragmentShadowBiasPass : _disableFragmentShadowBiasPass);

            // BeforeRenderingPrePasses
            renderer.EnqueuePass(_advancedTonemappingPass);

            // AfterRenderingPrePasses
            renderer.EnqueuePass(_setGlobalVariablesPass);

            if (useDepthPostPass)
            {
                renderer.EnqueuePass(_transparentCopyPreDepthPass);
                renderer.EnqueuePass(_transparentDepthNormalPostPass);
                renderer.EnqueuePass(_transparentDepthOnlyPass);
            }

            if (useDepthPyramid && !isOffscreenDepth)
            {
                renderer.EnqueuePass(_depthPyramidPass);
            }

            if (useForwardGBuffer)
            {
                renderer.EnqueuePass(_forwardGBufferPass);
            }

            // Re-order motion vector pass renderPassEvent.
            var motionVectorPass = UniversalRenderingUtility.GetMotionVectorRenderPass(renderer);
            if (motionVectorPass != null)
            {
                motionVectorPass.renderPassEvent = useMotionVectorPrepass ? IllusionRenderPassEvent.MotionVectorPrepass : RenderPassEvent.BeforeRenderingPostProcessing;
            }

            if (useAmbientOcclusion && !isOffscreenDepth)
            {
                renderer.EnqueuePass(_groundTruthAmbientOcclusionPass);
            }

            if (screenSpaceReflection && !isOffscreenDepth)
            {
                renderer.EnqueuePass(_screenSpaceReflectionPass);
            }
            if (useScreenSpaceReflection && !isOffscreenDepth)
            {
                renderer.EnqueuePass(_screenSpaceReflectionSyncFencePass); // Sync on BeforeRenderingOpaques
            }

            if (useScreenSpaceGlobalIllumination && !isOffscreenDepth)
            {
                renderer.EnqueuePass(_screenSpaceGlobalIlluminationPass);
            }

            renderer.EnqueuePass(enableSceneShadow ? _perObjShadowPass : _perObjShadowPreviewPass);

            if (useContactShadows)
            {
                renderer.EnqueuePass(_contactShadowsPass);
                switch (contactShadowParam.shadowDenoiser.value)
                {
                    case ShadowDenoiser.None:
                        break;
                    case ShadowDenoiser.Spatial:
                        renderer.EnqueuePass(_diffuseShadowDenoisePass);
                        break;
                }
            }

            renderer.EnqueuePass(_prtRelightPass);

            // AfterRenderingGBuffer
            renderer.EnqueuePass(_screenSpaceShadowsPass);
            
            // Always add subsurface scattering, upload parameters only when feature is disabled.
            renderer.EnqueuePass(_subsurfaceScatteringPass);

            // AfterRenderingOpaques
            renderer.EnqueuePass(_screenSpaceShadowsPostPass);

            // AfterRenderingTransparents
            if (orderIndependentTransparency)
            {
                renderer.EnqueuePass(_weightedBlendedOitPass);
            }

            if (useTransparentOverdrawPass)
            {
                renderer.EnqueuePass(_transparentCopyPostDepthPass);
                renderer.EnqueuePass(_transparentOverdrawPass);
            }

            if (useColorPyramid)
            {
                renderer.EnqueuePass(_colorPyramidPass);
            }
            else
            {
                _rendererData.ColorPyramidHistoryMipCount = 1;
            }

            // BeforeRenderingPostProcessing
            if (needHistoryColor)
            {
                renderer.EnqueuePass(_copyHistoryColorPass);
            }

            if (useConvolutionBloom)
            {
                renderer.EnqueuePass(_convolutionBloomPass);
            }

            if (useVolumetricFog)
            {
                renderer.EnqueuePass(_volumetricFogPass);
            }

            if (!isPreviewCamera) // Not let material preview affect game or scene view
            {
                // Control exposure in pass itself.
                renderer.EnqueuePass(_exposurePass);
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Debug
            if (IllusionRuntimeRenderingConfig.Get().EnableMotionVectorsDebug)
            {
                renderer.EnqueuePass(_motionVectorsDebugPass);
            }
            if (IllusionRuntimeRenderingConfig.Get().ExposureDebugMode != ExposureDebugMode.None && isGameCamera)
            {
                renderer.EnqueuePass(_exposureDebugPass);
            }
#endif
            // AfterRenderingPostProcessing
            renderer.EnqueuePass(_processingPostPass);
        }
        
        // Ref: MainLightShadowCasterPass.Setup
        private static bool CanRenderMainLightShadow(UniversalCameraData cameraData, UniversalLightData lightData, UniversalShadowData shadowData, UniversalRenderingData renderingData)
        {
            if (!shadowData.mainLightShadowsEnabled)
                return false;

#if UNITY_EDITOR
            if (CoreUtils.IsSceneLightingDisabled(cameraData.camera))
                return false;
#endif

            if (!shadowData.supportsMainLightShadows)
                return false;

            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return false;

            VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];
            Light light = shadowLight.light;
            if (light.shadows == LightShadows.None)
                return false;

            if (!renderingData.cullResults.GetShadowCasterBounds(shadowLightIndex, out _))
                return false;

            return true;
        }

        private static bool CanRenderAdditionalLightShadow(UniversalShadowData shadowData)
        {
            return shadowData.supportsAdditionalLightShadows;
        }

        private void PerformSetup(ContextContainer frameData, IllusionRendererData rendererData)
        {
            UpdateRenderDataSettings();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var shadowData = frameData.Get<UniversalShadowData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            rendererData.Update(cameraData, lightData, shadowData);
            var config = IllusionRuntimeRenderingConfig.Get();
            bool isPreviewOrReflectCamera = cameraData.cameraType is CameraType.Preview or CameraType.Reflection;

            var contactShadowParam = VolumeManager.instance.stack.GetComponent<ContactShadows>();
            rendererData.ContactShadowsSampling = contactShadows
                                                  && !isPreviewOrReflectCamera
                                                  && config.EnableContactShadows
                                                  && contactShadowParam.enable.value;
            rendererData.PCSSShadowSampling = pcssShadows
                                              && !isPreviewOrReflectCamera
                                              && config.EnablePercentageCloserSoftShadows;

            var screenSpaceGlobalIlluminationParam =
                VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();
            bool useScreenSpaceGlobalIllumination = screenSpaceGlobalIllumination
                                                    && config.EnableScreenSpaceGlobalIllumination
                                                    && !isPreviewOrReflectCamera
                                                    && screenSpaceGlobalIlluminationParam.enable.value;
            rendererData.SampleScreenSpaceIndirectDiffuse = useScreenSpaceGlobalIllumination;

            var screenSpaceReflectionParam = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
            bool useScreenSpaceReflection = config.EnableScreenSpaceReflection
                                            && screenSpaceReflection && !isPreviewOrReflectCamera
                                            && screenSpaceReflectionParam.enable.value;
            rendererData.SampleScreenSpaceReflection = useScreenSpaceReflection;
            rendererData.RequireHistoryDepthNormal = useScreenSpaceGlobalIllumination;

            // Re-order light shadow caster pass renderPassEvent better for async compute.
            // Ref: https://developer.nvidia.com/blog/advanced-api-performance-async-compute-and-overlap/
            bool enableAsyncCompute = rendererData.PreferComputeShader
                                      && IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute;

            // Shadow Caster has bug in URP14.0.12 when there is no main/additional light in scene which will clear pre-z.
            // So skip re-order when shadow is not rendered.
            bool reorderMainLightShadowPass = enableAsyncCompute && CanRenderMainLightShadow(cameraData, lightData, shadowData, renderingData);
            var mainLightShadowCasterPass = UniversalRenderingUtility.GetMainLightShadowCasterPass(cameraData.renderer);
            if (mainLightShadowCasterPass != null)
            {
                mainLightShadowCasterPass.renderPassEvent = reorderMainLightShadowPass
                    ? IllusionRenderPassEvent.LightsShadowCasterPass
                    : RenderPassEvent.BeforeRenderingShadows;
            }

            bool reorderAdditionalLightShadowPass =
                enableAsyncCompute && CanRenderAdditionalLightShadow(shadowData);
            var additionalLightsShadowCasterPass =
                UniversalRenderingUtility.GetAdditionalLightsShadowCasterPass(cameraData.renderer);
            if (additionalLightsShadowCasterPass != null)
            {
                additionalLightsShadowCasterPass.renderPassEvent = reorderAdditionalLightShadowPass
                    ? IllusionRenderPassEvent.LightsShadowCasterPass
                    : RenderPassEvent.BeforeRenderingShadows;
            }

            var shadow = VolumeManager.instance.stack.GetComponent<PerObjectShadows>();
            _sceneShadowCasterManager.Cull(cameraData, lightData,
                PerObjectShadowCasterPass.MaxShadowCount,
                shadow.perObjectShadowLengthOffset.value,
                IllusionRuntimeRenderingConfig.Get().EnablePerObjectShadowDebug);
            _perObjShadowPass.Setup(_sceneShadowCasterManager, shadow.perObjectShadowTileResolution.value,
                shadow.perObjectShadowDepthBits.value);
            _volumetricFogPass.Setup(_volumetricLightManager);
        }

        private void UpdateRenderDataSettings()
        {
            var config = IllusionRuntimeRenderingConfig.Get();
            _rendererData.PerObjectShadowRenderingLayer = perObjectShadowRenderingLayer;
            // We should check compute shaders are supported whether we should fall back to fragment shader.
            // But currently IllusionRP only supports platforms that support compute shader.
            _rendererData.PreferComputeShader = preferComputeShader 
                                                && SystemInfo.supportsComputeShaders
                                                && config.EnableComputeShader;
            _rendererData.RequireHistoryColor = requireHistoryColor;
            _rendererData.EnableIndirectDiffuseRenderingLayers = enableIndirectDiffuseRenderingLayers;
        }

        private static void SafeDispose<TDisposable>(ref TDisposable disposable) where TDisposable : class, IDisposable
        {
            disposable?.Dispose();
            disposable = null;
        }

        private void Release()
        {
            SafeDispose(ref _rendererData);
            SafeDispose(ref _diffuseShadowDenoisePass);
            SafeDispose(ref _groundTruthAmbientOcclusionPass);
            SafeDispose(ref _ggxAndDisneyDiffusePass);
            SafeDispose(ref _charlieAndFabricLambertPass);
            SafeDispose(ref _perObjShadowPass);
            SafeDispose(ref _screenSpaceShadowsPass);
            SafeDispose(ref _subsurfaceScatteringPass);
            SafeDispose(ref _contactShadowsPass);
            SafeDispose(ref _weightedBlendedOitPass);
            SafeDispose(ref _transparentCopyPreDepthPass);
            SafeDispose(ref _transparentCopyPostDepthPass);
            SafeDispose(ref _transparentDepthNormalPostPass);
            SafeDispose(ref _screenSpaceReflectionPass);
            SafeDispose(ref _screenSpaceGlobalIlluminationPass);
            SafeDispose(ref _forwardGBufferPass);
            SafeDispose(ref _depthPyramidPass);
            SafeDispose(ref _convolutionBloomPass);
            SafeDispose(ref _volumetricFogPass);
            SafeDispose(ref _copyHistoryColorPass);
            SafeDispose(ref _colorPyramidPass);
            SafeDispose(ref _exposurePass);
            SafeDispose(ref _prtRelightPass);
            SafeDispose(ref _setupPass);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            SafeDispose(ref _motionVectorsDebugPass);
            SafeDispose(ref _exposureDebugPass);
#endif

            // Need call it in URP manually
            ConstantBuffer.ReleaseAll();
        }

        protected override void Dispose(bool disposing)
        {
            Release();
            base.Dispose(disposing);
        }
    }
}
