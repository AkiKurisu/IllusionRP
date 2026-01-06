#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

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
        
        // Cached RTHandle wrappers for custom textures (RenderGraph)
        private RTHandle _weightTextureMaskRTHandle;
        
        private RTHandle _debugFontTexRTHandle;

#if UNITY_2023_1_OR_NEWER
        private class DebugHistogramPassData
        {
            internal ComputeShader DebugImageHistogramCs;
            internal int DebugImageHistogramKernel;
            internal TextureHandle SourceTexture;
            internal ComputeBuffer HistogramBuffer;
            internal int CameraWidth;
            internal int CameraHeight;
        }

        private class DebugExposurePassData
        {
            internal Material DebugExposureMaterial;
            internal TextureHandle OutputTexture;
            internal TextureHandle SourceTexture;
            internal TextureHandle CurrentExposure;
            internal TextureHandle PreviousExposure;
            internal TextureHandle ExposureDebugData;
            internal TextureHandle WeightTextureMask;
            internal ComputeBuffer HistogramBuffer;
            internal TextureHandle DebugFontTex;
            
            internal Vector4 ProceduralMaskParams;
            internal Vector4 ProceduralMaskParams2;
            internal Vector4 HistogramParams;
            internal Vector4 ExposureVariants;
            internal Vector4 ExposureParams;
            internal Vector4 ExposureParams2;
            internal Vector4 MousePixelCoord;
            internal Vector4 ExposureDebugParams;
            
            internal int PassIndex;
            internal ExposureDebugMode ExposureDebugMode;
        }

        private class FinalBlitPassData
        {
            internal TextureHandle Source;
            internal TextureHandle Destination;
        }
#endif
        
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
            PrepareDebugExposureData(ref renderingData);
        }
        
        private void PrepareDebugExposureData(ref RenderingData renderingData)
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

