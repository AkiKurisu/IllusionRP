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
    // Currently IllusionRP only supports Fixed and Automatic Histogram mode.
    public class ExposurePass : ScriptableRenderPass, IDisposable
    {
        // Exposure data
        private const int ExposureCurvePrecision = 128;
        
        private const int HistogramBins = 128;   // Important! If this changes, need to change HistogramExposure.compute

        private readonly Color[] _exposureCurveColorArray = new Color[ExposureCurvePrecision];
        
        private readonly ComputeShader _histogramExposureCs;

        private readonly LazyMaterial _applyExposureMaterial;
        
        private readonly int _exposurePreparationKernel;
        
        private readonly int _exposureReductionKernel;

        private readonly int[] _emptyHistogram = new int[HistogramBins];
        
        private readonly int[] _exposureVariants = new int[4];
        
        private Texture _textureMeteringMask;

        private Exposure _exposure;
        
        private Vector4 _proceduralMaskParams;
        
        private Vector4 _proceduralMaskParams2;
        
        // private ExposureMode exposureMode;
        
        private Vector4 _exposureParams;
        
        private Vector4 _exposureParams2;
        
        private Texture _exposureCurve;
        
        private Vector4 _histogramExposureParams;
        
        private Vector4 _adaptationParams;
        
        private bool _histogramUsesCurve;
        
        private bool _histogramOutputDebugData;
        
        private Texture2D _exposureCurveTexture;
        
        private readonly ComputeShader _exposureCS;

        private readonly IllusionRendererData _rendererData;
        
        private readonly ProfilingSampler _fixedExposureSampler = new("Fixed Exposure");
                
        private readonly ProfilingSampler _automaticExposureSampler = new("Automatic Exposure");
        
        // Cached RTHandle wrappers for custom textures (RenderGraph)
        private RTHandle _textureMeteringMaskRTHandle;
        
        private RTHandle _exposureCurveRTHandle;

#if UNITY_2023_1_OR_NEWER
        private class ExposurePassData
        {
            internal ComputeShader HistogramExposureCs;
            internal ComputeShader ExposureCs;
            internal int ExposurePreparationKernel;
            internal int ExposureReductionKernel;
            
            internal TextureHandle Source;
            internal TextureHandle PrevExposure;
            internal TextureHandle NextExposure;
            internal TextureHandle ExposureDebugData;
            
            // For setting global textures
            internal TextureHandle CurrentExposureTexture;
            internal TextureHandle PreviousExposureTexture;
            
            internal ComputeBuffer HistogramBuffer;
            internal TextureHandle TextureMeteringMask;
            internal TextureHandle ExposureCurve;
            
            internal int[] ExposureVariants;
            internal Vector4 ExposureParams;
            internal Vector4 ExposureParams2;
            internal Vector4 ProceduralMaskParams;
            internal Vector4 ProceduralMaskParams2;
            internal Vector4 HistogramExposureParams;
            internal Vector4 AdaptationParams;
            
            internal bool IsFixedExposure;
            internal bool HistogramUsesCurve;
            internal bool HistogramOutputDebugData;
            internal bool ResetPostProcessingHistory;
            
            internal int CameraWidth;
            internal int CameraHeight;
            
            internal IllusionRendererData RendererData;
            internal Material ApplyExposureMaterial;
        }

        private class ApplyExposurePassData
        {
            internal Material ApplyExposureMaterial;
            internal TextureHandle Source;
            internal TextureHandle Destination;
        }
#endif
        
        public ExposurePass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            _applyExposureMaterial = new LazyMaterial(IllusionShaders.ApplyExposure);
            _histogramExposureCs = rendererData.RuntimeResources.histogramExposureCS;
            _histogramExposureCs.shaderKeywords = null;
            _exposurePreparationKernel = _histogramExposureCs.FindKernel("KHistogramGen");
            _exposureReductionKernel = _histogramExposureCs.FindKernel("KHistogramReduce");
            _exposureCS = rendererData.RuntimeResources.exposureCS;
            renderPassEvent = IllusionRenderPassEvent.ExposurePass;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            PrepareExposureData(ref renderingData);
        }
        
        private void PrepareExposureData(ref RenderingData renderingData)
        {
            _exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
            if (_rendererData.IsExposureFixed())
            {
                return;
            }

            var renderingConfig = IllusionRuntimeRenderingConfig.Get();
            // Setup variants
            var adaptationMode = _exposure.adaptationMode.value;
            _exposureVariants[0] = 1; // (int)exposureSettings.luminanceSource.value;
            _exposureVariants[1] = (int)_exposure.meteringMode.value;
            _exposureVariants[2] = (int)adaptationMode;
            _exposureVariants[3] = 0;
            
            bool useTextureMask = _exposure.meteringMode.value == MeteringMode.MaskWeighted && _exposure.weightTextureMask.value != null;
            _textureMeteringMask = useTextureMask ? _exposure.weightTextureMask.value : Texture2D.whiteTexture;
            
            _exposure.ComputeProceduralMeteringParams(renderingData.cameraData.camera, out _proceduralMaskParams, out _proceduralMaskParams2);
            
            // exposureMode = m_Exposure.mode.value;
            // bool isHistogramBased = m_Exposure.mode.value == ExposureMode.AutomaticHistogram;
            // bool needsCurve = (isHistogramBased && m_Exposure.histogramUseCurveRemapping.value) || m_Exposure.mode.value == ExposureMode.CurveMapping;
            bool needsCurve = _exposure.histogramUseCurveRemapping.value;

            _histogramUsesCurve = _exposure.histogramUseCurveRemapping.value;

            // When recording with accumulation, unity_DeltaTime is adjusted to account for the subframes.
            // To match the ganeview's exposure adaptation when recording, we adjust similarly the speed.
            // float speedMultiplier = m_SubFrameManager.isRecording ? (float) m_SubFrameManager.subFrameCount : 1.0f;
            float speedMultiplier = 1.0f;
            _adaptationParams = new Vector4(_exposure.adaptationSpeedLightToDark.value * speedMultiplier, 
                _exposure.adaptationSpeedDarkToLight.value * speedMultiplier, 0.0f, 0.0f);


            float limitMax = _exposure.limitMax.value;
            float limitMin = _exposure.limitMin.value;

            float curveMin = 0.0f;
            float curveMax = 0.0f;
            if (needsCurve)
            {
                PrepareExposureCurveData(out curveMin, out curveMax);
                limitMin = curveMin;
                limitMax = curveMax;
            }

            float m_DebugExposureCompensation = 0;
            _exposureParams = new Vector4(_exposure.compensation.value + m_DebugExposureCompensation, limitMin, limitMax, 0f);
            _exposureParams2 = new Vector4(curveMin, curveMax, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);

            _exposureCurve = _exposureCurveTexture;

            // if (isHistogramBased)
            {
                IllusionRenderingUtils.ValidateComputeBuffer(ref _rendererData.HistogramBuffer, HistogramBins, sizeof(uint));
                _rendererData.HistogramBuffer.SetData(_emptyHistogram);    // Clear the histogram

                Vector2 histogramFraction = _exposure.histogramPercentages.value / 100.0f;
                float evRange = limitMax - limitMin;
                float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
                float histBias = -limitMin * histScale;
                _histogramExposureParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
                _histogramOutputDebugData = renderingConfig.ExposureDebugMode == ExposureDebugMode.HistogramView;
                if (_histogramOutputDebugData)
                {
                    _histogramExposureCs.EnableKeyword("OUTPUT_DEBUG_DATA");
                }
            }
        }
        
        private void PrepareExposureCurveData(out float min, out float max)
        {
            var curve = _exposure.curveMap.value;
            var minCurve = _exposure.limitMinCurveMap.value;
            var maxCurve = _exposure.limitMaxCurveMap.value;

            if (_exposureCurveTexture == null)
            {
                _exposureCurveTexture = new Texture2D(ExposureCurvePrecision, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
                {
                    name = "Exposure Curve",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            bool minCurveHasPoints = minCurve.length > 0;
            bool maxCurveHasPoints = maxCurve.length > 0;
            float defaultMin = -100.0f;
            float defaultMax = 100.0f;

            var pixels = _exposureCurveColorArray;

            // Fail safe in case the curve is deleted / has 0 point
            if (curve == null || curve.length == 0)
            {
                min = 0f;
                max = 0f;

                for (int i = 0; i < ExposureCurvePrecision; i++)
                    pixels[i] = Color.clear;
            }
            else
            {
                min = curve[0].time;
                max = curve[curve.length - 1].time;
                float step = (max - min) / (ExposureCurvePrecision - 1f);

                for (int i = 0; i < ExposureCurvePrecision; i++)
                {
                    float currTime = min + step * i;
                    pixels[i] = new Color(curve.Evaluate(currTime),
                        minCurveHasPoints ? minCurve.Evaluate(currTime) : defaultMin,
                        maxCurveHasPoints ? maxCurve.Evaluate(currTime) : defaultMax,
                        0f);
                }
            }

            _exposureCurveTexture.SetPixels(pixels);
            _exposureCurveTexture.Apply();
        }
        
        private void DoFixedExposure(CommandBuffer cmd, CameraData cameraData)
        {
            ComputeShader cs = _exposureCS;
            int kernel;
            float m_DebugExposureCompensation = 0;
            Vector4 exposureParams;
            Vector4 exposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);
            // if (_automaticExposure.mode.value == ExposureMode.Fixed)
            {
                kernel = cs.FindKernel("KFixedExposure");
                exposureParams = new Vector4(_exposure.compensation.value + m_DebugExposureCompensation, _exposure.fixedExposure.value, 0f, 0f);
            }
            // else // ExposureMode.UsePhysicalCamera
            // {
            //     kernel = cs.FindKernel("KManualCameraExposure");
            //     exposureParams = new Vector4(_automaticExposure.compensation.value + m_DebugExposureCompensation, cameraData.camera.aperture, cameraData.camera.shutterSpeed, cameraData.camera.iso);
            // }

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, exposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.Exposure));
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }

