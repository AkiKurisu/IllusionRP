using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        public void Dispose()
        {
            CoreUtils.Destroy(_exposureCurveTexture);
            _exposureCurveTexture = null;
            _applyExposureMaterial.DestroyCache();
        }
    }
}