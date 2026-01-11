using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Render transparent objects overdraw.
    /// </summary>
    public class TransparentOverdrawPass : ScriptableRenderPass
    {
        public static TransparentOverdrawPass Create(TransparentOverdrawStencilStateData stencilData)
        {
            var defaultStencilState = StencilState.defaultValue;
            defaultStencilState.enabled = stencilData.overrideStencilState;
            defaultStencilState.readMask = stencilData.stencilReadMask;
            defaultStencilState.SetCompareFunction(stencilData.stencilCompareFunction);
            defaultStencilState.SetPassOperation(stencilData.passOperation);
            defaultStencilState.SetFailOperation(stencilData.failOperation);
            defaultStencilState.SetZFailOperation(stencilData.zFailOperation);
            var pass = new TransparentOverdrawPass
            (
                IllusionRenderPassEvent.TransparentOverdrawPass,
                RenderQueueRange.transparent,
                -1,
                defaultStencilState,
                stencilData.stencilReference
            );
            return pass;
        }

        private readonly FilteringSettings _filteringSettings;

        private readonly RenderStateBlock _renderStateBlock;

        private readonly List<ShaderTagId> _shaderTagIdList = new();

        private static readonly int DrawObjectPassDataPropID = Shader.PropertyToID("_DrawObjectPassData");

        private TransparentOverdrawPass(ShaderTagId[] shaderTagIds, RenderPassEvent evt, RenderQueueRange renderQueueRange, 
            LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            profilingSampler = new ProfilingSampler("Transparent Overdraw");
            foreach (var sid in shaderTagIds)
            {
                _shaderTagIdList.Add(sid);
            }
            renderPassEvent = evt;
            _filteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(false, CompareFunction.LessEqual)
            };

            if (stencilState.enabled)
            {
                _renderStateBlock.stencilReference = stencilReference;
                _renderStateBlock.mask |= RenderStateMask.Stencil;
                _renderStateBlock.stencilState = stencilState;
            }
        }

        private TransparentOverdrawPass(RenderPassEvent evt,
            RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
            : this(new ShaderTagId[] { new("SRPDefaultUnlit"), new("UniversalForward"), new("UniversalForwardOnly") },
                evt, renderQueueRange, layerMask, stencilState, stencilReference)
        { }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            TextureHandle colorTarget = resource.activeColorTexture;
            TextureHandle depthTarget = resource.cameraDepth; // Restore to offscreen depth

            Render(renderGraph, colorTarget, depthTarget, frameData);
        }

        private static void InitRendererLists(ContextContainer frameData, ref PassData passData, ScriptableRenderContext context, RenderGraph renderGraph, bool useRenderGraph)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            Camera camera = cameraData.camera;
            var sortFlags = SortingCriteria.CommonTransparent;
            var filterSettings = passData.FilteringSettings;

#if UNITY_EDITOR
            // When rendering the preview camera, we want the layer mask to be forced to Everything
            if (cameraData.isPreviewCamera)
            {
                filterSettings.layerMask = -1;
            }
