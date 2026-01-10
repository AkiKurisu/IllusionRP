using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.Shadows
{
    public class ContactShadowsPass : ScriptableRenderPass, IDisposable
    {
        private readonly ProfilingSampler _contactShadowMapProfile;

        private readonly ComputeShader _contactShadowComputeShader;

        private readonly int _deferredContactShadowKernel;

        private readonly IllusionRendererData _rendererData;

        public ContactShadowsPass(IllusionRendererData rendererRendererData)
        {
            _rendererData = rendererRendererData;
            renderPassEvent = IllusionRenderPassEvent.ContactShadowsPass;
            _contactShadowMapProfile = new ProfilingSampler("Contact Shadow");
            _contactShadowComputeShader = _rendererData.RuntimeResources.contactShadowsCS;
            _deferredContactShadowKernel = _contactShadowComputeShader.FindKernel("ContactShadowMap");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            PrepareTexture(ref renderingData);
        }

        private void PrepareTexture(ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
#if UNITY_2023_1_OR_NEWER
            desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;
#else
            desc.graphicsFormat = RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;
#endif

            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.ContactShadowsRT, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_ContactShadowMap");
        }

        private void PrepareContactShadowParameters(ref RenderingData renderingData, 
            out Vector4 params1, out Vector4 params2, out Vector4 params3)
        {
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();

            float contactShadowRange = Mathf.Clamp(contactShadows.fadeDistance.value, 0.0f, contactShadows.maxDistance.value);
            float contactShadowFadeEnd = contactShadows.maxDistance.value;
            float contactShadowOneOverFadeRange = 1.0f / Mathf.Max(1e-6f, contactShadowRange);

            float contactShadowMinDist = Mathf.Min(contactShadows.minDistance.value, contactShadowFadeEnd);
            float contactShadowFadeIn = Mathf.Clamp(contactShadows.fadeInDistance.value, 1e-6f, contactShadowFadeEnd);

            params1 = new Vector4(contactShadows.length.value, contactShadows.distanceScaleFactor.value, contactShadowFadeEnd, contactShadowOneOverFadeRange);
            params2 = new Vector4(0, contactShadowMinDist, contactShadowFadeIn, contactShadows.rayBias.value * 0.01f);
            params3 = new Vector4(contactShadows.sampleCount.value, contactShadows.thicknessScale.value * 10.0f, Time.renderedFrameCount % 8, 0);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;

            PrepareContactShadowParameters(ref renderingData, out var params1, out var params2, out var params3);

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _contactShadowMapProfile))
            {
                cmd.SetComputeVectorParam(_contactShadowComputeShader, ShaderIDs._ContactShadowParamsParameters, params1);
                cmd.SetComputeVectorParam(_contactShadowComputeShader, ShaderIDs._ContactShadowParamsParameters2, params2);
                cmd.SetComputeVectorParam(_contactShadowComputeShader, ShaderIDs._ContactShadowParamsParameters3, params3);
                cmd.SetComputeTextureParam(_contactShadowComputeShader, _deferredContactShadowKernel, ShaderIDs._ContactShadowTextureUAV, _rendererData.ContactShadowsRT);
                cmd.DispatchCompute(_contactShadowComputeShader, _deferredContactShadowKernel, Mathf.CeilToInt(camera.pixelWidth / 8.0f), Mathf.CeilToInt(camera.pixelHeight / 8.0f), 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;

            PrepareTexture(ref renderingData);
                
            // Prepare parameters
            PrepareContactShadowParameters(ref renderingData, out var params1, out var params2, out var params3);

            // Import contact shadow RT
            TextureHandle contactShadowHandle = renderGraph.ImportTexture(_rendererData.ContactShadowsRT);

            using (var builder = renderGraph.AddComputePass<ContactShadowPassData>("Contact Shadow", out var passData, _contactShadowMapProfile))
            {
                passData.ComputeShader = _contactShadowComputeShader;
                passData.Kernel = _deferredContactShadowKernel;
                passData.Params1 = params1;
                passData.Params2 = params2;
                passData.Params3 = params3;
                passData.ContactShadowRT = builder.UseTexture(contactShadowHandle, IBaseRenderGraphBuilder.AccessFlags.Write);
                passData.DispatchX = Mathf.CeilToInt(camera.pixelWidth / 8.0f);
                passData.DispatchY = Mathf.CeilToInt(camera.pixelHeight / 8.0f);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (ContactShadowPassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetComputeVectorParam(data.ComputeShader, ShaderIDs._ContactShadowParamsParameters, data.Params1);
                    context.cmd.SetComputeVectorParam(data.ComputeShader, ShaderIDs._ContactShadowParamsParameters2, data.Params2);
                    context.cmd.SetComputeVectorParam(data.ComputeShader, ShaderIDs._ContactShadowParamsParameters3, data.Params3);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.Kernel, ShaderIDs._ContactShadowTextureUAV, data.ContactShadowRT);
                    context.cmd.DispatchCompute(data.ComputeShader, data.Kernel, data.DispatchX, data.DispatchY, 1);
                });
            }
        }

        private class ContactShadowPassData
        {
            public ComputeShader ComputeShader;
            public int Kernel;
            public Vector4 Params1;
            public Vector4 Params2;
            public Vector4 Params3;
            public TextureHandle ContactShadowRT;
            public int DispatchX;
            public int DispatchY;
        }
#endif

        public void Dispose()
        {
            // pass
        }

        private static class ShaderIDs
        {
            public static readonly int _ContactShadowParamsParameters = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _ContactShadowParamsParameters2 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _ContactShadowParamsParameters3 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _ContactShadowTextureUAV = MemberNameHelpers.ShaderPropertyID();
        }
    }
}