#if UNITY_2023_1_OR_NEWER
        private static void DoFixedExposure(ExposurePassData data, ComputeCommandBuffer cmd)
        {
            ComputeShader cs = data.ExposureCs;
            int kernel = cs.FindKernel("KFixedExposure");

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, data.ExposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, data.ExposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, data.NextExposure);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
#endif
        
        private void DoHistogramBasedExposure(CommandBuffer cmd, ref RenderingData renderingData, RTHandle source)
        {
            var cs = _histogramExposureCs;
            _rendererData.GrabExposureRequiredTextures(out var prevExposure, out var nextExposure);
            var histogramBuffer = _rendererData.HistogramBuffer;

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams, _proceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams2, _proceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._HistogramExposureParams, _histogramExposureParams);

            // Generate histogram.
            var kernel = _exposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, source);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureWeightMask, _textureMeteringMask);

            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, _exposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, histogramBuffer);

            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int width = renderingData.cameraData.camera.pixelWidth;
            int height = renderingData.cameraData.camera.pixelHeight;
            int dispatchSizeX = IllusionRenderingUtils.DivRoundUp(width / 2, threadGroupSizeX);
            int dispatchSizeY = IllusionRenderingUtils.DivRoundUp(height / 2, threadGroupSizeY);

            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);

            // Now read the histogram
            kernel = _exposureReductionKernel;
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, _exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, _exposureParams2);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._AdaptationParams, _adaptationParams);
            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, histogramBuffer);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, nextExposure);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureCurveTexture, _exposureCurve);
            _exposureVariants[3] = 0;
            if (_histogramUsesCurve)
            {
                _exposureVariants[3] = 2;
            }
            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, _exposureVariants);

            if (_histogramOutputDebugData)
            {
                var exposureDebugData = _rendererData.GetExposureDebugData();
                cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureDebugTexture, exposureDebugData);
            }

            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }

