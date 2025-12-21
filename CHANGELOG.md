# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

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
