using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    /// <summary>
    /// Render transparent objects with weighted blended order independent transparency.
    /// </summary>
    public class WeightedBlendedOITPass : ScriptableRenderPass, IDisposable
    {
        private readonly ProfilingSampler _accumulateSampler = new("Accumulate");

        private readonly ProfilingSampler _compositeSampler = new("Composite");

        private FilteringSettings _filteringSettings;

        private RenderStateBlock _renderStateBlock;

        private readonly LazyMaterial _compositeMat = new(IllusionShaders.WeightedBlendedOITComposite);

#if !UNITY_2023_1_OR_NEWER
        private RTHandle _accumulate;

        private RTHandle _revealage;

        private readonly RenderTargetIdentifier[] _oitBuffers = new RenderTargetIdentifier[2];

        private bool _nativeRenderPass;
#endif

        private static readonly ShaderTagId OitTagId = new(IllusionShaderPasses.OIT);

        private readonly IllusionRendererData _rendererData;

        public WeightedBlendedOITPass(LayerMask layerMask, IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.OrderIndependentTransparentPass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all, layerMask);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(false, CompareFunction.LessEqual)
            };
            profilingSampler = new ProfilingSampler("Order Independent Transparency");
        }

#if !UNITY_2023_1_OR_NEWER
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _nativeRenderPass = _rendererData.NativeRenderPass && renderingData.cameraData.isRenderPassSupportedCamera;
            if (_nativeRenderPass) return;
            
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;

            // Accumulate buffer
            desc.colorFormat = RenderTextureFormat.ARGBFloat;
            RenderingUtils.ReAllocateIfNeeded(ref _accumulate, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_AccumTex");

            // Revealage buffer
            desc.colorFormat = RenderTextureFormat.RFloat;
            RenderingUtils.ReAllocateIfNeeded(ref _revealage, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_RevealageTex");

            _oitBuffers[0] = _accumulate;
            _oitBuffers[1] = _revealage;
        }
#endif

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Color);
        }
        
