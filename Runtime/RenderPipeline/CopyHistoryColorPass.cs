using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Illusion.Rendering
{
    /// <summary>
    /// Copy history color to history color texture.
    /// </summary>
    public class CopyHistoryColorPass : CopyColorPass, IDisposable
    {
        private readonly Material _blitMaterial;

        private readonly Material _samplingMaterial;

        private readonly IllusionRendererData _rendererData;

        private CopyHistoryColorPass(IllusionRendererData rendererData, Material samplingMaterial, Material copyColorMaterial)
            : base(RenderPassEvent.BeforeRenderingPostProcessing - 1, samplingMaterial, copyColorMaterial)
        {
            _rendererData = rendererData;
            _samplingMaterial = samplingMaterial;
            _blitMaterial = copyColorMaterial;
        }

        public static CopyHistoryColorPass Create(IllusionRendererData rendererData)
        {
            var blitMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal/CoreBlit");
            var samplingMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/Sampling");
            return new CopyHistoryColorPass(rendererData, samplingMaterial, blitMaterial);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            ConfigureDescriptor(Downsampling.None, ref descriptor, out var filterMode);
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.CameraPreviousColorTextureRT, descriptor, filterMode,
                TextureWrapMode.Clamp, name: "_CameraPreviousColorTexture");
            ConfigureTarget(_rendererData.CameraPreviousColorTextureRT);
            ConfigureClear(ClearFlag.Color, Color.clear);
            Setup(renderingData.cameraData.renderer.cameraColorTargetHandle, _rendererData.CameraPreviousColorTextureRT, Downsampling.None);
            base.OnCameraSetup(cmd, ref renderingData);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_blitMaterial);
            CoreUtils.Destroy(_samplingMaterial);
        }
    }
}