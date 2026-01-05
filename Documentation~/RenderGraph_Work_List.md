# 目标

升级IllusionRP的ScriptableRenderPass到当前Unity 2023版本中，需要适配新的API和RenderGraph。

# 注意事项

为保证2022能继续使用，你需要通过UNITY_2023_1_OR_NEWER来包裹2023的代码（例如RenderGraph的部分。

# 工作列表

该文档用于跟踪RenderGraph适配进度。

### 已适配的 Pass
以下 Pass 已完成 RenderGraph 适配：
- `DepthPyramidPass` 
- `WeightedBlendedOITPass` 
- `TransparentOverdrawPass` 
- `CopyHistoryColorPass` 
- `ForwardGBufferPass` 
- `TransparentCopyPreDepthPass` 
- `TransparentCopyPostDepthPass` 
- `TransparentDepthNormalPostPass` 
- `TransparentDepthOnlyPostPass` 
- `PreIntegratedFGDPass`

- `SetKeywordPass` 
- `ExposurePass`
- `ExposureDebugPass`
- `SetGlobalVariablesPass`
- `PostProcessingPostPass`
- `ColorPyramidPass`
- `ScreenSpaceShadowsPass`
- `ScreenSpaceShadowsPostPass`
- `GroundTruthAmbientOcclusionPass`

### 待适配的 Pass
以下 Pass 尚未适配 RenderGraph： 
- `PerObjectShadowCasterPass`
- `PerObjectShadowCasterPreviewPass`
- `DiffuseShadowDenoisePass`
- `SubsurfaceScatteringPass`
- `ContactShadowsPass`
- `ScreenSpaceReflectionPass`
- `SyncGraphicsFencePass`
- `ScreenSpaceGlobalIlluminationPass`
- `ConvolutionBloomPass`
- `VolumetricFogPass`
- `AdvancedTonemappingPass`
- `PRTRelightPass`
- `MotionVectorsDebugPass`