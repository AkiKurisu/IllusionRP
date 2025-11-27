using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using ProfilingScope = UnityEngine.Rendering.ProfilingScope;
using UnityEngine.Assertions;
using System;

namespace Illusion.Rendering.Shadows
{
    public class ContactShadowsPass : ScriptableRenderPass, IDisposable
    {
        private readonly ProfilingSampler _contactShadowMapProfile;

        private ComputeShader _contactShadowComputeShader;

        private int _deferredContactShadowKernel;

        private readonly IllusionRendererData _rendererData;

        public ContactShadowsPass(IllusionRendererData rendererRendererData)
        {
            _rendererData = rendererRendererData;
            renderPassEvent = IllusionRenderPassEvent.ContactShadowsPass;
            _contactShadowMapProfile = new ProfilingSampler("Contact Shadow");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!_contactShadowComputeShader)
            {
                _contactShadowComputeShader = _rendererData.RuntimeResources.contactShadowsCS;
                Assert.IsNotNull(_contactShadowComputeShader);
                _deferredContactShadowKernel = _contactShadowComputeShader.FindKernel("ContactShadowMap");
            }
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat =
                RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.ContactShadowsRT, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_ContactShadowMap");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();

            float contactShadowRange = Mathf.Clamp(contactShadows.fadeDistance.value, 0.0f, contactShadows.maxDistance.value);
            float contactShadowFadeEnd = contactShadows.maxDistance.value;
            float contactShadowOneOverFadeRange = 1.0f / Mathf.Max(1e-6f, contactShadowRange);

            float contactShadowMinDist = Mathf.Min(contactShadows.minDistance.value, contactShadowFadeEnd);
            float contactShadowFadeIn = Mathf.Clamp(contactShadows.fadeInDistance.value, 1e-6f, contactShadowFadeEnd);

            var params1 = new Vector4(contactShadows.length.value, contactShadows.distanceScaleFactor.value, contactShadowFadeEnd, contactShadowOneOverFadeRange);
            var params2 = new Vector4(0, contactShadowMinDist, contactShadowFadeIn, contactShadows.rayBias.value * 0.01f);
            var params3 = new Vector4(contactShadows.sampleCount.value, contactShadows.thicknessScale.value * 10.0f, Time.renderedFrameCount % 8, 0);

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

        public void Dispose()
        {
            _contactShadowComputeShader = null;
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
