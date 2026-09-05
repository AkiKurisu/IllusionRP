using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.AreaLights
{
    /// <summary>
    /// Prepares rectangle area light data, renders the area shadow atlas and binds the area light globals.
    /// </summary>
    // Reference: HDShadowAtlas.RenderShadowMaps / EVSMBlurMoments, HDShadowManager.RenderShadows + BindShadowGlobalResources
    public sealed class AreaLightsPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;

        private readonly AreaLightManager _manager = new();

        private readonly Material _clearMaterial;

        private readonly ComputeShader _evsmShadowBlurMomentsCS;

        private readonly AreaLightCookieManager _cookieManager;

        private readonly ProfilingSampler _renderShadowMapsSampler = new("Render Area Light Shadow Maps");

        private readonly ProfilingSampler _evsmBlurSampler = new("EVSM Blur Moments");

        private readonly ProfilingSampler _globalsSampler = new("Area Light Globals");

        private readonly ProfilingSampler _cookiesSampler = new("Area Light Cookies");

        private bool _enabled;

        private bool _supportsShadows;

        private AreaLighting _settings;

        private HDAreaShadowFilteringQuality _areaShadowFilteringQuality;

        // The URP render graph does not hand the ScriptableRenderContext to passes, the second shadow caster culling
        // needs it before renderer lists are prepared, so it is captured per frame from the pipeline callback.
        private ScriptableRenderContext _renderContext;

        private bool _hasRenderContext;

        private bool _missingContextReported;

        private NativeArray<LightShadowCasterCullingInfo> _perLightInfos;

        private NativeArray<ShadowSplitData> _splitBuffer;

        private readonly RendererListHandle[] _shadowRendererLists = new RendererListHandle[AreaLightManager.k_MaxShadowRequests];

        private readonly int[] _finalAtlasTexture = new int[AreaLightManager.k_MaxShadowRequests];

        public AreaLightsPass(IllusionRendererData rendererData, CookieAtlasResolution cookieAtlasSize, CookieAtlasGraphicsFormat cookieFormat)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.AreaLightShadowPass;
            profilingSampler = new ProfilingSampler("Area Lights");
            var resources = rendererData.RuntimeResources;
            _clearMaterial = CoreUtils.CreateEngineMaterial(resources.areaLightShadowClearShader);
            _evsmShadowBlurMomentsCS = resources.evsmBlurCS;
            _cookieManager = new AreaLightCookieManager(resources.filterAreaLightCookiesShader, (int)cookieAtlasSize, (GraphicsFormat)cookieFormat, 0);
            LTCAreaLight.instance.Build();
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            LTCAreaLight.instance.Cleanup();
            CoreUtils.Destroy(_clearMaterial);
            _cookieManager.Release();
            _manager.Dispose();
            if (_perLightInfos.IsCreated) _perLightInfos.Dispose();
            if (_splitBuffer.IsCreated) _splitBuffer.Dispose();
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            _renderContext = context;
            _hasRenderContext = true;
            _cookieManager.NewFrame();
        }

        public void Setup(bool enabled, bool supportsShadows, AreaLighting settings,
            HDAreaShadowFilteringQuality areaShadowFilteringQuality)
        {
            _enabled = enabled;
            _supportsShadows = supportsShadows;
            _settings = settings;
            _areaShadowFilteringQuality = areaShadowFilteringQuality;
        }

        private class RenderShadowMapsPassData
        {
            public TextureHandle atlasTexture;
            public HDShadowRequest[] shadowRequests;
            public int shadowRequestCount;
            public Material clearMaterial;
            public CullingResults cullResults;
            public RendererListHandle[] shadowRendererLists;
        }

        private class EVSMBlurMomentsPassData
        {
            public TextureHandle atlasTexture;
            public TextureHandle momentAtlasTexture1;
            public TextureHandle momentAtlasTexture2;

            public ComputeShader evsmShadowBlurMomentsCS;
            public HDShadowRequest[] shadowRequests;
            public int shadowRequestCount;
            public int[] finalAtlasTexture;
        }

        private class CookiesPassData
        {
            public TextureHandle cookieAtlas;
            public AreaLightManager manager;
            public AreaLightCookieManager cookieManager;
        }

        private class GlobalsPassData
        {
            public TextureHandle shadowAtlas;
            public TextureHandle ltcData;
            public TextureHandle cookieAtlas;
            public AreaLightManager manager;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var shadowData = frameData.Get<UniversalShadowData>();
            var areaLightFrameData = frameData.GetOrCreate<AreaLightFrameData>();
            areaLightFrameData.Reset();

            TextureHandle blackTexture = renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());

            if (!_enabled || _settings == null)
            {
                _manager.Clear();
                BindShadowGlobalResources(renderGraph, blackTexture, blackTexture);
                return;
            }

            bool supportsShadows = _supportsShadows && _hasRenderContext;
            if (_supportsShadows && !_hasRenderContext && !_missingContextReported)
            {
                _missingContextReported = true;
                Debug.LogWarning("[IllusionRP] Area light shadows need the ScriptableRenderContext from RenderPipelineManager.beginContextRendering, shadows are skipped.");
            }

            // Imported once so the cookie write and the global bind share one render graph resource.
            TextureHandle cookieAtlas = renderGraph.ImportTexture(_cookieManager.atlasTexture);
            _manager.PrepareLights(cameraData, lightData, _settings, _areaShadowFilteringQuality, supportsShadows, _cookieManager);

            TextureHandle areaShadowResult = blackTexture;
            if (_manager.shadowRequestCount > 0)
            {
                CullShadowCasters(renderGraph, renderingData.cullResults, shadowData);
                TextureHandle depthAtlas = RenderShadowMaps(renderGraph, renderingData.cullResults);
                areaShadowResult = _manager.atlas.HasBlurredEVSM() ? EVSMBlurMoments(renderGraph, depthAtlas) : depthAtlas;

                areaLightFrameData.ShadowAtlas = areaShadowResult;
                areaLightFrameData.Atlas = _manager.atlas;
                areaLightFrameData.ShadowRequestCount = _manager.shadowRequestCount;
            }

            if (_manager.hasCookies)
                FetchCookies(renderGraph, cookieAtlas);

            BindShadowGlobalResources(renderGraph, areaShadowResult, cookieAtlas);

            // The camera need to be setup again after the shadows since those passes override some settings
            UniversalRenderer renderer = (UniversalRenderer)cameraData.renderer;
            renderer.SetupRenderGraphCameraProperties(renderGraph, resource.activeColorTexture);
        }

        // Resubmit the URP per-light culling infos plus one perspective split per rectangle light shadow request,
        // then create the shadow renderer lists through the render graph like the URP shadow passes do.
        private void CullShadowCasters(RenderGraph renderGraph, in CullingResults cullResults, UniversalShadowData shadowData)
        {
            ShadowCastersCullingInfos shadowCullingInfos = BuildShadowCasterCullingInfos(shadowData, cullResults);
            _renderContext.CullShadowCasters(cullResults, shadowCullingInfos);

            for (int i = 0; i < _manager.shadowRequestCount; i++)
            {
                ref var shadowRequest = ref _manager.shadowRequests[i];
                var shadowDrawSettings = new ShadowDrawingSettings(cullResults, shadowRequest.lightIndex);
                shadowDrawSettings.useRenderingLayerMaskTest = UniversalRenderPipeline.asset.useRenderingLayers;
                shadowDrawSettings.splitIndex = shadowRequest.cullingSplit.splitIndex;
                _shadowRendererLists[i] = renderGraph.CreateShadowRendererList(ref shadowDrawSettings);
            }
        }

        // URP ShadowCulling only builds Directional / Point / Spot splits, so the rectangle lights are appended here.
        private ShadowCastersCullingInfos BuildShadowCasterCullingInfos(UniversalShadowData shadowData, in CullingResults cullResults)
        {
            var visibleLights = cullResults.visibleLights;
            int lightCount = visibleLights.Length;
            var urpInfos = shadowData.visibleLightsShadowCullingInfos;
            bool hasUrpInfos = urpInfos.IsCreated && urpInfos.Length == lightCount;

            int totalSplitCount = _manager.shadowRequestCount;
            if (hasUrpInfos)
            {
                for (int i = 0; i < lightCount; i++)
                {
                    var infos = urpInfos[i];
                    if (infos.slices.IsCreated)
                        totalSplitCount += infos.slices.Length;
                }
            }

            EnsureCapacity(ref _perLightInfos, lightCount);
            EnsureCapacity(ref _splitBuffer, Mathf.Max(1, totalSplitCount));

            int splitBufferOffset = 0;
            for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
            {
                int splitCount = 0;
                BatchCullingProjectionType projectionType = BatchCullingProjectionType.Unknown;

                if (hasUrpInfos)
                {
                    var infos = urpInfos[lightIndex];
                    if (infos.slices.IsCreated)
                    {
                        for (int i = 0; i < infos.slices.Length; i++)
                            _splitBuffer[splitBufferOffset + i] = infos.slices[i].splitData;
                        splitCount = infos.slices.Length;
                        projectionType = GetCullingProjectionType(visibleLights[lightIndex].lightType);
                    }
                }

                int requestIndex = FindShadowRequest(lightIndex);
                if (requestIndex >= 0)
                {
                    _splitBuffer[splitBufferOffset] = _manager.shadowRequests[requestIndex].splitData;
                    splitCount = 1;
                    projectionType = BatchCullingProjectionType.Perspective;
                }

                _perLightInfos[lightIndex] = new LightShadowCasterCullingInfo
                {
                    splitRange = new RangeInt(splitBufferOffset, splitCount),
                    projectionType = projectionType,
                };
                splitBufferOffset += splitCount;
            }

            ShadowCastersCullingInfos shadowCullingInfos = default;
            shadowCullingInfos.splitBuffer = _splitBuffer.GetSubArray(0, splitBufferOffset);
            shadowCullingInfos.perLightInfos = _perLightInfos.GetSubArray(0, lightCount);
            return shadowCullingInfos;
        }

        private int FindShadowRequest(int lightIndex)
        {
            for (int i = 0; i < _manager.shadowRequestCount; i++)
            {
                if (_manager.shadowRequests[i].lightIndex == lightIndex)
                    return i;
            }

            return -1;
        }

        private static BatchCullingProjectionType GetCullingProjectionType(LightType type)
        {
            switch (type)
            {
                case LightType.Point: return BatchCullingProjectionType.Perspective;
                case LightType.Spot: return BatchCullingProjectionType.Perspective;
                case LightType.Directional: return BatchCullingProjectionType.Orthographic;
            }

            return BatchCullingProjectionType.Unknown;
        }

        private static void EnsureCapacity<T>(ref NativeArray<T> array, int count) where T : struct
        {
            if (array.IsCreated && array.Length >= count)
                return;
            if (array.IsCreated)
                array.Dispose();
            array = new NativeArray<T>(Mathf.NextPowerOfTwo(count), Allocator.Persistent);
        }

        private TextureHandle RenderShadowMaps(RenderGraph renderGraph, in CullingResults cullResults)
        {
            TextureHandle atlasTexture;

            using (var builder = renderGraph.AddUnsafePass<RenderShadowMapsPassData>("Render Area Light Shadow Maps", out var passData, _renderShadowMapsSampler))
            {
                passData.atlasTexture = renderGraph.CreateTexture(_manager.atlas.GetShadowMapTextureDesc());
                builder.UseTexture(passData.atlasTexture, AccessFlags.Write);

                passData.shadowRequests = _manager.shadowRequests;
                passData.shadowRequestCount = _manager.shadowRequestCount;
                passData.clearMaterial = _clearMaterial;
                passData.cullResults = cullResults;
                passData.shadowRendererLists = _shadowRendererLists;
                for (int i = 0; i < _manager.shadowRequestCount; i++)
                    builder.UseRendererList(_shadowRendererLists[i]);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(
                    static (RenderShadowMapsPassData data, UnsafeGraphContext ctx) =>
                    {
                        var natCmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                        natCmd.SetRenderTarget(data.atlasTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                        // Casters see a punctual light with zero vertex bias, slope scale depth bias is applied at rasterization instead.
                        CoreUtils.SetKeyword(natCmd, ShaderKeywordStrings.CastingPunctualLightShadow, true);

                        for (int i = 0; i < data.shadowRequestCount; i++)
                        {
                            ref var shadowRequest = ref data.shadowRequests[i];
                            CommonPerShadowRequestUpdate(natCmd, data, ref shadowRequest);

                            CoreUtils.DrawFullScreen(natCmd, data.clearMaterial, null, 0);

                            natCmd.DrawRendererList(data.shadowRendererLists[i]);
                        }

                        ResetDepthState(natCmd);
                        CoreUtils.SetKeyword(natCmd, ShaderKeywordStrings.CastingPunctualLightShadow, false);
                    });

                atlasTexture = passData.atlasTexture;
            }

            return atlasTexture;
        }

        private static void CommonPerShadowRequestUpdate(CommandBuffer cmd, RenderShadowMapsPassData data, ref HDShadowRequest shadowRequest)
        {
            cmd.SetGlobalDepthBias(1.0f, shadowRequest.slopeBias);
            cmd.SetViewport(shadowRequest.dynamicAtlasViewport);

            // Setup matrices for shadow rendering:
            Matrix4x4 view = shadowRequest.cullingSplit.view;
            cmd.SetViewProjectionMatrices(view, shadowRequest.cullingSplit.projection);

            VisibleLight shadowLight = data.cullResults.visibleLights[shadowRequest.lightIndex];
            ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref shadowLight, Vector4.zero);
        }

        private static void ResetDepthState(CommandBuffer cmd)
        {
            cmd.SetGlobalDepthBias(0.0f, 0.0f);             // Reset depth bias.
        }

        private TextureHandle EVSMBlurMoments(RenderGraph renderGraph, TextureHandle inputAtlas)
        {
            using (var builder = renderGraph.AddUnsafePass<EVSMBlurMomentsPassData>("EVSM Blur Moments", out var passData, _evsmBlurSampler))
            {
                passData.evsmShadowBlurMomentsCS = _evsmShadowBlurMomentsCS;
                passData.shadowRequests = _manager.shadowRequests;
                passData.shadowRequestCount = _manager.shadowRequestCount;
                passData.finalAtlasTexture = _finalAtlasTexture;
                passData.atlasTexture = inputAtlas;
                builder.UseTexture(passData.atlasTexture, AccessFlags.Read);
                passData.momentAtlasTexture1 = renderGraph.CreateTexture(_manager.atlas.GetMomentAtlasDesc());
                builder.UseTexture(passData.momentAtlasTexture1, AccessFlags.Write);
                passData.momentAtlasTexture2 = renderGraph.CreateTexture(_manager.atlas.GetMomentAtlasDesc(true));
                builder.UseTexture(passData.momentAtlasTexture2, AccessFlags.Write);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(
                    static (EVSMBlurMomentsPassData data, UnsafeGraphContext ctx) =>
                    {
                        var natCmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                        ComputeShader shadowBlurMomentsCS = data.evsmShadowBlurMomentsCS;
                        RTHandle[] momentAtlasRenderTextures = ctx.renderGraphPool.GetTempArray<RTHandle>(2);
                        momentAtlasRenderTextures[0] = data.momentAtlasTexture1;
                        momentAtlasRenderTextures[1] = data.momentAtlasTexture2;

                        int generateAndBlurMomentsKernel = shadowBlurMomentsCS.FindKernel("ConvertAndBlur");
                        int blurMomentsKernel = shadowBlurMomentsCS.FindKernel("Blur");
                        int copyMomentsKernel = shadowBlurMomentsCS.FindKernel("CopyMoments");

                        RTHandle atlasRenderTexture = data.atlasTexture;

                        natCmd.SetComputeTextureParam(shadowBlurMomentsCS, generateAndBlurMomentsKernel, IllusionShaderProperties._DepthTexture, atlasRenderTexture);
                        natCmd.SetComputeVectorArrayParam(shadowBlurMomentsCS, IllusionShaderProperties._BlurWeightsStorage, AreaLightShadowAtlas.evsmBlurWeights);

                        // We need to store in which of the two moment texture a request will have its last version stored in for a final patch up at the end.
                        var finalAtlasTexture = data.finalAtlasTexture;

                        int requestIdx = 0;
                        for (int i = 0; i < data.shadowRequestCount; i++)
                        {
                            ref var shadowRequest = ref data.shadowRequests[i];
                            var viewport = shadowRequest.dynamicAtlasViewport;

                            int downsampledWidth = Mathf.CeilToInt(viewport.width * 0.5f);
                            int downsampledHeight = Mathf.CeilToInt(viewport.height * 0.5f);

                            Vector2 DstRectOffset = new Vector2(viewport.min.x * 0.5f, viewport.min.y * 0.5f);

                            natCmd.SetComputeTextureParam(shadowBlurMomentsCS, generateAndBlurMomentsKernel, IllusionShaderProperties._OutputTexture, momentAtlasRenderTextures[0]);
                            natCmd.SetComputeVectorParam(shadowBlurMomentsCS, IllusionShaderProperties._SrcRect, new Vector4(viewport.min.x, viewport.min.y, viewport.width, viewport.height));
                            natCmd.SetComputeVectorParam(shadowBlurMomentsCS, IllusionShaderProperties._DstRect, new Vector4(DstRectOffset.x, DstRectOffset.y, 1.0f / atlasRenderTexture.rt.width, 1.0f / atlasRenderTexture.rt.height));
                            natCmd.SetComputeFloatParam(shadowBlurMomentsCS, IllusionShaderProperties._EVSMExponent, shadowRequest.evsmParams.x);

                            int dispatchSizeX = ((int)downsampledWidth + 7) / 8;
                            int dispatchSizeY = ((int)downsampledHeight + 7) / 8;

                            natCmd.DispatchCompute(shadowBlurMomentsCS, generateAndBlurMomentsKernel, dispatchSizeX, dispatchSizeY, 1);

                            int currentAtlasMomentSurface = 0;

                            RTHandle GetMomentRT() { return momentAtlasRenderTextures[currentAtlasMomentSurface]; }
                            RTHandle GetMomentRTCopy() { return momentAtlasRenderTextures[(currentAtlasMomentSurface + 1) & 1]; }

                            natCmd.SetComputeVectorParam(shadowBlurMomentsCS, IllusionShaderProperties._SrcRect, new Vector4(DstRectOffset.x, DstRectOffset.y, downsampledWidth, downsampledHeight));
                            for (int b = 0; b < shadowRequest.evsmParams.w; ++b)
                            {
                                currentAtlasMomentSurface = (currentAtlasMomentSurface + 1) & 1;
                                natCmd.SetComputeTextureParam(shadowBlurMomentsCS, blurMomentsKernel, IllusionShaderProperties._InputTexture, GetMomentRTCopy());
                                natCmd.SetComputeTextureParam(shadowBlurMomentsCS, blurMomentsKernel, IllusionShaderProperties._OutputTexture, GetMomentRT());

                                natCmd.DispatchCompute(shadowBlurMomentsCS, blurMomentsKernel, dispatchSizeX, dispatchSizeY, 1);
                            }

                            finalAtlasTexture[requestIdx++] = currentAtlasMomentSurface;
                        }

                        // We patch up the atlas with the requests that, due to different count of blur passes, remained in the copy
                        for (int i = 0; i < data.shadowRequestCount; ++i)
                        {
                            if (finalAtlasTexture[i] != 0)
                            {
                                ref var shadowRequest = ref data.shadowRequests[i];
                                var viewport = shadowRequest.dynamicAtlasViewport;
                                int downsampledWidth = Mathf.CeilToInt(viewport.width * 0.5f);
                                int downsampledHeight = Mathf.CeilToInt(viewport.height * 0.5f);

                                natCmd.SetComputeVectorParam(shadowBlurMomentsCS, IllusionShaderProperties._SrcRect, new Vector4(viewport.min.x * 0.5f, viewport.min.y * 0.5f, downsampledWidth, downsampledHeight));
                                natCmd.SetComputeTextureParam(shadowBlurMomentsCS, copyMomentsKernel, IllusionShaderProperties._InputTexture, momentAtlasRenderTextures[1]);
                                natCmd.SetComputeTextureParam(shadowBlurMomentsCS, copyMomentsKernel, IllusionShaderProperties._OutputTexture, momentAtlasRenderTextures[0]);

                                int dispatchSizeX = ((int)downsampledWidth + 7) / 8;
                                int dispatchSizeY = ((int)downsampledHeight + 7) / 8;

                                natCmd.DispatchCompute(shadowBlurMomentsCS, copyMomentsKernel, dispatchSizeX, dispatchSizeY, 1);
                            }
                        }
                    });

                return passData.momentAtlasTexture1;
            }
        }

        private void FetchCookies(RenderGraph renderGraph, TextureHandle cookieAtlas)
        {
            using (var builder = renderGraph.AddUnsafePass<CookiesPassData>("Area Light Cookies", out var passData, _cookiesSampler))
            {
                passData.cookieAtlas = cookieAtlas;
                passData.manager = _manager;
                passData.cookieManager = _cookieManager;
                builder.UseTexture(cookieAtlas, AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (CookiesPassData data, UnsafeGraphContext context) =>
                {
                    var natCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.manager.FetchCookies(natCmd, data.cookieManager);
                });
            }
        }

        // Unsafe pass: constant / structured buffer uploads are not allowed inside a native render pass.
        private void BindShadowGlobalResources(RenderGraph renderGraph, TextureHandle areaShadowResult, TextureHandle cookieAtlas)
        {
            using (var builder = renderGraph.AddUnsafePass<GlobalsPassData>("Area Light Globals", out var passData, _globalsSampler))
            {
                passData.shadowAtlas = areaShadowResult;
                passData.ltcData = renderGraph.ImportTexture(LTCAreaLight.instance.ltcDataHandle);
                passData.cookieAtlas = cookieAtlas;
                passData.manager = _manager;
                builder.UseTexture(areaShadowResult);
                builder.UseTexture(passData.ltcData);
                builder.UseTexture(cookieAtlas);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (GlobalsPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var natCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
                    data.manager.UploadBuffers(natCmd);
                    cmd.SetGlobalTexture(IllusionShaderProperties._ShadowmapAreaAtlas, data.shadowAtlas);
                    cmd.SetGlobalTexture(IllusionShaderProperties._CachedAreaLightShadowmapAtlas, data.shadowAtlas);
                    cmd.SetGlobalTexture(IllusionShaderProperties._LtcData, data.ltcData);
                    cmd.SetGlobalTexture(IllusionShaderProperties._CookieAtlas, data.cookieAtlas);
                    cmd.SetGlobalBuffer(IllusionShaderProperties._AreaLightDatas, data.manager.lightDataBuffer);
                    cmd.SetGlobalBuffer(IllusionShaderProperties._HDShadowDatas, data.manager.shadowDataBuffer);
                    ConstantBuffer.PushGlobal(natCmd, data.manager.shaderVariables, IllusionShaderProperties.ShaderVariablesAreaLights);
                });
            }
        }
    }
}