#if UNITY_2023_1_OR_NEWER
        private static void DoHistogramBasedExposure(ExposurePassData data, ComputeCommandBuffer cmd)
        {
            var cs = data.HistogramExposureCs;

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams, data.ProceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams2, data.ProceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._HistogramExposureParams, data.HistogramExposureParams);

            // Generate histogram.
            var kernel = data.ExposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.PrevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, data.Source);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureWeightMask, data.TextureMeteringMask);

            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, data.ExposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);

            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int dispatchSizeX = IllusionRenderingUtils.DivRoundUp(data.CameraWidth / 2, threadGroupSizeX);
            int dispatchSizeY = IllusionRenderingUtils.DivRoundUp(data.CameraHeight / 2, threadGroupSizeY);

            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);

            // Now read the histogram
            kernel = data.ExposureReductionKernel;
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, data.ExposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, data.ExposureParams2);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._AdaptationParams, data.AdaptationParams);
            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.PrevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, data.NextExposure);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureCurveTexture, data.ExposureCurve);
            data.ExposureVariants[3] = 0;
            if (data.HistogramUsesCurve)
            {
                data.ExposureVariants[3] = 2;
            }
            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, data.ExposureVariants);

            if (data.HistogramOutputDebugData)
            {
                cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureDebugTexture, data.ExposureDebugData);
            }

            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
