using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering.Universal.Internal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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
        
        private static FieldInfo _opaqueColorFieldInfo;
        
        private static FieldInfo _normalsTextureFieldInfo;

        private static FieldInfo _motionVectorColorFieldInfo;

        private static FieldInfo _motionVectorDepthFieldInfo;
        
        private static FieldInfo _activeRenderPassQueueFieldInfo;
        
        private static FieldInfo _shadowSliceDataFieldInfo;

        /// <summary>
        /// Get UniversalRenderer active camera color attachment.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetActiveCameraColorAttachment(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            var colorAttachment = universalRenderer.m_ActiveCameraColorAttachment;
            if (colorAttachment == null || !colorAttachment.rt)
            {
                colorAttachment = universalRenderer.m_ColorBufferSystem.PeekBackBuffer();
            }
            return colorAttachment;
        }
        
        /// <summary>
        /// Returns the front-buffer color target. Returns null if not implemented by the renderer.
        /// It's only valid to call GetCameraColorFrontBuffer in the scope of <c>ScriptableRenderPass</c>.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetCameraColorFrontBuffer(CommandBuffer cmd, ScriptableRenderer sr)
        {
            return sr.GetCameraColorFrontBuffer(cmd);
        }

        /// <summary>
        /// Returns the back-buffer color target. Returns null if not implemented by the renderer.
        /// It's only valid to call GetCameraColorBackBuffer in the scope of <c>ScriptableRenderPass</c>.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetCameraColorBackBuffer(CommandBuffer cmd, ScriptableRenderer sr)
        {
            return sr.GetCameraColorBackBuffer(cmd);
        }

        /// <summary>
        /// Get UniversalRenderer final target.
        /// </summary>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        public static RTHandle GetCameraTargetTexture(ref RenderingData renderingData)
        {
            var cameraTarget = RenderingUtils.GetCameraTargetIdentifier(ref renderingData);
            RTHandleStaticHelpers.SetRTHandleStaticWrapper(cameraTarget);
            return RTHandleStaticHelpers.s_RTHandleWrapper;
        }

        /// <summary>
        /// Get UniversalRenderer m_DepthTexture texture.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetDepthTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            var depthBuffer = universalRenderer.m_DepthTexture;
            return depthBuffer;
        }
        
        /// <summary>
        /// Get UniversalRenderer m_OpaqueColor texture.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetOpaqueTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (_opaqueColorFieldInfo == null)
            {
                _opaqueColorFieldInfo = typeof(UniversalRenderer).GetField("m_OpaqueColor",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            return _opaqueColorFieldInfo!.GetValue(universalRenderer) as RTHandle;
        }

        /// <summary>
        /// Get UniversalRenderer depth texture that actually written to.
        /// </summary>
        /// <param name="cameraData"></param>
        /// <returns></returns>
        public static RTHandle GetDepthWriteTexture(ref CameraData cameraData)
        {
            var depthTexture = GetDepthTexture(cameraData.renderer);
            // Reference: DepthNormalOnlyPass, PreZ will output depth to attachment directly.
            if (cameraData.renderer.useDepthPriming
                && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
            {
                depthTexture = cameraData.renderer.cameraDepthTargetHandle;
            }

            return depthTexture;
        }
        
#if UNITY_2023_1_OR_NEWER
        /// <summary>
        /// Get UniversalRenderer depth texture that actually written to.
        /// </summary>
        /// <param name="cameraData"></param>
        /// <returns></returns>
        public static TextureHandle GetDepthWriteTextureHandle(ref CameraData cameraData)
        {
            UniversalRenderer renderer = (UniversalRenderer)cameraData.renderer;
            var depthTexture = renderer.resources.GetTexture(UniversalResource.CameraDepthTexture);
            // Reference: DepthNormalOnlyPass, PreZ will output depth to attachment directly.
            if (cameraData.renderer.useDepthPriming
                && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
            {
                depthTexture = renderer.resources.GetTexture(UniversalResource.CameraDepth);
            }

            return depthTexture;
        }
#endif

        /// <summary>
        /// Set UniversalRenderer depth texture.
        /// </summary>
        /// <param name="sr"></param>
        /// <param name="depthTexture"></param>
        public static void SetDepthTexture(ScriptableRenderer sr, RTHandle depthTexture)
        {
            if (sr is not UniversalRenderer universalRenderer) return;
            universalRenderer.m_DepthTexture = depthTexture;
        }

        /// <summary>
        /// Get UniversalRenderer camera depth attachment.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetCameraDepthAttachment(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            var depthBuffer = universalRenderer.m_CameraDepthAttachment;
            return depthBuffer;
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
        /// Get UniversalRenderer normals texture.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetNormalTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (_normalsTextureFieldInfo == null)
            {
                _normalsTextureFieldInfo = typeof(UniversalRenderer).GetField("m_NormalsTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_normalsTextureFieldInfo!.GetValue(universalRenderer) is not RTHandle normalBuffer) return null;
            return normalBuffer;
        }

        /// <summary>
        /// Get UniversalRenderer motion vector color.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetMotionVectorColor(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (_motionVectorColorFieldInfo == null)
            {
                _motionVectorColorFieldInfo = typeof(UniversalRenderer).GetField("m_MotionVectorColor",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_motionVectorColorFieldInfo!.GetValue(universalRenderer) is not RTHandle motionVectorColor) return null;
            return motionVectorColor;
        }

        /// <summary>
        /// Get UniversalRenderer motion vector depth.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetMotionVectorDepth(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (_motionVectorDepthFieldInfo == null)
            {
                _motionVectorDepthFieldInfo = typeof(UniversalRenderer).GetField("m_MotionVectorDepth",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_motionVectorDepthFieldInfo!.GetValue(universalRenderer) is not RTHandle motionVectorDepth) return null;
            return motionVectorDepth;
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
        /// Calculate UniversalAdditionalCameraData temporal AA jitter.
        /// </summary>
        /// <param name="cameraData"></param>
        /// <returns></returns>
        public static Vector2 CalculateTemporalAAJitter(CameraData cameraData)
        {
            int frameIndex = Time.frameCount + cameraData.taaSettings.jitterFrameCountOffset;
            return TemporalAA.CalculateJitter(frameIndex);
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