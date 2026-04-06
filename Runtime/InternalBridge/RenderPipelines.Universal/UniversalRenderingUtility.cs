using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Utility class for Universal Rendering.
    /// </summary>
    public static class UniversalRenderingUtility
    {
        private static class UniversalRenderPassField<TRenderPass> where TRenderPass : ScriptableRenderPass
        {
            private static FieldInfo _fieldInfo;

            public static TRenderPass Get(ScriptableRenderer sr)
            {
                if (sr is not UniversalRenderer universalRenderer) return null;
                if (_fieldInfo == null)
                {
                    _fieldInfo = typeof(UniversalRenderer).GetField($"m_{typeof(TRenderPass).Name.Replace("Render", "")}",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
                return _fieldInfo!.GetValue(universalRenderer) as TRenderPass;
            }
        }

        private static FieldInfo _motionVectorDepthFieldInfo;
        
        private static FieldInfo _activeRenderPassQueueFieldInfo;
        
        private static FieldInfo _shadowSliceDataFieldInfo;

        /// <summary>
        /// Get UniversalRenderer depth texture that actually written to.
        /// </summary>
        /// <param name="frameData"></param>
        /// <returns></returns>
        public static TextureHandle GetDepthWriteTextureHandle(this ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var depthTexture = res.cameraDepthTexture;
            // Reference: DepthNormalOnlyPass, PreZ will output depth to attachment directly.
            if (cameraData.renderer.useDepthPriming
                && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
            {
                depthTexture = res.cameraDepth;
            }

            return depthTexture;
        }

        /// <summary>
        /// Ensures depth resources match URP's RenderGraph allocation (see UniversalRendererRenderGraph.CreateCameraDepthCopyTexture
        /// and CreateIntermediateCameraDepthAttachment). Fills <c>_cameraDepthTexture</c> via backing field (internal setter)
        /// and assigns <see cref="UniversalResourceData.cameraDepth"/> when depth priming applies.
        /// </summary>
        public static void EnsureCameraDepthGraphicsResourcesForFrame(RenderGraph renderGraph,
            UniversalResourceData resourceData, UniversalCameraData cameraData)
        {
            if (cameraData.renderer is not UniversalRenderer universalRenderer)
                return;

            var targetDesc = cameraData.cameraTargetDescriptor;
            int w = targetDesc.width;
            int h = targetDesc.height;
            Color clearValue = cameraData.backgroundColor;
            
            {
                // CreateCameraDepthCopyTexture: depth-stencil format, MSAA off (URP uses TextureDesc.format).
                var desc = new TextureDesc(w, h)
                {
                    format = universalRenderer.cameraDepthTextureFormat,
                    msaaSamples = MSAASamples.None,
                    name = "_CameraDepthTexture",
                    clearBuffer = true,
                    clearColor = clearValue
                };
                TextureHandle depthCopy = renderGraph.CreateTexture(desc);
                resourceData.cameraDepthTexture = depthCopy;
            }

            bool usePrimedDepth = cameraData.renderer.useDepthPriming
                                  && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth);
            if (usePrimedDepth)
            {
                var desc = new TextureDesc(w, h)
                {
                    format = universalRenderer.cameraDepthAttachmentFormat,
                    msaaSamples = (MSAASamples)targetDesc.msaaSamples,
                    name = "_CameraDepthAttachment",
                    clearBuffer = true,
                    clearColor = clearValue
                };
                resourceData.cameraDepth = renderGraph.CreateTexture(desc);
            }
        }

        /// <summary>
        /// Ensures <see cref="UniversalResourceData.cameraNormalsTexture"/> matches URP's RenderGraph allocation
        /// (see UniversalRendererRenderGraph.CreateCameraNormalsTexture). If the handle is invalid, creates the same
        /// kind of texture and assigns it to frame resources so all passes reading <c>resource.cameraNormalsTexture</c> stay consistent.
        /// </summary>
        public static TextureHandle EnsureCameraNormalsTextureForFrame(RenderGraph renderGraph,
            UniversalResourceData resourceData, UniversalCameraData cameraData)
        {
            TextureHandle handle = resourceData.cameraNormalsTexture;
            if (handle.IsValid())
                return handle;

            var targetDesc = cameraData.cameraTargetDescriptor;
            int w = targetDesc.width;
            int h = targetDesc.height;
            GraphicsFormat format = DepthNormalOnlyPass.GetGraphicsFormat();

            var desc = new TextureDesc(w, h)
            {
                colorFormat = format,
                msaaSamples = MSAASamples.None,
                name = "_CameraNormalsTexture",
                clearBuffer = true,
                clearColor = Color.black
            };

            handle = renderGraph.CreateTexture(desc);
            resourceData.cameraNormalsTexture = handle;
            return handle;
        }
        
        public static DrawingSettings CreateDrawingSettings(ShaderTagId shaderTagId, ContextContainer frameData, SortingCriteria sortingCriteria)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            return RenderingUtils.CreateDrawingSettings(shaderTagId, universalRenderingData, cameraData, lightData, sortingCriteria);
        }
              
        public static DrawingSettings CreateDrawingSettings(List<ShaderTagId> shaderTagIdList, ContextContainer frameData, SortingCriteria sortingCriteria)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            return RenderingUtils.CreateDrawingSettings(shaderTagIdList, universalRenderingData, cameraData, lightData, sortingCriteria);
        }

        /// <summary>
        /// Get UniversalRenderer motion vector render pass.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        internal static MotionVectorRenderPass GetMotionVectorRenderPass(ScriptableRenderer sr)
        {
            return UniversalRenderPassField<MotionVectorRenderPass>.Get(sr);
        }
        
        /// <summary>
        /// Get UniversalRenderer main light shadow caster pass.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        internal static MainLightShadowCasterPass GetMainLightShadowCasterPass(ScriptableRenderer sr)
        {
            return UniversalRenderPassField<MainLightShadowCasterPass>.Get(sr);
        }
        
        /// <summary>
        /// Get UniversalRenderer main light shadow caster shadow slice data.
        /// </summary>
        /// <param name="pass"></param>
        /// <returns></returns>
        internal static ShadowSliceData[] GetMainLightShadowSliceData(MainLightShadowCasterPass pass)
        {
            if (_shadowSliceDataFieldInfo == null)
            {
                _shadowSliceDataFieldInfo = typeof(MainLightShadowCasterPass).GetField("m_CascadeSlices",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            return _shadowSliceDataFieldInfo!.GetValue(pass) as ShadowSliceData[];
        }
        
        /// <summary>
        /// Get UniversalRenderer additional lights shadow caster pass.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        internal static AdditionalLightsShadowCasterPass GetAdditionalLightsShadowCasterPass(ScriptableRenderer sr)
        {
            return UniversalRenderPassField<AdditionalLightsShadowCasterPass>.Get(sr);
        }

        /// <summary>
        /// Returns if the camera renders to a offscreen depth texture.
        /// </summary>
        /// <param name="cameraData">The camera data for the camera being rendered.</param>
        /// <returns>Returns true if the camera renders to depth without any color buffer. It will return false otherwise.</returns>
        public static bool IsOffscreenDepthTexture(in CameraData cameraData)
        {
            return cameraData.targetTexture != null && cameraData.targetTexture.format == RenderTextureFormat.Depth;
        }
        
        /// <summary>
        /// Get ScriptableRenderer active render pass queue.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static List<ScriptableRenderPass> GetActiveRenderPassQueue(ScriptableRenderer sr)
        {
            if (_activeRenderPassQueueFieldInfo == null)
            {
                _activeRenderPassQueueFieldInfo = typeof(ScriptableRenderer).GetField("m_ActiveRenderPassQueue",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_activeRenderPassQueueFieldInfo!.GetValue(sr) is not List<ScriptableRenderPass> queue) return null;
            return queue;
        }

        /// <summary>
        /// Get UniversalRenderer rendering mode actual.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RenderingMode GetRenderingModeActual(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return RenderingMode.Forward;
            return universalRenderer.renderingModeActual;
        }

        /// <summary>
        /// Returns true if contains renderer feature with specified type.
        /// </summary>
        /// <typeparam name="T">Renderer Feature type.</typeparam>
        /// <returns></returns>
        public static bool TryGetRendererFeature<T>(ScriptableRendererData sr, out T rendererFeature)
            where T : ScriptableRendererFeature
        {
            foreach (var target in sr.rendererFeatures)
            {
                if (target is not T feature) continue;
                rendererFeature = feature;
                return true;
            }
            rendererFeature = null;
            return false;
        }

        /// <summary>
        /// Set UniversalAdditionalCameraData temporal AA quality.
        /// </summary>
        /// <param name="additionalCameraData"></param>
        /// <param name="quality"></param>
        public static void SetTemporalAAQuality(UniversalAdditionalCameraData additionalCameraData, int quality)
        {
            additionalCameraData.taaSettings.quality = (TemporalAAQuality)quality;
        }

        /// <summary>
        /// Get UniversalRenderPipelineAsset default renderer data.
        /// </summary>
        /// <param name="renderPipelineAsset"></param>
        /// <returns></returns>
        public static ScriptableRendererData GetDefaultRendererData(UniversalRenderPipelineAsset renderPipelineAsset)
        {
            return renderPipelineAsset.m_RendererDataList[renderPipelineAsset.m_DefaultRendererIndex];
        }

        /// <summary>
        /// Convert uint to valid rendering layers.
        /// </summary>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public static uint ToValidRenderingLayers(uint maxValue)
        {
            return RenderingLayerUtils.ToValidRenderingLayers(maxValue);
        }
    }
}