using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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

        private readonly ProfilingSampler _profilingSampler;

        private PassData _passData;

        private static readonly int DrawObjectPassDataPropID = Shader.PropertyToID("_DrawObjectPassData");

        private TransparentOverdrawPass(ShaderTagId[] shaderTagIds, RenderPassEvent evt, RenderQueueRange renderQueueRange, 
            LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            profilingSampler = new ProfilingSampler(nameof(TransparentOverdrawPass));
            _passData = new PassData();
            _profilingSampler = new ProfilingSampler("Transparent Overdraw");
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

#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            TextureHandle colorTarget = renderer.activeColorTexture;
            TextureHandle depthTarget = renderer.activeDepthTexture;

            Render(renderGraph, colorTarget, depthTarget, ref renderingData);
        }
#endif
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _passData.RenderStateBlock = _renderStateBlock;
            _passData.FilteringSettings = _filteringSettings;
            _passData.ShaderTagIdList = _shaderTagIdList;
            _passData.ProfilingSampler = _profilingSampler;

#if !UNITY_2023_1_OR_NEWER
            ExecutePass(context, _passData, ref renderingData, renderingData.cameraData.IsCameraProjectionMatrixFlipped());
#else
            InitRendererLists(ref renderingData, ref _passData, context, null, false);

            using (new ProfilingScope(renderingData.commandBuffer, _profilingSampler))
            {
                ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(renderingData.commandBuffer), _passData, ref renderingData, renderingData.cameraData.IsCameraProjectionMatrixFlipped());
            }
#endif
        }

        private void InitRendererLists(ref RenderingData renderingData, ref PassData passData, ScriptableRenderContext context, RenderGraph renderGraph, bool useRenderGraph)
        {
            Camera camera = renderingData.cameraData.camera;
            var sortFlags = SortingCriteria.CommonTransparent;
            var filterSettings = passData.FilteringSettings;

#if UNITY_EDITOR
            // When rendering the preview camera, we want the layer mask to be forced to Everything
            if (renderingData.cameraData.isPreviewCamera)
            {
                filterSettings.layerMask = -1;
            }
#endif

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(passData.ShaderTagIdList, ref renderingData, sortFlags);

#if UNITY_2023_1_OR_NEWER
            var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
            if (useRenderGraph)
            {
                if (activeDebugHandler != null)
                {
                    passData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(renderGraph, ref renderingData, ref drawSettings, ref filterSettings, ref passData.RenderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, renderingData, drawSettings, filterSettings, passData.RenderStateBlock, ref passData.RendererListHdl);
                    RenderingUtils.CreateRendererListObjectsWithError(renderGraph, ref renderingData.cullResults, camera, filterSettings, sortFlags, ref passData.ObjectsWithErrorRendererListHdl);
                }
            }
            else
            {
                if (activeDebugHandler != null)
                {
                    passData.DebugRendererLists = activeDebugHandler.CreateRendererListsWithDebugRenderState(context, ref renderingData, ref drawSettings, ref filterSettings, ref passData.RenderStateBlock);
                }
                else
                {
                    RenderingUtils.CreateRendererListWithRenderStateBlock(context, renderingData, drawSettings, filterSettings, passData.RenderStateBlock, ref passData.RendererList);
                    RenderingUtils.CreateRendererListObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, sortFlags, ref passData.ObjectsWithErrorRendererList);
                }
            }
#endif
        }

#if !UNITY_2023_1_OR_NEWER
        private static void ExecutePass(ScriptableRenderContext context, PassData data, ref RenderingData renderingData, bool yFlip)
        {
            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, data.ProfilingSampler))
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
                Vector4 scaleBias = (flipSign < 0.0f)
                    ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                    : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
                cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);

                // Set a value that can be used by shaders to identify when AlphaToMask functionality may be active
                // The material shader alpha clipping logic requires this value in order to function correctly in all cases.
                float alphaToMaskAvailable = 0.0f;
                cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, alphaToMaskAvailable);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                Camera camera = renderingData.cameraData.camera;
                var sortFlags = SortingCriteria.CommonTransparent;

                var filterSettings = data.FilteringSettings;

#if UNITY_EDITOR
                // When rendering the preview camera, we want the layer mask to be forced to Everything
                if (renderingData.cameraData.isPreviewCamera)
                {
                    filterSettings.layerMask = -1;
                }
#endif

                DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(data.ShaderTagIdList, ref renderingData, sortFlags);

                var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
                if (activeDebugHandler != null)
                {
                    activeDebugHandler.DrawWithDebugRenderState(context, cmd, ref renderingData, ref drawSettings, ref filterSettings, ref data.RenderStateBlock,
                        (ScriptableRenderContext ctx, ref RenderingData rd, ref DrawingSettings ds, ref FilteringSettings fs, ref RenderStateBlock rsb) =>
                        {
                            ctx.DrawRenderers(rd.cullResults, ref ds, ref fs, ref rsb);
                        });
                }
                else
                {
                    context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings, ref data.RenderStateBlock);

                    // Render objects that did not match any shader pass with error shader
                    RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortingCriteria.None);
                }

                // Clean up
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.WriteRenderingLayers, false);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
#else
        private static void ExecutePass(RasterCommandBuffer cmd, PassData data, ref RenderingData renderingData, bool yFlip)
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
            Vector4 scaleBias = (flipSign < 0.0f)
                ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
            cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);

            // Set a value that can be used by shaders to identify when AlphaToMask functionality may be active
            // The material shader alpha clipping logic requires this value in order to function correctly in all cases.
            float alphaToMaskAvailable = 0.0f;
            cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, alphaToMaskAvailable);

            var activeDebugHandler = GetActiveDebugHandler(ref renderingData);
            if (activeDebugHandler != null)
            {
                data.DebugRendererLists.DrawWithRendererList(cmd);
            }
            else
            {
                cmd.DrawRendererList(data.RendererList);
                // Render objects that did not match any shader pass with error shader
                RenderingUtils.DrawRendererListObjectsWithError(cmd, ref data.ObjectsWithErrorRendererList);
            }
        }
#endif

#if UNITY_2023_1_OR_NEWER
        internal void Render(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Transparent Overdraw", out var passData, _profilingSampler))
            {
                passData.RenderStateBlock = _renderStateBlock;
                passData.FilteringSettings = _filteringSettings;
                passData.ShaderTagIdList = _shaderTagIdList;
                passData.ProfilingSampler = _profilingSampler;
                passData.RenderingData = renderingData;

                if (colorTarget.IsValid())
                    passData.ColorHandle = builder.UseTextureFragment(colorTarget, 0);

                if (depthTarget.IsValid())
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
                    builder.UseRendererList(passData.ObjectsWithErrorRendererListHdl);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    ref var renderingData = ref data.RenderingData;
                    bool yFlip = renderingData.cameraData.IsRenderTargetProjectionMatrixFlipped(data.ColorHandle, data.DepthHandle);

                    ExecutePass(context.cmd, data, ref renderingData, yFlip);
                });
            }
        }
#endif

        private class PassData
        {
            internal RenderStateBlock RenderStateBlock;

            internal FilteringSettings FilteringSettings;

            internal List<ShaderTagId> ShaderTagIdList;

            internal ProfilingSampler ProfilingSampler;

#if UNITY_2023_1_OR_NEWER
            internal TextureHandle ColorHandle;
            
            internal TextureHandle DepthHandle;
            
            internal RenderingData RenderingData;
            
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