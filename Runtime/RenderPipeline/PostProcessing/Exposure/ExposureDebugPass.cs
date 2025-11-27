#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    internal class ExposureDebugPass : ScriptableRenderPass, IDisposable
    {
        private const int DebugImageHistogramBins = 256;   // Important! If this changes, need to change HistogramExposure.compute
        
        private readonly int[] _emptyDebugImageHistogram = new int[DebugImageHistogramBins * 4];
        
        private readonly ComputeShader _debugImageHistogramCs;

        private readonly int _debugImageHistogramKernel;

        private Exposure _exposure;

        private readonly LazyMaterial _debugExposureMaterial = new(IllusionShaders.DebugExposure);
        
        private ComputeBuffer _histogramBuffer;

        private readonly IllusionRendererData _rendererData;
        
        public ExposureDebugPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            _debugImageHistogramCs = rendererData.RuntimeResources.debugImageHistogramCS;
            _debugImageHistogramKernel = _debugImageHistogramCs.FindKernel("KHistogramGen");
            profilingSampler = new ProfilingSampler("Exposure Debug");
            renderPassEvent = IllusionRenderPassEvent.FullScreenDebugPass;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
            IllusionRenderingUtils.ValidateComputeBuffer(ref _rendererData.DebugImageHistogram, DebugImageHistogramBins, 4 * sizeof(uint));
            _rendererData.DebugImageHistogram.SetData(_emptyDebugImageHistogram);    // Clear the histogram
            
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = (int)DepthBits.None;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref _rendererData.DebugExposureTexture, descriptor,
                wrapMode: TextureWrapMode.Clamp, name: "ExposureDebug");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                var colorBeforePostProcess = _rendererData.GetPreviousFrameColorRT(renderingData.cameraData, out _);
                DoGenerateDebugImageHistogram(cmd, ref renderingData, colorBeforePostProcess);
                var colorAfterPostProcess = renderingData.cameraData.renderer.GetCameraColorBackBuffer(cmd);
                DoDebugExposure(cmd, ref renderingData, colorAfterPostProcess);
                IllusionRenderingUtils.FinalBlit(cmd, ref renderingData, _rendererData.DebugExposureTexture);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DoDebugExposure(CommandBuffer cmd, ref RenderingData renderingData, RTHandle sourceTexture)
        {
            var renderingConfig = IllusionRuntimeRenderingConfig.Get();
            
            var currentExposure = _rendererData.GetExposureTexture();
            var previousExposure =_rendererData.GetPreviousExposureTexture();
            var debugExposureData = _rendererData.GetExposureDebugData();
            _histogramBuffer = renderingConfig.ExposureDebugMode == ExposureDebugMode.FinalImageHistogramView ? _rendererData.DebugImageHistogram : _rendererData.HistogramBuffer;

            
            _exposure.ComputeProceduralMeteringParams(renderingData.cameraData.camera, 
                out var proceduralMeteringParams1, out var proceduralMeteringParams2);
            Vector4 exposureParams = new Vector4(_exposure.compensation.value, _exposure.limitMin.value,
                            _exposure.limitMax.value, 0f);

            Vector4 exposureVariants = new Vector4(1.0f, (int)_exposure.meteringMode.value, (int)_exposure.adaptationMode.value, 0.0f);
            Vector2 histogramFraction = _exposure.histogramPercentages.value / 100.0f;
            float evRange = _exposure.limitMax.value - _exposure.limitMin.value;
            float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
            float histBias = -_exposure.limitMin.value * histScale;
            Vector4 histogramParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);

            var material = _debugExposureMaterial.Value;
            material.SetVector(ExposureShaderIDs._ProceduralMaskParams, proceduralMeteringParams1);
            material.SetVector(ExposureShaderIDs._ProceduralMaskParams2, proceduralMeteringParams2);

            material.SetVector(ExposureShaderIDs._HistogramExposureParams, histogramParams);
            material.SetVector(ExposureShaderIDs._Variants, exposureVariants);
            material.SetVector(ExposureShaderIDs._ExposureParams, exposureParams);
            material.SetVector(ExposureShaderIDs._ExposureParams2, new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant));
            material.SetVector(ExposureShaderIDs._MousePixelCoord, IllusionRenderingUtils.GetMouseCoordinates(ref renderingData.cameraData));
            material.SetTexture(ExposureShaderIDs._SourceTexture, sourceTexture);
            material.SetTexture(ExposureShaderIDs._DebugFullScreenTexture, sourceTexture);
            material.SetTexture(ExposureShaderIDs._PreviousExposureTexture, previousExposure);
            material.SetTexture(IllusionShaderProperties._ExposureTexture, currentExposure);
            material.SetTexture(ExposureShaderIDs._ExposureWeightMask, _exposure.weightTextureMask.value);
            material.SetBuffer(ExposureShaderIDs._HistogramBuffer, _histogramBuffer);
            material.SetTexture(ExposureShaderIDs._DebugFont, _rendererData.RuntimeResources.debugFontTex);


            int passIndex = 0;
            if (renderingConfig.ExposureDebugMode == ExposureDebugMode.MeteringWeighted)
            {
                passIndex = 1;
                material.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(renderingConfig.DisplayMaskOnly ? 1 : 0, 0, 0, 0));
            }
            if (renderingConfig.ExposureDebugMode == ExposureDebugMode.HistogramView)
            {
                material.SetTexture(ExposureShaderIDs._ExposureDebugTexture, debugExposureData);
                var tonemappingSettings = VolumeManager.instance.stack.GetComponent<Tonemapping>();
                var tonemappingMode = tonemappingSettings.IsActive() ? tonemappingSettings.mode.value : TonemappingMode.None;

                bool centerAroundMiddleGrey = renderingConfig.CenterHistogramAroundMiddleGrey;
                bool displayOverlay = renderingConfig.DisplayOnSceneOverlay;
                material.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(0.0f, (int)tonemappingMode, centerAroundMiddleGrey ? 1 : 0, displayOverlay ? 1 : 0));
                passIndex = 2;
            }
            if (renderingConfig.ExposureDebugMode == ExposureDebugMode.FinalImageHistogramView)
            {
                bool finalImageRGBHistogram = renderingConfig.DisplayFinalImageHistogramAsRGB;

                material.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(0, 0, 0, finalImageRGBHistogram ? 1 : 0));
                material.SetBuffer(ExposureShaderIDs._FullImageHistogram, _histogramBuffer);
                passIndex = 3;
            }
            
            CoreUtils.SetRenderTarget(cmd, _rendererData.DebugExposureTexture);
            cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1);
        }

        private void DoGenerateDebugImageHistogram(CommandBuffer cmd, ref RenderingData renderingData, RTHandle sourceTexture)
        {
            int cameraWidth = renderingData.cameraData.camera.pixelWidth;
            int cameraHeight = renderingData.cameraData.camera.pixelHeight;
            cmd.SetComputeTextureParam(_debugImageHistogramCs, _debugImageHistogramKernel, ExposureShaderIDs._SourceTexture, sourceTexture);
            cmd.SetComputeBufferParam(_debugImageHistogramCs, _debugImageHistogramKernel, ExposureShaderIDs._HistogramBuffer, _rendererData.DebugImageHistogram);

            int threadGroupSizeX = 16;
            int threadGroupSizeY = 16;
            int dispatchSizeX = IllusionRenderingUtils.DivRoundUp(cameraWidth / 2, threadGroupSizeX);
            int dispatchSizeY = IllusionRenderingUtils.DivRoundUp(cameraHeight / 2, threadGroupSizeY);
            cmd.DispatchCompute(_debugImageHistogramCs, _debugImageHistogramKernel, dispatchSizeX, dispatchSizeY, 1);
        }

        public void Dispose()
        {
            _debugExposureMaterial.DestroyCache();
        }
    }
}
#endif