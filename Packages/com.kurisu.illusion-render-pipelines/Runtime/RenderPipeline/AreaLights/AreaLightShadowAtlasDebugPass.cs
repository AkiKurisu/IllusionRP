#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.AreaLights
{
    /// <summary>
    /// Draws the area light shadow atlas as a screen overlay.
    /// </summary>
    // Reference: HDRenderPipeline.Debug.cs RenderShadowsDebugOverlay (ShadowMapDebugMode.VisualizeAreaLightAtlas)
    public sealed class AreaLightShadowAtlasDebugPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _material = new(IllusionShaders.DebugDisplayHDShadowMap);

        private const string PassName = "Area Light Shadow Atlas Debug";

        // The overlay edge is a third of the screen height.
        private const float OverlayRatio = 0.33f;

        public AreaLightShadowAtlasDebugPass()
        {
            renderPassEvent = IllusionRenderPassEvent.FullScreenDebugPass;
            profilingSampler = new ProfilingSampler(PassName);
        }

        private class PassData
        {
            public Material Material;
            public TextureHandle AtlasTexture;
            public TextureHandle ColorTexture;
            public AreaLightShadowAtlas Atlas;
            public Rect Rect;
            public float MinValue;
            public float MaxValue;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var areaLightFrameData = frameData.Get<AreaLightFrameData>();
            if (areaLightFrameData == null || areaLightFrameData.Atlas == null
                || areaLightFrameData.ShadowRequestCount == 0 || !areaLightFrameData.ShadowAtlas.IsValid())
                return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var config = IllusionRuntimeRenderingConfig.Get();

            // Unsafe pass: the atlas is bound through a property block, not an attachment.
            using (var builder = renderGraph.AddUnsafePass<PassData>(PassName, out var passData, profilingSampler))
            {
                passData.Material = _material.Value;
                passData.AtlasTexture = areaLightFrameData.ShadowAtlas;
                passData.Atlas = areaLightFrameData.Atlas;
                passData.ColorTexture = resourceData.activeColorTexture;
                int overlaySize = (int)(cameraData.cameraTargetDescriptor.height * OverlayRatio);
                passData.Rect = new Rect(0, 0, overlaySize, overlaySize);
                passData.MinValue = config.AreaLightShadowAtlasDebugMinValue;
                passData.MaxValue = config.AreaLightShadowAtlasDebugMaxValue;

                builder.UseTexture(passData.AtlasTexture);
                builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var natCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    natCmd.SetRenderTarget(data.ColorTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._AtlasTexture, data.AtlasTexture);
                    var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
                    data.Atlas.DisplayAtlas(natCmd, data.Material,
                        new Rect(0, 0, data.Atlas.width, data.Atlas.height),
                        data.Rect.x, data.Rect.y, data.Rect.width, data.Rect.height,
                        data.MinValue, data.MaxValue, mpb);
                });
            }
        }

        public void Dispose()
        {
            _material.DestroyCache();
        }
    }
}
#endif
