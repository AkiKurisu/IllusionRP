// Modified from https://github.com/stalomeow/StarRailNPRShader and https://github.com/recaeee/RecaNoMaho_P
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.Shadows
{
    public class ScreenSpaceShadowsPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _shadowMaterial = new(IllusionShaders.ScreenSpaceShadows);

        private readonly LazyMaterial _penumbraMaskMat = new(IllusionShaders.PenumbraMask);

        private readonly IllusionRendererData _rendererData;

        private struct PcssCascadeData
        {
            public Vector4 DirLightPcssParams0;

            public Vector4 DirLightPcssParams1;
        }

        private RenderTextureDescriptor _penumbraMaskDesc;

        private RTHandle _penumbraMaskTex;

        private RTHandle _penumbraMaskBlurTempTex;

        private int _colorAttachmentWidth;

        private int _colorAttachmentHeight;

        private readonly PcssCascadeData[] _pcssCascadeData;

        private readonly Vector4[] _cascadeOffsetScales;

        private readonly Vector4[] _dirLightPcssParams0;

        private readonly Vector4[] _dirLightPcssParams1;

        private readonly ProfilingSampler _pcssPenumbraSampler;

        private readonly ProfilingSampler _screenSpaceShadowSampler;

        public ScreenSpaceShadowsPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceShadowsPass;
            profilingSampler = new ProfilingSampler("Screen Space Shadows");
            _screenSpaceShadowSampler = new ProfilingSampler("Screen Space Shadows");

            // PCSS
            _pcssPenumbraSampler = new ProfilingSampler("PCSS Penumbra");
            _penumbraMaskDesc = new RenderTextureDescriptor();
            _pcssCascadeData = new PcssCascadeData[IllusionRendererData.ShadowCascadeCount];
            _cascadeOffsetScales = new Vector4[IllusionRendererData.ShadowCascadeCount];
            _dirLightPcssParams0 = new Vector4[IllusionRendererData.ShadowCascadeCount];
            _dirLightPcssParams1 = new Vector4[IllusionRendererData.ShadowCascadeCount];
        }

        public void Dispose()
        {
            _shadowMaterial.DestroyCache();
            _penumbraMaskMat.DestroyCache();

            _penumbraMaskTex?.Release();
            _penumbraMaskTex = null;

            _penumbraMaskBlurTempTex?.Release();
            _penumbraMaskBlurTempTex = null;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

#if UNITY_2023_1_OR_NEWER
        private class PenumbraPassData
        {
            internal TextureHandle PenumbraMaskTexture;
            internal TextureHandle PenumbraMaskBlurTempTexture;
            internal TextureHandle DepthTexture;
            internal RenderingData RenderingData;
            internal IllusionRendererData RendererData;
            internal Material PenumbraMaskMaterial;
            internal RenderTextureDescriptor PenumbraMaskDesc;
            internal int ColorAttachmentWidth;
            internal int ColorAttachmentHeight;
            internal Vector4[] CascadeOffsetScales;
            internal Vector4[] DirLightPcssParams0;
            internal Vector4[] DirLightPcssParams1;
        }

        private class ShadowPassData
        {
            internal TextureHandle ScreenSpaceShadowsTexture;
            internal RenderingData RenderingData;
            internal IllusionRendererData RendererData;
            internal Material ShadowMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            SetupPenumbraMask(descriptor);
            
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;

            // Create screen space shadows texture
            TextureHandle screenSpaceShadowsTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_ScreenSpaceShadowmapTexture", true);
            
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle depthTexture = renderer.activeDepthTexture;
            
            // Set global texture for screen space shadows
            RenderGraphUtils.SetGlobalTexture(renderGraph, "_ScreenSpaceShadowmapTexture", screenSpaceShadowsTexture);

            // PCSS Penumbra Pass - Use UnsafePass for multiple render target switching
            if (_rendererData.PCSSShadowSampling)
            {
                TextureHandle penumbraMaskTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, _penumbraMaskDesc, "_PenumbraMaskTex", false);
                TextureHandle penumbraMaskBlurTempTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, _penumbraMaskDesc, "_PenumbraMaskBlurTempTex", false);

                using (var builder = renderGraph.AddLowLevelPass<PenumbraPassData>("PCSS Penumbra Pass", out var passData, _pcssPenumbraSampler))
                {
                    passData.PenumbraMaskTexture = builder.UseTexture(penumbraMaskTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
                    passData.PenumbraMaskBlurTempTexture = builder.UseTexture(penumbraMaskBlurTempTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
                    passData.DepthTexture = builder.UseTexture(depthTexture);
                    passData.RenderingData = renderingData;
                    passData.RendererData = _rendererData;
                    passData.PenumbraMaskMaterial = _penumbraMaskMat.Value;
                    passData.PenumbraMaskDesc = _penumbraMaskDesc;
                    passData.ColorAttachmentWidth = _colorAttachmentWidth;
                    passData.ColorAttachmentHeight = _colorAttachmentHeight;
                    passData.CascadeOffsetScales = _cascadeOffsetScales;
                    passData.DirLightPcssParams0 = _dirLightPcssParams0;
                    passData.DirLightPcssParams1 = _dirLightPcssParams1;

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PenumbraPassData data, LowLevelGraphContext context) =>
                    {
                        ExecutePenumbraPass(context.cmd, data);
                    });
                }
            }

            // Screen Space Shadows Pass - Use RasterPass for single render target
            using (var builder = renderGraph.AddRasterRenderPass<ShadowPassData>("Screen Space Shadows Pass", out var passData, _screenSpaceShadowSampler))
            {
                passData.ScreenSpaceShadowsTexture = builder.UseTextureFragment(screenSpaceShadowsTexture, 0);
                passData.RenderingData = renderingData;
                passData.RendererData = _rendererData;
                passData.ShadowMaterial = _shadowMaterial.Value;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((ShadowPassData data, RasterGraphContext rgContext) =>
                {
                    ExecuteShadowPass(rgContext.cmd, data);
                });
            }
        }

        private static void ExecutePenumbraPass(LowLevelCommandBuffer cmd, PenumbraPassData data)
        {
            cmd.EnableShaderKeyword(IllusionShaderKeywords._PCSS_SHADOWS);
            PackDirLightParamsRenderGraph(cmd, data);
            RenderPenumbraMaskRenderGraph(cmd, data);
        }

        private static void ExecuteShadowPass(RasterCommandBuffer cmd, ShadowPassData data)
        {
            Material material = data.ShadowMaterial;
            var config = IllusionRuntimeRenderingConfig.Get();
            var rendererData = data.RendererData;

            // Setup debug keywords
            var debugMode = config.ScreenSpaceShadowDebugMode;
            CoreUtils.SetKeyword(cmd, IllusionShaderKeywords._DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT, debugMode == ScreenSpaceShadowDebugMode.MainLightShadow);
            CoreUtils.SetKeyword(cmd, IllusionShaderKeywords._DEBUG_SCREEN_SPACE_SHADOW_CONTACT, debugMode == ScreenSpaceShadowDebugMode.ContactShadow);

            // Bind ContactShadow
            var contactShadowParam = VolumeManager.instance.stack.GetComponent<ContactShadows>();
            if (rendererData.ContactShadowsSampling)
            {
                var contactShadowRT = contactShadowParam.shadowDenoiser.value == ShadowDenoiser.Spatial
                    ? rendererData.ContactShadowsDenoisedRT
                    : rendererData.ContactShadowsRT;
                if (contactShadowRT.IsValid())
                {
                    material.SetTexture(IllusionShaderProperties._ContactShadowMap, contactShadowRT);
                }
            }
            CoreUtils.SetKeyword(cmd, IllusionShaderKeywords._CONTACT_SHADOWS, rendererData.ContactShadowsSampling);
            
            // Handle PCSS keyword
            if (rendererData.PCSSShadowSampling)
            {
                cmd.EnableShaderKeyword(IllusionShaderKeywords._PCSS_SHADOWS);
            }
            else
            {
                cmd.DisableShaderKeyword(IllusionShaderKeywords._PCSS_SHADOWS);
            }
            
            Blitter.BlitTexture(cmd, data.ScreenSpaceShadowsTexture, Vector2.one, material, 0);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
        }

        private static void RenderPenumbraMaskRenderGraph(LowLevelCommandBuffer cmd, PenumbraPassData data)
        {
            var material = data.PenumbraMaskMaterial;
            var penumbraMaskDesc = data.PenumbraMaskDesc;

            cmd.SetRenderTarget(data.PenumbraMaskTexture);
            cmd.SetGlobalVector(ShaderProperties.ColorAttachmentTexelSize,
                new Vector4(1f / data.ColorAttachmentWidth, 1f / data.ColorAttachmentHeight, data.ColorAttachmentWidth,
                    data.ColorAttachmentHeight));
            cmd.SetGlobalVector(ShaderProperties.PenumbraMaskTexelSize, new Vector4(1f / penumbraMaskDesc.width, 1f / penumbraMaskDesc.height, penumbraMaskDesc.width, penumbraMaskDesc.height));
            cmd.SetGlobalVector(ShaderProperties.BlitScaleBias, new Vector4(1, 1, 0, 0));
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);

            cmd.SetGlobalTexture(ShaderProperties.PenumbraMaskTex, data.PenumbraMaskTexture);
            cmd.SetRenderTarget(data.PenumbraMaskBlurTempTexture);
            cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1);

            cmd.SetGlobalTexture(ShaderProperties.PenumbraMaskTex, data.PenumbraMaskBlurTempTexture);
            cmd.SetRenderTarget(data.PenumbraMaskTexture);
            cmd.DrawProcedural(Matrix4x4.identity, material, 2, MeshTopology.Triangles, 3, 1);

            cmd.SetGlobalTexture(ShaderProperties.PenumbraMaskTex, data.PenumbraMaskTexture);
        }

        private static void PackDirLightParamsRenderGraph(LowLevelCommandBuffer cmd, PenumbraPassData data)
        {
            var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
            var renderingData = data.RenderingData;
            var rendererData = data.RendererData;
            
            if (renderingData.shadowData.supportsSoftShadows)
            {
                float renderTargetWidth = renderingData.shadowData.mainLightShadowmapWidth;
                float renderTargetHeight = renderingData.shadowData.mainLightShadowCascadesCount == 2
                    ? renderingData.shadowData.mainLightShadowmapHeight >> 1
                    : renderingData.shadowData.mainLightShadowmapHeight;
                float invShadowAtlasWidth = 1.0f / renderTargetWidth;
                float invShadowAtlasHeight = 1.0f / renderTargetHeight;
                var slices = rendererData.MainLightShadowSliceData;
                for (int i = 0; i < data.CascadeOffsetScales.Length; i++)
                {
                    data.CascadeOffsetScales[i] = new Vector4(
                        slices[i].offsetX * invShadowAtlasWidth,
                        slices[i].offsetY * invShadowAtlasHeight,
                        slices[i].resolution * invShadowAtlasWidth,
                        slices[i].resolution * invShadowAtlasHeight);
                }

                cmd.SetGlobalVectorArray(ShaderProperties.CascadeOffsetScales, data.CascadeOffsetScales);
            }

            cmd.SetGlobalFloat(ShaderProperties.FindBlockerSampleCount, pcssParams.findBlockerSampleCount.value);
            cmd.SetGlobalFloat(ShaderProperties.PcfSampleCount, pcssParams.pcfSampleCount.value);

            float lightAngularDiameter = pcssParams.angularDiameter.value;
            float dirlightDepth2Radius = Mathf.Tan(0.5f * Mathf.Deg2Rad * lightAngularDiameter);
            float minFilterAngularDiameter = Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, pcssParams.minFilterMaxAngularDiameter.value);
            float halfMinFilterAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(minFilterAngularDiameter, lightAngularDiameter));
            float halfBlockerSearchAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, lightAngularDiameter));

            for (int i = 0; i < IllusionRendererData.ShadowCascadeCount; ++i)
            {
                float shadowmapDepth2RadialScale = Mathf.Abs(rendererData.MainLightShadowDeviceProjectionMatrixs[i].m00 / rendererData.MainLightShadowDeviceProjectionMatrixs[i].m22);
                
                // Reuse arrays from data
                data.DirLightPcssParams0[i].x = dirlightDepth2Radius * shadowmapDepth2RadialScale;
                data.DirLightPcssParams0[i].y = 1.0f / data.DirLightPcssParams0[i].x;
                data.DirLightPcssParams0[i].z = pcssParams.maxPenumbraSize.value / (2.0f * halfMinFilterAngularDiameterTangent);
                data.DirLightPcssParams0[i].w = pcssParams.maxSamplingDistance.value;

                data.DirLightPcssParams1[i].x = pcssParams.minFilterSizeTexels.value;
                data.DirLightPcssParams1[i].y = 1.0f / (halfMinFilterAngularDiameterTangent * shadowmapDepth2RadialScale);
                data.DirLightPcssParams1[i].z = 1.0f / (halfBlockerSearchAngularDiameterTangent * shadowmapDepth2RadialScale);
            }

            cmd.SetGlobalVectorArray(ShaderProperties.DirLightPcssParams0, data.DirLightPcssParams0);
            cmd.SetGlobalVectorArray(ShaderProperties.DirLightPcssParams1, data.DirLightPcssParams1);
            cmd.SetGlobalVectorArray(ShaderProperties.DirLightPcssProjs, rendererData.MainLightShadowDeviceProjectionVectors);
        }
