using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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

        private readonly ProfilingSampler _profilingSampler;

        private readonly PassData _passData;

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
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _passData.RenderStateBlock = _renderStateBlock;
            _passData.FilteringSettings = _filteringSettings;
            _passData.ShaderTagIdList = _shaderTagIdList;
            _passData.ProfilingSampler = _profilingSampler;

            ExecutePass(context, _passData, ref renderingData, renderingData.cameraData.IsCameraProjectionMatrixFlipped());
        }

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

        private class PassData
        {
            internal RenderStateBlock RenderStateBlock;

            internal FilteringSettings FilteringSettings;

            internal List<ShaderTagId> ShaderTagIdList;

            internal ProfilingSampler ProfilingSampler;
        }
    }
}