#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            // Prepare debug exposure data (replaces OnCameraSetup for RenderGraph)
            PrepareDebugExposureData(ref renderingData);
            
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            
            // Import textures
            var colorBeforePostProcess = _rendererData.GetPreviousFrameColorRT(renderingData.cameraData, out _);
            TextureHandle sourceBeforePostProcess = renderGraph.ImportTexture(colorBeforePostProcess);
            TextureHandle colorAfterPostProcess = renderer.activeColorTexture;
            TextureHandle debugOutputTexture = renderGraph.ImportTexture(_rendererData.DebugExposureTexture);
            
            // Stage 1: Generate debug histogram
            using (var builder = renderGraph.AddComputePass<DebugHistogramPassData>("Debug Image Histogram", 
                out var histogramPassData, new ProfilingSampler("Debug Image Histogram")))
            {
                histogramPassData.DebugImageHistogramCs = _debugImageHistogramCs;
                histogramPassData.DebugImageHistogramKernel = _debugImageHistogramKernel;
                histogramPassData.SourceTexture = builder.UseTexture(sourceBeforePostProcess);
                histogramPassData.HistogramBuffer = _rendererData.DebugImageHistogram;
                histogramPassData.CameraWidth = renderingData.cameraData.camera.pixelWidth;
                histogramPassData.CameraHeight = renderingData.cameraData.camera.pixelHeight;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc(static (DebugHistogramPassData data, ComputeGraphContext context) =>
                {
                    DoGenerateDebugImageHistogram(data, context.cmd);
                });
            }
            
            // Stage 2: Render debug exposure overlay
            using (var builder = renderGraph.AddRasterRenderPass<DebugExposurePassData>("Debug Exposure Overlay", 
                out var debugPassData, profilingSampler))
            {
                PrepareDebugPassData(debugPassData, ref renderingData, renderGraph, 
                    colorAfterPostProcess, debugOutputTexture);
                
                debugPassData.OutputTexture = builder.UseTextureFragment(debugOutputTexture, 0);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (DebugExposurePassData data, RasterGraphContext context) =>
                {
                    DoDebugExposure(data, context.cmd);
                });
            }
            
            // Stage 3: Final blit to camera target
            using (var builder = renderGraph.AddRasterRenderPass<FinalBlitPassData>("Debug Exposure Final Blit", 
                out var blitPassData, new ProfilingSampler("Debug Exposure Final Blit")))
            {
                blitPassData.Source = builder.UseTexture(debugOutputTexture);
                blitPassData.Destination = builder.UseTextureFragment(renderer.activeColorTexture, 0);
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc(static (FinalBlitPassData data, RasterGraphContext context) =>
                {
                    // Blit debug output to camera target
                    Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }

        private void PrepareDebugPassData(DebugExposurePassData passData, ref RenderingData renderingData, 
            RenderGraph renderGraph, TextureHandle sourceTexture, TextureHandle outputTexture)
        {
            var renderingConfig = IllusionRuntimeRenderingConfig.Get();
            
            passData.DebugExposureMaterial = _debugExposureMaterial.Value;
            passData.SourceTexture = sourceTexture;
            passData.OutputTexture = outputTexture;
            
            var currentExposure = _rendererData.GetExposureTexture();
            var previousExposure = _rendererData.GetPreviousExposureTexture();
            var debugExposureData = _rendererData.GetExposureDebugData();
            
            passData.CurrentExposure = renderGraph.ImportTexture(currentExposure);
            passData.PreviousExposure = renderGraph.ImportTexture(previousExposure);
            passData.ExposureDebugData = renderGraph.ImportTexture(debugExposureData);
            
            passData.HistogramBuffer = renderingConfig.ExposureDebugMode == ExposureDebugMode.FinalImageHistogramView 
                ? _rendererData.DebugImageHistogram 
                : _rendererData.HistogramBuffer;
            
            // Import Texture2D resources as TextureHandle with cached RTHandle wrappers
            var currentWeightMask = _exposure.weightTextureMask.value;
            if (_weightTextureMaskRTHandle == null || currentWeightMask == null)
            {
                // Release old RTHandle if texture changed
                if (_weightTextureMaskRTHandle != null)
                {
                    RTHandles.Release(_weightTextureMaskRTHandle);
                    _weightTextureMaskRTHandle = null;
                }
                
                // Create new RTHandle wrapper
                if (currentWeightMask != null)
                {
                    _weightTextureMaskRTHandle = RTHandles.Alloc(currentWeightMask);
                }
            }
            
            RTHandle weightMaskHandle = _weightTextureMaskRTHandle ?? _rendererData.GetWhiteTextureRT();
            passData.WeightTextureMask = renderGraph.ImportTexture(weightMaskHandle);
            
            // Cache debug font RTHandle (only allocate once as it doesn't change)
            if (_debugFontTexRTHandle == null)
            {
                _debugFontTexRTHandle = RTHandles.Alloc(_rendererData.RuntimeResources.debugFontTex);
            }
            passData.DebugFontTex = renderGraph.ImportTexture(_debugFontTexRTHandle);
            
            _exposure.ComputeProceduralMeteringParams(renderingData.cameraData.camera, 
                out passData.ProceduralMaskParams, out passData.ProceduralMaskParams2);
            
            passData.ExposureParams = new Vector4(_exposure.compensation.value, _exposure.limitMin.value,
                _exposure.limitMax.value, 0f);
            passData.ExposureVariants = new Vector4(1.0f, (int)_exposure.meteringMode.value, 
                (int)_exposure.adaptationMode.value, 0.0f);
            
            Vector2 histogramFraction = _exposure.histogramPercentages.value / 100.0f;
            float evRange = _exposure.limitMax.value - _exposure.limitMin.value;
            float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
            float histBias = -_exposure.limitMin.value * histScale;
            passData.HistogramParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
            passData.ExposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, 
                ColorUtils.s_LightMeterCalibrationConstant);
            
            passData.MousePixelCoord = IllusionRenderingUtils.GetMouseCoordinates(ref renderingData.cameraData);
            passData.ExposureDebugMode = renderingConfig.ExposureDebugMode;
            
            // Determine pass index and debug params based on debug mode
            passData.PassIndex = 0;
            if (renderingConfig.ExposureDebugMode == ExposureDebugMode.MeteringWeighted)
            {
                passData.PassIndex = 1;
                passData.ExposureDebugParams = new Vector4(renderingConfig.DisplayMaskOnly ? 1 : 0, 0, 0, 0);
            }
            else if (renderingConfig.ExposureDebugMode == ExposureDebugMode.HistogramView)
            {
                var tonemappingSettings = VolumeManager.instance.stack.GetComponent<Tonemapping>();
                var tonemappingMode = tonemappingSettings.IsActive() ? tonemappingSettings.mode.value : TonemappingMode.None;
                bool centerAroundMiddleGrey = renderingConfig.CenterHistogramAroundMiddleGrey;
                bool displayOverlay = renderingConfig.DisplayOnSceneOverlay;
                passData.ExposureDebugParams = new Vector4(0.0f, (int)tonemappingMode, 
                    centerAroundMiddleGrey ? 1 : 0, displayOverlay ? 1 : 0);
                passData.PassIndex = 2;
            }
            else if (renderingConfig.ExposureDebugMode == ExposureDebugMode.FinalImageHistogramView)
            {
                bool finalImageRGBHistogram = renderingConfig.DisplayFinalImageHistogramAsRGB;
                passData.ExposureDebugParams = new Vector4(0, 0, 0, finalImageRGBHistogram ? 1 : 0);
                passData.PassIndex = 3;
            }
        }
#endif

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

#if UNITY_2023_1_OR_NEWER
        private static void DoGenerateDebugImageHistogram(DebugHistogramPassData data, ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(data.DebugImageHistogramCs, data.DebugImageHistogramKernel, 
                ExposureShaderIDs._SourceTexture, data.SourceTexture);
            cmd.SetComputeBufferParam(data.DebugImageHistogramCs, data.DebugImageHistogramKernel, 
                ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);

            int threadGroupSizeX = 16;
            int threadGroupSizeY = 16;
            int dispatchSizeX = IllusionRenderingUtils.DivRoundUp(data.CameraWidth / 2, threadGroupSizeX);
            int dispatchSizeY = IllusionRenderingUtils.DivRoundUp(data.CameraHeight / 2, threadGroupSizeY);
            cmd.DispatchCompute(data.DebugImageHistogramCs, data.DebugImageHistogramKernel, dispatchSizeX, dispatchSizeY, 1);
        }

        private static void DoDebugExposure(DebugExposurePassData data, RasterCommandBuffer cmd)
        {
            var material = data.DebugExposureMaterial;
            
            material.SetVector(ExposureShaderIDs._ProceduralMaskParams, data.ProceduralMaskParams);
            material.SetVector(ExposureShaderIDs._ProceduralMaskParams2, data.ProceduralMaskParams2);
            material.SetVector(ExposureShaderIDs._HistogramExposureParams, data.HistogramParams);
            material.SetVector(ExposureShaderIDs._Variants, data.ExposureVariants);
            material.SetVector(ExposureShaderIDs._ExposureParams, data.ExposureParams);
            material.SetVector(ExposureShaderIDs._ExposureParams2, data.ExposureParams2);
            material.SetVector(ExposureShaderIDs._MousePixelCoord, data.MousePixelCoord);
            material.SetTexture(ExposureShaderIDs._SourceTexture, data.SourceTexture);
            material.SetTexture(ExposureShaderIDs._DebugFullScreenTexture, data.SourceTexture);
            material.SetTexture(ExposureShaderIDs._PreviousExposureTexture, data.PreviousExposure);
            material.SetTexture(IllusionShaderProperties._ExposureTexture, data.CurrentExposure);
            material.SetTexture(ExposureShaderIDs._ExposureWeightMask, data.WeightTextureMask);
            material.SetBuffer(ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);
            material.SetTexture(ExposureShaderIDs._DebugFont, data.DebugFontTex);
            
            if (data.ExposureDebugMode == ExposureDebugMode.HistogramView)
            {
                material.SetTexture(ExposureShaderIDs._ExposureDebugTexture, data.ExposureDebugData);
            }
            else if (data.ExposureDebugMode == ExposureDebugMode.FinalImageHistogramView)
            {
                material.SetBuffer(ExposureShaderIDs._FullImageHistogram, data.HistogramBuffer);
            }
            
            material.SetVector(ExposureShaderIDs._ExposureDebugParams, data.ExposureDebugParams);
            
            cmd.DrawProcedural(Matrix4x4.identity, material, data.PassIndex, MeshTopology.Triangles, 3, 1);
        }
#endif

        public void Dispose()
        {
            _debugExposureMaterial.DestroyCache();
            
            // Release cached RTHandle wrappers
            RTHandles.Release(_weightTextureMaskRTHandle);
            RTHandles.Release(_debugFontTexRTHandle);
            _weightTextureMaskRTHandle = null;
            _debugFontTexRTHandle = null;
        }
    }
}
#endif