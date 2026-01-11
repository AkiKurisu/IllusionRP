// Modified from https://github.com/stalomeow/StarRailNPRShader and https://github.com/recaeee/RecaNoMaho_P
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    public class ScreenSpaceShadowsPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _shadowMaterial = new(IllusionShaders.ScreenSpaceShadows);

        private readonly LazyMaterial _penumbraMaskMat = new(IllusionShaders.PenumbraMask);

        private readonly IllusionRendererData _rendererData;

        private RenderTextureDescriptor _penumbraMaskDesc;

        private RTHandle _penumbraMaskTex;

        private RTHandle _penumbraMaskBlurTempTex;

        private int _colorAttachmentWidth;

        private int _colorAttachmentHeight;

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
            _cascadeOffsetScales = new Vector4[IllusionRendererData.ShadowCascadeCount];
            _dirLightPcssParams0 = new Vector4[IllusionRendererData.ShadowCascadeCount];
            _dirLightPcssParams1 = new Vector4[IllusionRendererData.ShadowCascadeCount];
            ConfigureInput(ScriptableRenderPassInput.Depth);
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

        private class PenumbraPassData
        {
            internal TextureHandle PenumbraMaskTexture;
            internal TextureHandle PenumbraMaskBlurTempTexture;
            internal TextureHandle DepthTexture;
            internal UniversalShadowData ShadowData;
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
            internal IllusionRendererData RendererData;
            internal Material ShadowMaterial;
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

            RenderingUtils.ReAllocateHandleIfNeeded(ref _penumbraMaskTex, _penumbraMaskDesc,
                wrapMode: TextureWrapMode.Clamp, filterMode: FilterMode.Bilinear,
                name: "_PenumbraMaskTex");

            RenderingUtils.ReAllocateHandleIfNeeded(ref _penumbraMaskBlurTempTex, _penumbraMaskDesc,
                wrapMode: TextureWrapMode.Clamp, filterMode: FilterMode.Bilinear,
                name: "_PenumbraMaskBlurTempTex");
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var shadowData = frameData.Get<UniversalShadowData>();
            var descriptor = cameraData.cameraTargetDescriptor;
            SetupPenumbraMask(descriptor);
            
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;

            // Create screen space shadows texture
            TextureHandle screenSpaceShadowsTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_ScreenSpaceShadowmapTexture", true);
            
            TextureHandle preDepthTexture = resource.activeDepthTexture;
            var preDepthRT = _rendererData.CameraPreDepthTextureRT;
            if (preDepthRT.IsValid())
            {
                preDepthTexture = renderGraph.ImportTexture(preDepthRT);
            }
            
            // Set global texture for screen space shadows
            RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderProperties._ScreenSpaceShadowmapTexture, screenSpaceShadowsTexture);

            // PCSS Penumbra Pass - Use UnsafePass for multiple render target switching
            if (_rendererData.PCSSShadowSampling)
            {
                TextureHandle penumbraMaskTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, _penumbraMaskDesc, "_PenumbraMaskTex", false);
                TextureHandle penumbraMaskBlurTempTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, _penumbraMaskDesc, "_PenumbraMaskBlurTempTex", false);

                using (var builder = renderGraph.AddUnsafePass<PenumbraPassData>("PCSS Penumbra Pass", out var passData, _pcssPenumbraSampler))
                {
                    builder.UseTexture(penumbraMaskTexture, AccessFlags.Write);
                    passData.PenumbraMaskTexture = penumbraMaskTexture;
                    builder.UseTexture(penumbraMaskBlurTempTexture, AccessFlags.Write);
                    passData.PenumbraMaskBlurTempTexture = penumbraMaskBlurTempTexture;
                    builder.UseTexture(preDepthTexture);
                    passData.DepthTexture = preDepthTexture;
                    passData.RendererData = _rendererData;
                    passData.PenumbraMaskMaterial = _penumbraMaskMat.Value;
                    passData.PenumbraMaskDesc = _penumbraMaskDesc;
                    passData.ColorAttachmentWidth = _colorAttachmentWidth;
                    passData.ColorAttachmentHeight = _colorAttachmentHeight;
                    passData.CascadeOffsetScales = _cascadeOffsetScales;
                    passData.DirLightPcssParams0 = _dirLightPcssParams0;
                    passData.DirLightPcssParams1 = _dirLightPcssParams1;
                    passData.ShadowData = shadowData;

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PenumbraPassData data, UnsafeGraphContext context) =>
                    {
                        ExecutePenumbraPass(context.cmd, data);
                    });
                }
            }

            // Screen Space Shadows Pass - Use RasterPass for single render target
            using (var builder = renderGraph.AddRasterRenderPass<ShadowPassData>("Screen Space Shadows Pass", out var passData, _screenSpaceShadowSampler))
            {
                builder.SetRenderAttachment(screenSpaceShadowsTexture, 0);
                passData.ScreenSpaceShadowsTexture = screenSpaceShadowsTexture;
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

        private static void ExecutePenumbraPass(UnsafeCommandBuffer cmd, PenumbraPassData data)
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

        private static void RenderPenumbraMaskRenderGraph(UnsafeCommandBuffer cmd, PenumbraPassData data)
        {
            var material = data.PenumbraMaskMaterial;
            var penumbraMaskDesc = data.PenumbraMaskDesc;
            material.SetTexture(IllusionShaderProperties._CameraDepthTexture, (RTHandle)data.DepthTexture);
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

        private static void PackDirLightParamsRenderGraph(UnsafeCommandBuffer cmd, PenumbraPassData data)
        {
            var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
            var rendererData = data.RendererData;
            
            if (data.ShadowData.supportsSoftShadows)
            {
                float renderTargetWidth = data.ShadowData.mainLightShadowmapWidth;
                float renderTargetHeight = data.ShadowData.mainLightShadowCascadesCount == 2
                    ? data.ShadowData.mainLightShadowmapHeight >> 1
                    : data.ShadowData.mainLightShadowmapHeight;
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
        
        private static class ShaderProperties
        {
            public static readonly int _ScreenSpaceShadowmapTexture = MemberNameHelpers.ShaderPropertyID();
            
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
