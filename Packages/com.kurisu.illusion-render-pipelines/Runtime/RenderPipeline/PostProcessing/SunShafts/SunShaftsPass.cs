using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    public sealed class SunShaftsPass : ScriptableRenderPass, IDisposable
    {
        private const string MaskRTName = "_SunShaftsMask";

        private const string BlurRTName = "_SunShaftsBlur";

        // Fixed reference height, so shaft length is deliberately resolution dependent.
        private const float BlurStepReferenceHeight = 768.0f;

        private class MaskPassData
        {
            internal Material Material;
            internal int PassIndex;
            internal TextureHandle Source;
            internal Vector4 SunPosition;
            internal Vector4 Threshold;
            internal Vector4 MaskTexelSize;
        }

        private class BlurPassData
        {
            internal Material Material;
            internal int PassIndex;
            internal TextureHandle Mask;
            internal TextureHandle Temp;
            internal Vector4 SunPosition;
            internal float BlurRadius;
            internal int Iterations;
        }

        private class CompositePassData
        {
            internal Material Material;
            internal int PassIndex;
            internal TextureHandle Mask;
            internal Vector4 SunColor;
        }

        private readonly LazyMaterial _sunShaftsMaterial = new(IllusionShaders.SunShafts);

        private readonly ProfilingSampler _maskSampler = new("Sun Shafts Depth Mask");

        private readonly ProfilingSampler _blurSampler = new("Sun Shafts Radial Blur");

        private readonly ProfilingSampler _compositeSampler = new("Sun Shafts Composite");

        private int _depthMaskPassIndex;

        private int _radialBlurPassIndex;

        private int _screenPassIndex;

        private int _addPassIndex;

        private SunShaftCasterManager _casterManager;

        public SunShaftsPass()
        {
            profilingSampler = new ProfilingSampler("Sun Shafts");
            renderPassEvent = IllusionRenderPassEvent.SunShaftsPass;
            InitializePassesIndices();
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private void InitializePassesIndices()
        {
            var material = _sunShaftsMaterial.Value;
            _depthMaskPassIndex = material.FindPass("SunShaftsDepthMask");
            _radialBlurPassIndex = material.FindPass("SunShaftsRadialBlur");
            _screenPassIndex = material.FindPass("SunShaftsScreen");
            _addPassIndex = material.FindPass("SunShaftsAdd");
        }

        public void Setup(SunShaftCasterManager casterManager)
        {
            _casterManager = casterManager;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var volume = VolumeManager.instance.stack.GetComponent<SunShafts>();
            if (!volume || !volume.IsActive())
            {
                return;
            }

            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            var caster = _casterManager?.GetActiveCaster();
            Vector3 viewportPosition = caster
                ? cameraData.camera.WorldToViewportPoint(caster.position)
                : new Vector3(0.5f, 0.5f, 0.0f);

            int divider = volume.resolution.value switch
            {
                SunShaftsResolution.Low => 4,
                SunShaftsResolution.Normal => 2,
                _ => 1
            };

            var descriptor = cameraData.cameraTargetDescriptor;
            int width = Mathf.Max(1, descriptor.width / divider);
            int height = Mathf.Max(1, descriptor.height / divider);

            var sunPosition = new Vector4(viewportPosition.x, viewportPosition.y, viewportPosition.z,
                volume.maxRadius.value);

            TextureHandle mask = RenderDepthMaskPass(renderGraph, resource.activeColorTexture,
                resource.cameraDepthTexture, width, height, sunPosition, volume);
            mask = RenderRadialBlurPass(renderGraph, mask, width, height, sunPosition, volume);
            RenderCompositePass(renderGraph, mask, resource.activeColorTexture, viewportPosition.z, volume);
        }

        private TextureHandle RenderDepthMaskPass(RenderGraph renderGraph, TextureHandle cameraColorTexture,
            TextureHandle cameraDepthTexture, int width, int height, Vector4 sunPosition, SunShafts volume)
        {
            using var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Sun Shafts Depth Mask",
                out var passData, _maskSampler);

            var maskTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                name = MaskRTName
            });

            passData.Material = _sunShaftsMaterial.Value;
            passData.PassIndex = _depthMaskPassIndex;
            passData.SunPosition = sunPosition;
            passData.Threshold = volume.thresholdColor.value;
            passData.MaskTexelSize = new Vector4(1.0f / width, 1.0f / height, width, height);

            builder.UseTexture(cameraColorTexture);
            passData.Source = cameraColorTexture;
            builder.UseTexture(cameraDepthTexture);
            builder.SetRenderAttachment(maskTexture, 0);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
            {
                context.cmd.SetGlobalVector(ShaderIDs._SunShaftsSunPosition, data.SunPosition);
                context.cmd.SetGlobalVector(ShaderIDs._SunShaftsThreshold, data.Threshold);
                context.cmd.SetGlobalVector(ShaderIDs._SunShaftsMaskTexelSize, data.MaskTexelSize);
                Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.Material, data.PassIndex);
            });

            return maskTexture;
        }

        private TextureHandle RenderRadialBlurPass(RenderGraph renderGraph, TextureHandle maskTexture,
            int width, int height, Vector4 sunPosition, SunShafts volume)
        {
            using var builder = renderGraph.AddUnsafePass<BlurPassData>("Sun Shafts Radial Blur", out var passData,
                _blurSampler);

            var tempTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                name = BlurRTName
            });

            passData.Material = _sunShaftsMaterial.Value;
            passData.PassIndex = _radialBlurPassIndex;
            passData.SunPosition = sunPosition;
            passData.BlurRadius = volume.blurRadius.value;
            passData.Iterations = volume.iterations.value;

            builder.UseTexture(maskTexture, AccessFlags.ReadWrite);
            passData.Mask = maskTexture;
            builder.UseTexture(tempTexture, AccessFlags.ReadWrite);
            passData.Temp = tempTexture;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (BlurPassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetGlobalVector(ShaderIDs._SunShaftsSunPosition, data.SunPosition);

                // Material state resolves when the buffer executes, so a per blit value has to be a global.
                for (int i = 0; i < data.Iterations; i++)
                {
                    SetBlurStep(cmd, i == 0
                        ? data.BlurRadius / BlurStepReferenceHeight
                        : StepOffset(data.BlurRadius, i * 2));
                    Blitter.BlitCameraTexture(cmd, data.Mask, data.Temp, RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store, data.Material, data.PassIndex);

                    SetBlurStep(cmd, StepOffset(data.BlurRadius, i * 2 + 1));
                    Blitter.BlitCameraTexture(cmd, data.Temp, data.Mask, RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store, data.Material, data.PassIndex);
                }
            });

            return passData.Mask;
        }

        private static float StepOffset(float blurRadius, int step)
        {
            return blurRadius * (step * 6.0f) / BlurStepReferenceHeight;
        }

        private static void SetBlurStep(CommandBuffer cmd, float offset)
        {
            cmd.SetGlobalVector(ShaderIDs._SunShaftsBlurRadius4, new Vector4(offset, offset, 0.0f, 0.0f));
        }

        private void RenderCompositePass(RenderGraph renderGraph, TextureHandle maskTexture,
            TextureHandle cameraColorTexture, float viewportDepth, SunShafts volume)
        {
            using var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Sun Shafts Composite",
                out var passData, _compositeSampler);

            passData.Material = _sunShaftsMaterial.Value;
            passData.PassIndex = volume.blendMode.value == SunShaftsBlendMode.Screen
                ? _screenPassIndex
                : _addPassIndex;

            passData.SunColor = viewportDepth >= 0.0f
                ? (Vector4)volume.shaftsColor.value * volume.intensity.value
                : Vector4.zero;

            builder.UseTexture(maskTexture);
            passData.Mask = maskTexture;
            builder.SetRenderAttachment(cameraColorTexture, 0);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
            {
                context.cmd.SetGlobalVector(ShaderIDs._SunShaftsColor, data.SunColor);
                Blitter.BlitTexture(context.cmd, data.Mask, Vector2.one, data.Material, data.PassIndex);
            });
        }

        public void Dispose()
        {
            _sunShaftsMaterial.DestroyCache();
        }

        private static class ShaderIDs
        {
            public static readonly int _SunShaftsSunPosition = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SunShaftsBlurRadius4 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SunShaftsMaskTexelSize = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SunShaftsThreshold = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SunShaftsColor = MemberNameHelpers.ShaderPropertyID();
        }
    }
}
