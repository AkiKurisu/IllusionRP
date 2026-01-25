using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering
{
    /// <summary>
    /// Render transparent objects with weighted blended order independent transparency.
    /// </summary>
    public class WeightedBlendedOITPass : ScriptableRenderPass, IDisposable
    {
        private readonly ProfilingSampler _accumulateSampler = new("Accumulate");

        private readonly ProfilingSampler _compositeSampler = new("Composite");

        private readonly FilteringSettings _filteringSettings;

        private RenderStateBlock _renderStateBlock;

        private readonly LazyMaterial _compositeMat = new(IllusionShaders.WeightedBlendedOITComposite);

        private static readonly ShaderTagId OitTagId = new(IllusionShaderPasses.OIT);

        public WeightedBlendedOITPass(LayerMask layerMask)
        {
            renderPassEvent = IllusionRenderPassEvent.OrderIndependentTransparentPass;
            _filteringSettings = new FilteringSettings(RenderQueueRange.all, layerMask);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(false, CompareFunction.LessEqual)
            };
            profilingSampler = new ProfilingSampler("Order Independent Transparency");
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
#if UNITY_EDITOR
            if (cameraData.cameraType == CameraType.Preview)
                return;
#endif
            if (!_compositeMat.Value) return;
            
            TextureHandle colorTarget = resource.activeColorTexture;
            TextureHandle depthTarget = resource.activeDepthTexture;

            Render(renderGraph, colorTarget, depthTarget, frameData);
        }

        private void InitRendererLists(ContextContainer frameData, ref OITPassData oitPassData, ScriptableRenderContext context, RenderGraph renderGraph, bool useRenderGraph)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var sortFlags = cameraData.defaultOpaqueSortFlags;
            var filterSettings = _filteringSettings;

#if UNITY_EDITOR
            // When rendering the preview camera, we want the layer mask to be forced to Everything
            if (cameraData.isPreviewCamera)
            {
                filterSettings.layerMask = -1;
            }
#endif

            DrawingSettings drawSettings = UniversalRenderingUtility.CreateDrawingSettings(OitTagId, frameData, sortFlags);

            var activeDebugHandler = GetActiveDebugHandler(cameraData);
            if (useRenderGraph)
            {
                if (activeDebugHandler != null)
                {
                    oitPassData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(renderGraph, ref renderingData.cullResults, ref drawSettings, ref filterSettings, ref _renderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettings, filterSettings, _renderStateBlock, ref oitPassData.RendererListHdl);
                }
            }
            else
            {
                if (activeDebugHandler != null)
                {
                    oitPassData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(context, ref renderingData.cullResults, ref drawSettings, ref filterSettings, ref _renderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(context, ref renderingData.cullResults, drawSettings, filterSettings, _renderStateBlock, ref oitPassData.RendererList);
                }
            }
        }

        private void Render(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            // Create OIT buffers
            var desc = cameraData.cameraTargetDescriptor;
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
                builder.SetRenderAttachment(accumulateHandle, 0);
                builder.SetRenderAttachment(revealageHandle, 1);
                builder.SetRenderAttachmentDepth(depthTarget);

                passData.CameraData = cameraData;
                InitRendererLists(frameData, ref passData, default, renderGraph, true);

                var activeDebugHandler = GetActiveDebugHandler(cameraData);
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

                builder.SetRenderFunc(static (OITPassData data, RasterGraphContext context) =>
                {
                    var handler = GetActiveDebugHandler(data.CameraData);
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
                passData.CameraData = cameraData;
                passData.CompositeMaterial = _compositeMat.Value;
                builder.SetInputAttachment(accumulateHandle, 0);
                builder.SetInputAttachment(revealageHandle, 1);
                builder.SetRenderAttachment(colorTarget, 0);
                passData.ColorHandle = colorTarget;
                builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
            
                builder.SetRenderFunc((OITPassData data, RasterGraphContext context) =>
                {
                    data.CompositeMaterial.EnableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
                    Blitter.BlitTexture(context.cmd, data.ColorHandle, new Vector4(1, 1, 0, 0), data.CompositeMaterial, 0);
                });
            }
        }

        public void Dispose()
        {
            _compositeMat.DestroyCache();
        }

        private class OITPassData
        {
            internal TextureHandle ColorHandle;
            internal Material CompositeMaterial;
            internal UniversalCameraData CameraData;
            internal RendererListHandle RendererListHdl;
            internal DebugRendererLists DebugRendererLists;
            internal RendererList RendererList;
        }
    }
}