#endif
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            SetupPenumbraMask(descriptor);
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
#if UNITY_2023_1_OR_NEWER
            descriptor.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;
#else
            descriptor.graphicsFormat = RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;
#endif

            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.ScreenSpaceShadowsRT, descriptor, wrapMode: TextureWrapMode.Clamp,
                name: "_ScreenSpaceShadowmapTexture");
            cmd.SetGlobalTexture(_rendererData.ScreenSpaceShadowsRT.name, _rendererData.ScreenSpaceShadowsRT.nameID);

            ConfigureTarget(_rendererData.ScreenSpaceShadowsRT);
            ConfigureClear(ClearFlag.None, Color.white);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var depthTexture = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            var preDepthTexture = _rendererData.CameraPreDepthTextureRT;
            if (!preDepthTexture.IsValid())
            {
                preDepthTexture = depthTexture;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                using (new ProfilingScope(cmd, _pcssPenumbraSampler))
                {
                    DoPCSSPenumbra(cmd, context, ref renderingData, preDepthTexture);
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                using (new ProfilingScope(cmd, _screenSpaceShadowSampler))
                {
                    DoScreenSpaceShadows(cmd, context, ref renderingData, preDepthTexture);
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DoScreenSpaceShadows(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData, RTHandle depthTexture)
        {
            Material material = _shadowMaterial.Value;
            var config = IllusionRuntimeRenderingConfig.Get();

            // Setup debug keywords
            var debugMode = config.ScreenSpaceShadowDebugMode;
            CoreUtils.SetKeyword(cmd, IllusionShaderKeywords._DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT,
                debugMode == ScreenSpaceShadowDebugMode.MainLightShadow);
            CoreUtils.SetKeyword(cmd, IllusionShaderKeywords._DEBUG_SCREEN_SPACE_SHADOW_CONTACT,
                debugMode == ScreenSpaceShadowDebugMode.ContactShadow);

            // Bind ContactShadow
            var contactShadowParam = VolumeManager.instance.stack.GetComponent<ContactShadows>();
            if (_rendererData.ContactShadowsSampling)
            {
                var contactShadowRT = contactShadowParam.shadowDenoiser.value == ShadowDenoiser.Spatial
                    ? _rendererData.ContactShadowsDenoisedRT
                    : _rendererData.ContactShadowsRT;
                if (contactShadowRT.IsValid())
                {
                    material.SetTexture(IllusionShaderProperties._ContactShadowMap, contactShadowRT);
                }
            }
            CoreUtils.SetKeyword(cmd, IllusionShaderKeywords._CONTACT_SHADOWS, _rendererData.ContactShadowsSampling);
            
            material.SetTexture(IllusionShaderProperties._CameraDepthTexture, depthTexture);
            Blitter.BlitCameraTexture(cmd, _rendererData.ScreenSpaceShadowsRT, _rendererData.ScreenSpaceShadowsRT, material, 0);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
        }

        private void DoPCSSPenumbra(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData, RTHandle depthTexture)
        {
            using (new ProfilingScope(cmd, _pcssPenumbraSampler))
            {
                if (_rendererData.PCSSShadowSampling)
                {
                    cmd.EnableShaderKeyword(IllusionShaderKeywords._PCSS_SHADOWS);
                    PackDirLightParams(cmd, ref renderingData);

                    RenderPenumbraMask(cmd, depthTexture);
                }
                else
                {
                    cmd.DisableShaderKeyword(IllusionShaderKeywords._PCSS_SHADOWS);
                }
            }
        }

        private void SetupPenumbraMask(RenderTextureDescriptor cameraTargetDesc)
        {
            var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
            _penumbraMaskDesc = cameraTargetDesc;
            _penumbraMaskDesc.colorFormat = RenderTextureFormat.R8;
            _penumbraMaskDesc.graphicsFormat = GraphicsFormat.R8_UNorm;
            _penumbraMaskDesc.depthStencilFormat = GraphicsFormat.None;
            _penumbraMaskDesc.autoGenerateMips = false;
            _penumbraMaskDesc.useMipMap = false;
            _penumbraMaskDesc.msaaSamples = 1;
            _penumbraMaskDesc.width = cameraTargetDesc.width / pcssParams.penumbraMaskScale.value;
            _penumbraMaskDesc.height = cameraTargetDesc.height / pcssParams.penumbraMaskScale.value;
            _colorAttachmentWidth = cameraTargetDesc.width;
            _colorAttachmentHeight = cameraTargetDesc.height;

            RenderingUtils.ReAllocateIfNeeded(ref _penumbraMaskTex, _penumbraMaskDesc,
                wrapMode: TextureWrapMode.Clamp, filterMode: FilterMode.Bilinear,
                name: "_PenumbraMaskTex");

            RenderingUtils.ReAllocateIfNeeded(ref _penumbraMaskBlurTempTex, _penumbraMaskDesc,
                wrapMode: TextureWrapMode.Clamp, filterMode: FilterMode.Bilinear,
                name: "_PenumbraMaskBlurTempTex");
        }

        private void RenderPenumbraMask(CommandBuffer cmd, RTHandle depthTexture)
        {
            var material = _penumbraMaskMat.Value;
            material.SetTexture(IllusionShaderProperties._CameraDepthTexture, depthTexture);

            cmd.SetRenderTarget(_penumbraMaskTex, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetGlobalVector(ShaderProperties.ColorAttachmentTexelSize,
                new Vector4(1f / _colorAttachmentWidth, 1f / _colorAttachmentHeight, _colorAttachmentWidth,
                    _colorAttachmentHeight));
            cmd.SetGlobalVector(ShaderProperties.PenumbraMaskTexelSize, new Vector4(1f / _penumbraMaskDesc.width, 1f / _penumbraMaskDesc.height, _penumbraMaskDesc.width, _penumbraMaskDesc.height));
            cmd.SetGlobalVector(ShaderProperties.BlitScaleBias, new Vector4(1, 1, 0, 0));
            IllusionRenderingUtils.DrawFullScreenTriangle(cmd, material, 0);

            cmd.SetGlobalTexture(ShaderProperties.PenumbraMaskTex, _penumbraMaskTex);
            cmd.SetRenderTarget(_penumbraMaskBlurTempTex, RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store);
            IllusionRenderingUtils.DrawFullScreenTriangle(cmd, material, 1);

            cmd.SetGlobalTexture(ShaderProperties.PenumbraMaskTex, _penumbraMaskBlurTempTex);
            cmd.SetRenderTarget(_penumbraMaskTex, RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store);
            IllusionRenderingUtils.DrawFullScreenTriangle(cmd, material, 2);

            cmd.SetGlobalTexture(ShaderProperties.PenumbraMaskTex, _penumbraMaskTex);
        }

        private void PackDirLightParams(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
            // Ref： MainLightShadowCasterPass.Setup
            if (renderingData.shadowData.supportsSoftShadows)
            {
                float renderTargetWidth = renderingData.shadowData.mainLightShadowmapWidth;
                float renderTargetHeight = renderingData.shadowData.mainLightShadowCascadesCount == 2
                    ? renderingData.shadowData.mainLightShadowmapHeight >> 1
                    : renderingData.shadowData.mainLightShadowmapHeight;
                float invShadowAtlasWidth = 1.0f / renderTargetWidth;
                float invShadowAtlasHeight = 1.0f / renderTargetHeight;
                var slices = _rendererData.MainLightShadowSliceData;
                for (int i = 0; i < _cascadeOffsetScales.Length; i++)
                {
                    _cascadeOffsetScales[i] = new Vector4(
                        slices[i].offsetX * invShadowAtlasWidth,
                        slices[i].offsetY * invShadowAtlasHeight,
                        slices[i].resolution * invShadowAtlasWidth,
                        slices[i].resolution * invShadowAtlasHeight);
                }

                cmd.SetGlobalVectorArray(ShaderProperties.CascadeOffsetScales, _cascadeOffsetScales);
            }

            cmd.SetGlobalFloat(ShaderProperties.FindBlockerSampleCount, pcssParams.findBlockerSampleCount.value);
            cmd.SetGlobalFloat(ShaderProperties.PcfSampleCount, pcssParams.pcfSampleCount.value);

            float lightAngularDiameter = pcssParams.angularDiameter.value;
            float dirlightDepth2Radius =
                Mathf.Tan(0.5f * Mathf.Deg2Rad * lightAngularDiameter);
            float minFilterAngularDiameter =
                Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, pcssParams.minFilterMaxAngularDiameter.value);
            float halfMinFilterAngularDiameterTangent =
                Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(minFilterAngularDiameter, lightAngularDiameter));

            float halfBlockerSearchAngularDiameterTangent =
                Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, lightAngularDiameter));


            for (int i = 0; i < IllusionRendererData.ShadowCascadeCount; ++i)
            {
                float shadowmapDepth2RadialScale = Mathf.Abs(_rendererData.MainLightShadowDeviceProjectionMatrixs[i].m00 /
                                                             _rendererData.MainLightShadowDeviceProjectionMatrixs[i].m22);
                // depth2RadialScale
                _pcssCascadeData[i].DirLightPcssParams0.x = dirlightDepth2Radius * shadowmapDepth2RadialScale;
                // radial2DepthScale
                _pcssCascadeData[i].DirLightPcssParams0.y = 1.0f / _pcssCascadeData[i].DirLightPcssParams0.x;
                // maxBlockerDistance
                _pcssCascadeData[i].DirLightPcssParams0.z = pcssParams.maxPenumbraSize.value /
                                                            (2.0f * halfMinFilterAngularDiameterTangent);
                // maxSamplingDistance
                _pcssCascadeData[i].DirLightPcssParams0.w = pcssParams.maxSamplingDistance.value;

                // minFilterRadius(in texels)
                _pcssCascadeData[i].DirLightPcssParams1.x = pcssParams.minFilterSizeTexels.value;
                // minFilterRadial2DepthScale
                _pcssCascadeData[i].DirLightPcssParams1.y =
                    1.0f / (halfMinFilterAngularDiameterTangent * shadowmapDepth2RadialScale);
                // blockerRadial2DepthScale
                _pcssCascadeData[i].DirLightPcssParams1.z =
                    1.0f / (halfBlockerSearchAngularDiameterTangent * shadowmapDepth2RadialScale);
            }

            for (int i = 0; i < IllusionRendererData.ShadowCascadeCount; ++i)
            {
                _dirLightPcssParams0[i] = _pcssCascadeData[i].DirLightPcssParams0;
                _dirLightPcssParams1[i] = _pcssCascadeData[i].DirLightPcssParams1;
            }

            cmd.SetGlobalVectorArray(ShaderProperties.DirLightPcssParams0, _dirLightPcssParams0);
            cmd.SetGlobalVectorArray(ShaderProperties.DirLightPcssParams1, _dirLightPcssParams1);
            cmd.SetGlobalVectorArray(ShaderProperties.DirLightPcssProjs, _rendererData.MainLightShadowDeviceProjectionVectors);
        }

        private static class ShaderProperties
        {
            public static readonly int CascadeOffsetScales = Shader.PropertyToID("_CascadeOffsetScales");

            public static readonly int DirLightPcssParams0 = Shader.PropertyToID("_DirLightPcssParams0");

            public static readonly int DirLightPcssParams1 = Shader.PropertyToID("_DirLightPcssParams1");

            public static readonly int DirLightPcssProjs = Shader.PropertyToID("_DirLightPcssProjs");

            public static readonly int ColorAttachmentTexelSize = Shader.PropertyToID("_ColorAttachmentTexelSize");

            public static readonly int PenumbraMaskTexelSize = Shader.PropertyToID("_PenumbraMaskTexelSize");

            public static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

            public static readonly int PenumbraMaskTex = Shader.PropertyToID("_PenumbraMaskTex");

            public static readonly int FindBlockerSampleCount = Shader.PropertyToID("_FindBlockerSampleCount");

            public static readonly int PcfSampleCount = Shader.PropertyToID("_PcfSampleCount");
        }
    }
}