#endif

            DrawingSettings drawSettings = UniversalRenderingUtility.CreateDrawingSettings(passData.ShaderTagIdList, frameData, sortFlags);
            
            var activeDebugHandler = GetActiveDebugHandler(cameraData);
            if (useRenderGraph)
            {
                if (activeDebugHandler != null)
                {
                    passData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(renderGraph, ref renderingData.cullResults, ref drawSettings, ref filterSettings, ref passData.RenderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettings, filterSettings, passData.RenderStateBlock, ref passData.RendererListHdl);
                    RenderingUtils.CreateRendererListObjectsWithError(renderGraph, ref renderingData.cullResults, camera, filterSettings, sortFlags, ref passData.ObjectsWithErrorRendererListHdl);
                }
            }
            else
            {
                if (activeDebugHandler != null)
                {
                    passData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(context, ref renderingData.cullResults, ref drawSettings, ref filterSettings, ref passData.RenderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(context, ref renderingData.cullResults, drawSettings, filterSettings, passData.RenderStateBlock, ref passData.RendererList);
                    RenderingUtils.CreateRendererListObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, sortFlags, ref passData.ObjectsWithErrorRendererList);
                }
            }
        }

        private static void ExecutePass(RasterCommandBuffer cmd, PassData data, RendererList rendererList, RendererList objectsWithErrorRendererList, UniversalCameraData cameraData, bool yFlip)
        {
            // Global render pass data containing various settings.
            // x,y,z are currently unused
            // w is used for knowing whether the object is opaque(1) or alpha blended(0)
            Vector4 drawObjectPassData = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            cmd.SetGlobalVector(DrawObjectPassDataPropID, drawObjectPassData);

            // scaleBias.x = flipSign
            // scaleBias.y = scale
            // scaleBias.z = bias
            // scaleBias.w = unused
            float flipSign = yFlip ? -1.0f : 1.0f;
            Vector4 scaleBias = flipSign < 0.0f
                ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
            cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);

            // Set a value that can be used by shaders to identify when AlphaToMask functionality may be active
            // The material shader alpha clipping logic requires this value in order to function correctly in all cases.
            float alphaToMaskAvailable = 0.0f;
            cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, alphaToMaskAvailable);

            var activeDebugHandler = GetActiveDebugHandler(cameraData);
            if (activeDebugHandler != null)
            {
                data.DebugRendererLists.DrawWithRendererList(cmd);
            }
            else
            {
                cmd.DrawRendererList(rendererList);
                // Render objects that did not match any shader pass with error shader
                RenderingUtils.DrawRendererListObjectsWithError(cmd, ref objectsWithErrorRendererList);
            }
        }

        private void Render(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Transparent Overdraw", out var passData, profilingSampler))
            {
                passData.RenderStateBlock = _renderStateBlock;
                passData.FilteringSettings = _filteringSettings;
                passData.ShaderTagIdList = _shaderTagIdList;

                if (colorTarget.IsValid())
                {
                    builder.SetRenderAttachment(colorTarget, 0);
                    passData.ColorHandle = colorTarget;
                }

                if (depthTarget.IsValid())
                {
                    builder.SetRenderAttachmentDepth(depthTarget);
                    passData.DepthHandle = depthTarget;
                }

                InitRendererLists(frameData, ref passData, default, renderGraph, true);
                var cameraData = frameData.Get<UniversalCameraData>();
                var activeDebugHandler = GetActiveDebugHandler(cameraData);
                if (activeDebugHandler != null)
                {
                    passData.DebugRendererLists.PrepareRendererListForRasterPass(builder);
                }
                else
                {
                    builder.UseRendererList(passData.RendererListHdl);
                    builder.UseRendererList(passData.ObjectsWithErrorRendererListHdl);
                }

                passData.CameraData = cameraData;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    bool yFlip = data.CameraData.IsRenderTargetProjectionMatrixFlipped(data.ColorHandle, data.DepthHandle);

                    ExecutePass(context.cmd, data, data.RendererListHdl, data.ObjectsWithErrorRendererListHdl, data.CameraData, yFlip);
                });
            }
        }
        
        private class PassData
        {
            internal RenderStateBlock RenderStateBlock;

            internal FilteringSettings FilteringSettings;

            internal List<ShaderTagId> ShaderTagIdList;

#if UNITY_2023_1_OR_NEWER
            internal TextureHandle ColorHandle;
            
            internal TextureHandle DepthHandle;
            
            internal UniversalCameraData CameraData;
            
            internal RendererListHandle RendererListHdl;
            
            internal RendererListHandle ObjectsWithErrorRendererListHdl;
            
            internal DebugRendererLists DebugRendererLists;

            // Required for code sharing purpose between RG and non-RG.
            internal RendererList RendererList;
            
            internal RendererList ObjectsWithErrorRendererList;
#endif
        }
    }
}