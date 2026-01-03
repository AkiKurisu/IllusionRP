using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Illusion.Rendering.Shadows;
using Illusion.Rendering.PostProcessing;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    public partial class IllusionRendererFeature
    {
        /// <summary>
        /// Setup pass that handles renderer configuration and setup logic.
        /// </summary>
        private class SetupPass : ScriptableRenderPass, IDisposable
        {
            private readonly IllusionRendererFeature _rendererFeature;
            
            private readonly IllusionRendererData _rendererData;

            private readonly PerObjectShadowCasterPass _perObjShadowPass;

            private readonly VolumetricFogPass _volumetricFogPass;

            private readonly ShadowCasterManager _sceneShadowCasterManager;

            private readonly VolumetricLightManager _volumetricLightManager;

            private RenderingData _renderingData;

            private static readonly ProfilingSampler SetupProfilingSampler = new("Illusion Setup Pass");

            public SetupPass(
                IllusionRendererFeature rendererFeature,
                IllusionRendererData rendererData,
                PerObjectShadowCasterPass perObjShadowPass,
                VolumetricFogPass volumetricFogPass,
                ShadowCasterManager sceneShadowCasterManager,
                VolumetricLightManager volumetricLightManager)
            {
                _rendererFeature = rendererFeature;
                _rendererData = rendererData;
                _perObjShadowPass = perObjShadowPass;
                _volumetricFogPass = volumetricFogPass;
                _sceneShadowCasterManager = sceneShadowCasterManager;
                _volumetricLightManager = volumetricLightManager;
                renderPassEvent = RenderPassEvent.BeforeRendering;
            }

            private static void PerformSetup(
                IllusionRendererFeature rendererFeature,
                ScriptableRenderer renderer,
                ref RenderingData renderingData,
                IllusionRendererData rendererData,
                PerObjectShadowCasterPass perObjShadowPass,
                VolumetricFogPass volumetricFogPass,
                ShadowCasterManager sceneShadowCasterManager,
                VolumetricLightManager volumetricLightManager)
            {
                rendererFeature.UpdateRenderDataSettings();
                rendererData.Update(renderer, in renderingData);
                var config = IllusionRuntimeRenderingConfig.Get();
                bool isPreviewOrReflectCamera =
                    renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection;

                var contactShadowParam = VolumeManager.instance.stack.GetComponent<ContactShadows>();
                rendererData.ContactShadowsSampling = rendererFeature.contactShadows
                                                      && !isPreviewOrReflectCamera
                                                      && config.EnableContactShadows
                                                      && contactShadowParam.enable.value;
                rendererData.PCSSShadowSampling = rendererFeature.pcssShadows
                                                  && !isPreviewOrReflectCamera
                                                  && config.EnablePercentageCloserSoftShadows;

                var screenSpaceGlobalIlluminationParam =
                    VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();
                bool useScreenSpaceGlobalIllumination = rendererFeature.screenSpaceGlobalIllumination
                                                        && config.EnableScreenSpaceGlobalIllumination
                                                        && !isPreviewOrReflectCamera
                                                        && screenSpaceGlobalIlluminationParam.enable.value;
                rendererData.SampleScreenSpaceIndirectDiffuse = useScreenSpaceGlobalIllumination;

                var screenSpaceReflectionParam = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
                bool useScreenSpaceReflection = config.EnableScreenSpaceReflection
                                                && rendererFeature.screenSpaceReflection && !isPreviewOrReflectCamera
                                                && screenSpaceReflectionParam.enable.value;
                rendererData.SampleScreenSpaceReflection = useScreenSpaceReflection;
                rendererData.RequireHistoryDepthNormal = useScreenSpaceGlobalIllumination;

                // Re-order light shadow caster pass renderPassEvent better for async compute.
                // Ref: https://developer.nvidia.com/blog/advanced-api-performance-async-compute-and-overlap/
                bool enableAsyncCompute = rendererData.PreferComputeShader
                                          && IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute;

                // Shadow Caster has bug in URP14.0.12 when there is no main/additional light in scene which will clear pre-z.
                // So skip re-order when shadow is not rendered.
                bool reorderMainLightShadowPass = enableAsyncCompute && CanRenderMainLightShadow(ref renderingData);
                var mainLightShadowCasterPass = UniversalRenderingUtility.GetMainLightShadowCasterPass(renderer);
                if (mainLightShadowCasterPass != null)
                {
                    mainLightShadowCasterPass.renderPassEvent = reorderMainLightShadowPass
                        ? IllusionRenderPassEvent.LightsShadowCasterPass
                        : RenderPassEvent.BeforeRenderingShadows;
                }

                bool reorderAdditionalLightShadowPass =
                    enableAsyncCompute && CanRenderAdditionalLightShadow(renderingData);
                var additionalLightsShadowCasterPass =
                    UniversalRenderingUtility.GetAdditionalLightsShadowCasterPass(renderer);
                if (additionalLightsShadowCasterPass != null)
                {
                    additionalLightsShadowCasterPass.renderPassEvent = reorderAdditionalLightShadowPass
                        ? IllusionRenderPassEvent.LightsShadowCasterPass
                        : RenderPassEvent.BeforeRenderingShadows;
                }

                var shadow = VolumeManager.instance.stack.GetComponent<PerObjectShadows>();
                sceneShadowCasterManager.Cull(in renderingData,
                    PerObjectShadowCasterPass.MaxShadowCount,
                    shadow.perObjectShadowLengthOffset.value,
                    IllusionRuntimeRenderingConfig.Get().EnablePerObjectShadowDebug);
                perObjShadowPass.Setup(sceneShadowCasterManager, shadow.perObjectShadowTileResolution.value,
                    shadow.perObjectShadowDepthBits.value);
                volumetricFogPass.Setup(volumetricLightManager);
            }

            // Ref: MainLightShadowCasterPass.Setup
            private static bool CanRenderMainLightShadow(ref RenderingData renderingData)
            {
                if (!renderingData.shadowData.mainLightShadowsEnabled)
                    return false;

#if UNITY_EDITOR
                if (CoreUtils.IsSceneLightingDisabled(renderingData.cameraData.camera))
                    return false;
#endif

                if (!renderingData.shadowData.supportsMainLightShadows)
                    return false;

                int shadowLightIndex = renderingData.lightData.mainLightIndex;
                if (shadowLightIndex == -1)
                    return false;

                VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];
                Light light = shadowLight.light;
                if (light.shadows == LightShadows.None)
                    return false;

                if (!renderingData.cullResults.GetShadowCasterBounds(shadowLightIndex, out _))
                    return false;

                return true;
            }

            private static bool CanRenderAdditionalLightShadow(in RenderingData renderingData)
            {
                return renderingData.shadowData.supportsAdditionalLightShadows;
            }
            
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, SetupProfilingSampler))
                {
                    PerformSetup(_rendererFeature, renderingData.cameraData.renderer, 
                        ref renderingData, _rendererData, 
                        _perObjShadowPass, _volumetricFogPass,
                        _sceneShadowCasterManager, _volumetricLightManager);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

#if UNITY_2023_1_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources,
                ref RenderingData renderingData)
            {
                using (new ProfilingScope((CommandBuffer)null, SetupProfilingSampler))
                {
                    PerformSetup(_rendererFeature, renderingData.cameraData.renderer, 
                        ref renderingData, _rendererData, 
                        _perObjShadowPass, _volumetricFogPass,
                        _sceneShadowCasterManager, _volumetricLightManager);
                }
            }
#endif

            public void Dispose()
            {
                // pass
            }
        }
    }
}

