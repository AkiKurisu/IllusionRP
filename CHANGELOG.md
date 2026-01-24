# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.2.1] - 2026-1-24

### Fixed

- Fix prt probe incorrect debug effects after enabling MultiRelight.
- Fix HD Skin missing DepthNormals Pass.
- Fix StencilVRSGenerationPass build exception.
- Fix Vulkan shader compilation error in UnpackNormal method.
- Fix AdaptiveProbeVolume support for SSGI.
- Fix material compilation error when enable AdaptiveProbeVolume.

## [1.2.0] - 2026-1-21

This version is compatible with Unity 6. Old version for Unity 2022 and Unity 2023 has been moved to [urp14 branch](https://github.com/AkiKurisu/IllusionRP/tree/urp14).

### Changed

- Update all passes to Unity 6.3 and URP 17.
- Remove UNITY_2023_1_OR_NEWER macro.
- Remove Graphics Fence pass.
- Remove ComputeConstantBuffer utility.
- Remove SetGlobalVariablesPass.
- Remove Native Render Pass debug option.
- Remove requireEarlyMotionVector option.
- Disable Async Compute by default (Known issues).
- Remove the second blit in VolumetricFogPass.
- Remove the second blit in ExposurePass.
- Make all ASE shaders to compatiable to URP 17.

### Added

- Add VRS (Variable Rate Shading) support with StencilVRSGenerationPass and StencilVRSDebugPass.
- Add IllusionTransformWorldToShadowCoord override.

### Fixed

- Fix CameraPreDepth format.
- Fix SubsurfaceScatteringPass.cs.
- Fix GTAO half resolution bug.
- Fix SSGI half resolution bug.
- Fix preview camera null exception bug.
- Fix depth copy bug.
- Fix RenderTexture null exception in PRTProbeVolume.
- Fix Subsurface Scattering Clear Color bug.
- Fix Transparency depth bug.
- Fix SetGlobalVariablesPass GfxDeviceD3D12::SetComputeBufferData error.
- Fix subsurface scattering not work.

## [1.1.6] - 2026-1-11

This version is compatible with Unity 2022.3.62f1 and 2023.2.22f1.

### Compatibility

Following features are now compatible with RenderGraph.

- `ScreenSpaceGlobalIlluminationPass`
- `VolumetricFogPass`

All features are now compatible with Unity 2023's RenderGraph, but there are still many issues with the RenderGraph in the 2023 URP version, so its use is not recommended at this time.

### Fixed

- Fix ForwardGBuffer Pass crash when using RenderGraph in Unity 2023.
- Fix PRTProbeVolume null exception bug.
- Fix ConvolutionBloom null exception bug when using RenderGraph in Unity 2023.
- Fix ScreenSpaceShadowsPass not use predepth texture when using RenderGraph in Unity 2023.
- Fix SubsurfaceScatteringPass exception when volume is disabled when using RenderGraph in Unity 2023.

## [1.1.5] - 2026-1-10

This version is compatible with Unity 2022.3.62f1 and 2023.2.22f1.

### Compatibility

Following features are now compatible with RenderGraph.

- `SubsurfaceScatteringPass`
- `DiffuseShadowDenoisePass`
- `ContactShadowsPass`
- `SyncGraphicsFencePass` (No need when using RenderGraph)

### Fixed

- Fix GTAO scene view bug when using RenderGraph in Unity 2023.
- Fix SSR scene view bug when using RenderGraph in Unity 2023.
- Fix ConvolutionBloomPass blend result incorrect bug when using RenderGraph in Unity 2023.
- Fix copy depth and transparency bug when using RenderGraph in Unity 2023.

## [1.1.4] - 2026-1-8

This version is compatible with Unity 2022.3.62f1 and 2023.2.22f1.

### Compatibility

Following features are now compatible with RenderGraph.

- `AdvancedTonemappingPass`
- `MotionVectorsDebugPass`
- `PRTRelightPass`
- `ScreenSpaceReflectionPass`
- `ConvolutionBloomPass`

### Fixed

- Fix GroundTruthAmbientOcclusionPass missing CameraNormalTexture.
- Fix Motion Vectors Debug Pass for Unity 2023.1 compatibility.
- Fix Apply Exposure not work for Unity 2023.1 compatibility.

## [1.1.3] - 2026-1-6

This version is compatible with Unity 2022.3.62f1 and 2023.2.22f1.

### Compatibility

Following features are now compatible with RenderGraph.

- `SetKeywordPass` 
- `ExposurePass`
- `ExposureDebugPass`
- `SetGlobalVariablesPass`
- `PostProcessingPostPass`
- `ColorPyramidPass`
- `ScreenSpaceShadowsPass`
- `ScreenSpaceShadowsPostPass`
- `GroundTruthAmbientOcclusionPass`
- `PerObjectShadowCasterPass`
- `PerObjectShadowCasterPreviewPass`

### Fixed

- Fix PreIntegratedFGD performance bug introduced from 1.1.2.

## [1.1.2] - 2026-1-3

This version is compatible with Unity 2022.3.62f1.

### Added

- Allow use metallic port in Skin Template.

### Compatibility

Following features are now compatible with RenderGraph.

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

### Changed

- Remove unused lighting functions.
- Refactor renderer setup into dedicated SetupPass.
- Refactor DepthPyramidPass to use strongly-typed pass data and ComputePass APIs for Unity 2023.1 or newer. 
- Update shader and C# code to conditionally include RTHandleScale and related clamping functions based on Unity version. 
- Change LitOITPassFragment to return an OITFragmentOutput struct instead of using out parameters. 
- Refactor OIT pass for RenderGraph and data structure.
- Refactor depth and normal texture handling in transparency passes.
- Refactor PreIntegratedFGD to use RTHandle and RenderGraph.

### Fixed

- Fix prt bake validity execption.

## [1.1.1] - 2025-12-27

This version is compatible with Unity 2022.3.62f1.

### Added
- Add Enable Relight Shadow.
- Add PRT Per-Probe Invalidate.
- Add PRT Per-Probe Intensity.

### Changed
- Remove HDFabric ASE transparency and transmission effect.
- Disable SSGI when use Lightmap.

### Fixed
- Fix PRTProbeVolume relight may miss probe.
- Fix PRTGI toggle not work in debugger.

## [1.1.0] - 2025-12-21

This version is compatible with Unity 2022.3.62f1.

### Added
- Add MicroShadows.
- Add Diffuse_GGX_Rough model from Unreal 5.
- Add Multi Scattering options for Hair Template.
- Add diffuse model options for Skin Template.

### Changed
- Remove _USE_LIGHT_FACING_NORMAL macro.
- Remove HAIR_PERFORMANCE_HIGH macro.
- Skin shading model now calculate low frequency normal for diffuse GI.
- Remove PixelSetAsNoMotionVectors.

### Fixed
- Fix marschner hair float precision.
- Fix KajiyaKayDiffuseAttenuation use wrong input, replace N with Tangent.
- Fix missing ForwardGBuffer pass of hair.
- Fix NullReferenceException when IllusionRendererFeature is first added to the renderer asset.
- Fix incorrect use of half for lighting attenuation.
- Fix TemporalFilter historyUV.

## [1.0.0] - 2025-12-06

First release.

This version is compatible with Unity 2022.3.62f1.