#if !UNITY_2023_1_OR_NEWER
        private void DoAccumulate(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            cmd.SetRenderTarget(_oitBuffers, renderingData.cameraData.renderer.cameraDepthTargetHandle);
            context.ExecuteCommandBuffer(cmd);
            
            var drawSettings = CreateDrawingSettings(OitTagId, ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
            var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
            if (activeDebugHandler != null)
            {
                activeDebugHandler.DrawWithDebugRenderState(context, cmd, ref renderingData, ref drawSettings, ref _filteringSettings, ref _renderStateBlock,
                    (ScriptableRenderContext ctx, ref RenderingData rd, ref DrawingSettings ds, ref FilteringSettings fs, ref RenderStateBlock rsb) =>
                    {
                        ctx.DrawRenderers(rd.cullResults, ref ds, ref fs, ref rsb);
                    });
            }
            else
            {
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref _filteringSettings, ref _renderStateBlock);

                // Render objects that did not match any shader pass with error shader
                var camera = renderingData.cameraData.camera;
                RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, _filteringSettings, SortingCriteria.None);
            }
        }

        private void ClearBuffers(CommandBuffer cmd, ScriptableRenderContext context)
        {
            cmd.SetRenderTarget(_accumulate);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetRenderTarget(_revealage);
            cmd.ClearRenderTarget(false, true, Color.white);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private void DoComposite(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var colorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (!colorHandle.IsValid())
            {
                return;
            }
            cmd.SetRenderTarget(colorHandle);
            _compositeMat.Value.DisableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
            _compositeMat.Value.SetTexture(Properties._AccumTex, _accumulate);
            _compositeMat.Value.SetTexture(Properties._RevealageTex, _revealage);
            Blitter.BlitCameraTexture(cmd,colorHandle, colorHandle, _compositeMat.Value, 0);
        }
#endif
        
        private void DoNativeRenderPass(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camDesc = renderingData.cameraData.cameraTargetDescriptor;
            var colorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var depthHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            int width = camDesc.width, height = camDesc.height, samples = Mathf.Max(1, camDesc.msaaSamples);

            var depthDesc = new AttachmentDescriptor(SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil));
            depthDesc.ConfigureTarget(depthHandle.nameID, true, true);
            
            var accumDesc = new AttachmentDescriptor(RenderTextureFormat.ARGBFloat);
            accumDesc.ConfigureClear(Color.clear, 0);
            accumDesc.loadStoreTarget = BuiltinRenderTextureType.None;
            
            var revealDesc = new AttachmentDescriptor(RenderTextureFormat.RFloat);
            revealDesc.ConfigureClear(Color.white, 0);
            revealDesc.loadStoreTarget = BuiltinRenderTextureType.None;
            
            var colorDesc = new AttachmentDescriptor(colorHandle.rt.descriptor.graphicsFormat);
            colorDesc.ConfigureTarget(colorHandle.nameID, true, true);

            const int kDepth = 0;
            const int kAccum = 1;
            const int kReveal = 2;
            const int kColor = 3;
            var attachments = new NativeArray<AttachmentDescriptor>(4, Allocator.Temp);
            attachments[kDepth] = depthDesc;     // 0 -> Depth Attachment
            attachments[kAccum] = accumDesc;     // 1 -> Accumulate
            attachments[kReveal] = revealDesc;    // 2 -> Revealage
            attachments[kColor] = colorDesc;     // 3 -> Color Attachment

            using (context.BeginScopedRenderPass(width, height, samples, attachments, depthAttachmentIndex: kDepth))
            {
                attachments.Dispose();

                var compositeBuffer = new NativeArray<int>(2, Allocator.Temp);
                compositeBuffer[0] = kAccum;
                compositeBuffer[1] = kReveal;
                using (context.BeginScopedSubPass(compositeBuffer, isDepthStencilReadOnly: false))
                {
                    compositeBuffer.Dispose();
                    CommandBuffer cmd = CommandBufferPool.Get();
                    using (new ProfilingScope(cmd, _accumulateSampler))
                    {
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        var drawSettings = CreateDrawingSettings(OitTagId, ref renderingData,
                            renderingData.cameraData.defaultOpaqueSortFlags);
                        var rendererList = default(RendererList);
                        RenderingUtils.CreateRendererListWithRenderStateBlock(context, renderingData, drawSettings, _filteringSettings, _renderStateBlock, ref rendererList);
                        cmd.DrawRendererList(rendererList);
                    }

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                    // Need to execute it immediately to avoid sync issues between context and cmd buffer
                    context.ExecuteCommandBuffer(renderingData.commandBuffer);
                    renderingData.commandBuffer.Clear();
                }

                var compositeTarget = new NativeArray<int>(1, Allocator.Temp);
                compositeTarget[0] = kColor;
                var compositeInput = new NativeArray<int>(2, Allocator.Temp);
                compositeInput[0] = kAccum;
                compositeInput[1] = kReveal;
                using (context.BeginScopedSubPass(compositeTarget, compositeInput, isDepthStencilReadOnly: true))
                {
                    compositeTarget.Dispose();
                    compositeInput.Dispose();
                    CommandBuffer cmd = CommandBufferPool.Get();
                    using (new ProfilingScope(cmd, _compositeSampler))
                    {
                        Vector2 viewportScale = colorHandle.useScaling 
                            ? new Vector2(colorHandle.rtHandleProperties.rtHandleScale.x, colorHandle.rtHandleProperties.rtHandleScale.y) 
                            : Vector2.one;
                        _compositeMat.Value.EnableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
                        Blitter.BlitTexture(cmd, viewportScale, _compositeMat.Value, 0);
                    }

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                    // Need to execute it immediately to avoid sync issues between context and cmd buffer
                    context.ExecuteCommandBuffer(renderingData.commandBuffer);
                    renderingData.commandBuffer.Clear();
                }
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif
            if (!_compositeMat.Value) return;

#if !UNITY_2023_1_OR_NEWER
            if (_nativeRenderPass)
#endif
            {
                using (new ProfilingScope((CommandBuffer)null, profilingSampler))
                {
                    DoNativeRenderPass(context, ref renderingData);
                }
            }
#if !UNITY_2023_1_OR_NEWER
            else
            {
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    ClearBuffers(cmd, context);
                    
                    using (new ProfilingScope(cmd, _accumulateSampler))
                    {
                        DoAccumulate(cmd, context, ref renderingData);
                    }
                    
                    using (new ProfilingScope(cmd, _compositeSampler))
                    {
                        DoComposite(cmd, ref renderingData);
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
#endif
        }
        
#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif
            if (!_compositeMat.Value) return;

            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle colorTarget = renderer.activeColorTexture;
            TextureHandle depthTarget = renderer.activeDepthTexture;

            Render(renderGraph, colorTarget, depthTarget, ref renderingData);
        }

        private void InitRendererLists(ref RenderingData renderingData, ref OITPassData oitPassData, ScriptableRenderContext context, RenderGraph renderGraph, bool useRenderGraph)
        {
            var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
            var filterSettings = _filteringSettings;

#if UNITY_EDITOR
            // When rendering the preview camera, we want the layer mask to be forced to Everything
            if (renderingData.cameraData.isPreviewCamera)
            {
                filterSettings.layerMask = -1;
            }
#endif

            DrawingSettings drawSettings = CreateDrawingSettings(OitTagId, ref renderingData, sortFlags);

            var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
            if (useRenderGraph)
            {
                if (activeDebugHandler != null)
                {
                    oitPassData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(renderGraph, ref renderingData, ref drawSettings, ref filterSettings, ref _renderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, renderingData, drawSettings, filterSettings, _renderStateBlock, ref oitPassData.RendererListHdl);
                }
            }
            else
            {
                if (activeDebugHandler != null)
                {
                    oitPassData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(context, ref renderingData, ref drawSettings, ref filterSettings, ref _renderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(context, renderingData, drawSettings, filterSettings, _renderStateBlock, ref oitPassData.RendererList);
                }
            }
        }

        private void Render(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, ref RenderingData renderingData)
        {
            // Create OIT buffers
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;

            // Accumulate buffer (ARGBFloat)
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            var accumulateHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_AccumTex", true, Color.clear, FilterMode.Bilinear);

            // Revealage buffer (RFloat)
            desc.graphicsFormat = GraphicsFormat.R16_SFloat;
            var revealageHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RevealageTex", true, Color.white, FilterMode.Bilinear);

            // Pass 1: Accumulation - render transparent objects to accumulate and revealage buffers
            using (var builder = renderGraph.AddRasterRenderPass<OITPassData>("OIT Accumulate", out var passData, _accumulateSampler))
            {
                passData.CompositeMaterial = _compositeMat.Value;
                passData.RenderingData = renderingData;
                passData.AccumulateHandle = builder.UseTextureFragment(accumulateHandle, 0);
                passData.RevealageHandle = builder.UseTextureFragment(revealageHandle, 1);
                passData.ColorHandle = builder.UseTexture(colorTarget);
                passData.DepthHandle = builder.UseTextureFragmentDepth(depthTarget);

                InitRendererLists(ref renderingData, ref passData, default, renderGraph, true);

                var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
                if (activeDebugHandler != null)
                {
                    passData.DebugRendererLists.PrepareRendererListForRasterPass(builder);
                }
                else
                {
                    builder.UseRendererList(passData.RendererListHdl);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((OITPassData data, RasterGraphContext context) =>
                {
                    var handler = GetActiveDebugHandler(ref data.RenderingData);
                    if (handler != null)
                    {
                        data.DebugRendererLists.DrawWithRendererList(context.cmd);
                    }
                    else
                    {
                        context.cmd.DrawRendererList(data.RendererListHdl);
                    }
                });
            }

            // Pass 2: Composite - blend accumulated results into color target
            using (var builder = renderGraph.AddRasterRenderPass<OITPassData>("OIT Composite", out var passData, _compositeSampler))
            {
                passData.CompositeMaterial = _compositeMat.Value;
                passData.RenderingData = renderingData;
                passData.CompositeMaterial = _compositeMat.Value;
                passData.AccumulateHandle = builder.UseTextureFragmentInput(accumulateHandle, 0);
                passData.RevealageHandle = builder.UseTextureFragmentInput(revealageHandle, 1);
                passData.ColorHandle = builder.UseTextureFragment(colorTarget, 0);
                passData.DepthHandle = builder.UseTextureFragmentDepth(depthTarget, IBaseRenderGraphBuilder.AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
            
                builder.SetRenderFunc((OITPassData data, RasterGraphContext context) =>
                {
                    data.CompositeMaterial.EnableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
                    Blitter.BlitTexture(context.cmd, data.ColorHandle, new Vector4(1, 1, 0, 0), data.CompositeMaterial, 0);
                });
            }
        }
#endif

        public void Dispose()
        {
            _compositeMat.DestroyCache();
#if !UNITY_2023_1_OR_NEWER
            _accumulate?.Release();
            _revealage?.Release();
#endif
        }

#if UNITY_2023_1_OR_NEWER
        private class OITPassData
        {
            internal TextureHandle AccumulateHandle;
            internal TextureHandle RevealageHandle;
            internal TextureHandle DepthHandle;
            internal TextureHandle ColorHandle;
            internal RenderingData RenderingData;
            internal Material CompositeMaterial;
            internal RendererListHandle RendererListHdl;
            internal DebugRendererLists DebugRendererLists;
            internal RendererList RendererList;
        }
#endif
        
        private static class Properties
        {
            public static readonly int _AccumTex = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _RevealageTex = MemberNameHelpers.ShaderPropertyID();
        }
    }
}