#endif
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var colorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get();
            if (_rendererData.CanRunFixedExposurePass())
            {
                using (new ProfilingScope(cmd, _fixedExposureSampler))
                {
                    DoFixedExposure(cmd, renderingData.cameraData);
                }
            }
            else
            {
                using (new ProfilingScope(cmd, _automaticExposureSampler))
                {
                    DoHistogramBasedExposure(cmd, ref renderingData, colorHandle);

                    if (_rendererData.ResetPostProcessingHistory)
                    {
                        Blit(cmd, ref renderingData, _applyExposureMaterial.Value); // Swap Front to Back
                    }
                }
            }
            cmd.SetGlobalTexture(IllusionShaderProperties._ExposureTexture, _rendererData.GetExposureTexture());
            cmd.SetGlobalTexture(IllusionShaderProperties._PrevExposureTexture, _rendererData.GetPreviousExposureTexture());
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

#if UNITY_2023_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            PrepareExposureData(ref renderingData);
            
            UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
            
            bool isFixedExposure = _rendererData.CanRunFixedExposurePass();
            bool resetHistory = _rendererData.ResetPostProcessingHistory;
            ProfilingSampler sampler = isFixedExposure ? _fixedExposureSampler : _automaticExposureSampler;
            
            // Main exposure pass
            using (var builder = renderGraph.AddComputePass<ExposurePassData>("Exposure Pass", out var passData, sampler))
            {
                // Setup pass data
                PreparePassDataForRenderGraph(builder, passData, ref renderingData, renderer, isFixedExposure, renderGraph);
                
                _rendererData.GrabExposureRequiredTextures(out var prevExposure, out var nextExposure);
                passData.PrevExposure = builder.UseTexture(renderGraph.ImportTexture(prevExposure));
                passData.NextExposure = builder.UseTexture(renderGraph.ImportTexture(nextExposure), IBaseRenderGraphBuilder.AccessFlags.Write);
                
                if (!isFixedExposure && passData.HistogramOutputDebugData)
                {
                    var exposureDebugData = _rendererData.GetExposureDebugData();
                    passData.ExposureDebugData = builder.UseTexture(renderGraph.ImportTexture(exposureDebugData), IBaseRenderGraphBuilder.AccessFlags.Write);
                }
                
                // Import textures for SetGlobalTexture
                var currentExposureRT = _rendererData.GetExposureTexture();
                var previousExposureRT = _rendererData.GetPreviousExposureTexture();
                passData.CurrentExposureTexture = builder.UseTexture(renderGraph.ImportTexture(currentExposureRT));
                passData.PreviousExposureTexture = builder.UseTexture(renderGraph.ImportTexture(previousExposureRT));
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                // Set render function
                builder.SetRenderFunc(static (ExposurePassData data, ComputeGraphContext context) =>
                {
                    if (data.IsFixedExposure)
                    {
                        DoFixedExposure(data, context.cmd);
                    }
                    else
                    {
                        DoHistogramBasedExposure(data, context.cmd);
                    }
                    
                    // Set global textures using TextureHandle
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._ExposureTexture, data.CurrentExposureTexture);
                    context.cmd.SetGlobalTexture(IllusionShaderProperties._PrevExposureTexture, data.PreviousExposureTexture);
                });
            }
            
            // Apply exposure pass for history reset (swap front to back)
            if (!isFixedExposure && resetHistory)
            {
                // Create intermediate texture
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                
                TextureHandle intermediateTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_ExposureIntermediateTexture", false, FilterMode.Bilinear);
                
                // First pass: blit from activeColorTexture to intermediate texture
                using (var builder = renderGraph.AddRasterRenderPass<ApplyExposurePassData>("Apply Exposure To Intermediate", 
                    out var applyPassData, new ProfilingSampler("Apply Exposure To Intermediate")))
                {
                    applyPassData.ApplyExposureMaterial = _applyExposureMaterial.Value;
                    applyPassData.Source = builder.UseTexture(renderer.activeColorTexture);
                    applyPassData.Destination = builder.UseTextureFragment(intermediateTexture, 0);
                    
                    builder.AllowPassCulling(false);
                    
                    builder.SetRenderFunc(static (ApplyExposurePassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.ApplyExposureMaterial, 0);
                    });
                }
                
                // TODO: Optimize one blit in Unity 6
                // Second pass: blit from intermediate texture back to activeColorTexture
                using (var builder = renderGraph.AddRasterRenderPass<ApplyExposurePassData>("Apply Exposure From Intermediate", 
                    out var applyPassData2, new ProfilingSampler("Apply Exposure From Intermediate")))
                {
                    applyPassData2.Source = builder.UseTexture(intermediateTexture);
                    applyPassData2.Destination = builder.UseTextureFragment(renderer.activeColorTexture, 0);
                    
                    builder.AllowPassCulling(false);
                    
                    builder.SetRenderFunc(static (ApplyExposurePassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, Blitter.GetBlitMaterial(TextureDimension.Tex2D), 0);
                    });
                }
            }
        }

        private void PreparePassDataForRenderGraph(IComputeRenderGraphBuilder builder, ExposurePassData passData, ref RenderingData renderingData, 
            UniversalRenderer renderer, bool isFixedExposure, RenderGraph renderGraph)
        {
            passData.RendererData = _rendererData;
            passData.IsFixedExposure = isFixedExposure;
            passData.ExposureCs = _exposureCS;
            passData.HistogramExposureCs = _histogramExposureCs;
            passData.ApplyExposureMaterial = _applyExposureMaterial.Value;
            passData.ResetPostProcessingHistory = _rendererData.ResetPostProcessingHistory;
            
            if (isFixedExposure)
            {
                // Fixed exposure setup
                float m_DebugExposureCompensation = 0;
                passData.ExposureParams = new Vector4(_exposure.compensation.value + m_DebugExposureCompensation, 
                    _exposure.fixedExposure.value, 0f, 0f);
                passData.ExposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, 
                    ColorUtils.s_LightMeterCalibrationConstant);
            }
            else
            {
                passData.Source = builder.UseTexture(renderer.activeColorTexture);
                
                // Histogram-based exposure setup (from OnCameraSetup)
                passData.ExposurePreparationKernel = _exposurePreparationKernel;
                passData.ExposureReductionKernel = _exposureReductionKernel;
                
                passData.ExposureVariants = _exposureVariants;
                
                // Import Texture2D resources as TextureHandle with cached RTHandle wrappers
                if (_textureMeteringMaskRTHandle == null || _textureMeteringMask == null)
                {
                    // Release old RTHandle if texture changed
                    if (_textureMeteringMaskRTHandle != null)
                    {
                        RTHandles.Release(_textureMeteringMaskRTHandle);
                        _textureMeteringMaskRTHandle = null;
                    }
                    
                    // Create new RTHandle wrapper
                    if (_textureMeteringMask != null)
                    {
                        _textureMeteringMaskRTHandle = RTHandles.Alloc(_textureMeteringMask);
                    }
                }
                
                RTHandle meteringMaskHandle = _textureMeteringMaskRTHandle ?? _rendererData.GetWhiteTextureRT();
                passData.TextureMeteringMask = builder.UseTexture(renderGraph.ImportTexture(meteringMaskHandle));
                
                passData.ProceduralMaskParams = _proceduralMaskParams;
                passData.ProceduralMaskParams2 = _proceduralMaskParams2;
                passData.ExposureParams = _exposureParams;
                passData.ExposureParams2 = _exposureParams2;
                
                // Import exposure curve texture if available with cached RTHandle wrapper
                if (_exposureCurveRTHandle == null || _exposureCurve == null)
                {
                    // Release old RTHandle if texture changed
                    if (_exposureCurveRTHandle != null)
                    {
                        RTHandles.Release(_exposureCurveRTHandle);
                        _exposureCurveRTHandle = null;
                    }
                    
                    // Create new RTHandle wrapper
                    if (_exposureCurve != null)
                    {
                        _exposureCurveRTHandle = RTHandles.Alloc(_exposureCurve);
                    }
                }
                
                RTHandle exposureCurveHandle = _exposureCurveRTHandle ?? _rendererData.GetWhiteTextureRT();
                passData.ExposureCurve = builder.UseTexture(renderGraph.ImportTexture(exposureCurveHandle));
                
                passData.HistogramExposureParams = _histogramExposureParams;
                passData.AdaptationParams = _adaptationParams;
                passData.HistogramUsesCurve = _histogramUsesCurve;
                passData.HistogramOutputDebugData = _histogramOutputDebugData;
                
                passData.HistogramBuffer = _rendererData.HistogramBuffer;
                passData.CameraWidth = renderingData.cameraData.camera.pixelWidth;
                passData.CameraHeight = renderingData.cameraData.camera.pixelHeight;
            }
        }
#endif

        public void Dispose()
        {
            CoreUtils.Destroy(_exposureCurveTexture);
            _exposureCurveTexture = null;
            _applyExposureMaterial.DestroyCache();
            
            // Release cached RTHandle wrappers
            RTHandles.Release(_textureMeteringMaskRTHandle);
            RTHandles.Release(_exposureCurveRTHandle);
            _textureMeteringMaskRTHandle = null;
            _exposureCurveRTHandle = null;
        }
    }
}