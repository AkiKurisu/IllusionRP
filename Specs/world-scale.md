# World Scale

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |

World Scale is the single renderer-level conversion between the logical world lengths that IllusionRP settings are authored in and Unity world units. A project whose content runs at a non-standard size keeps its Volume profiles, renderer settings and diffusion profiles in logical units, and every pass consumes Unity-space values.

## Setting

- **Meaning.** `World Scale`, in the General section of `IllusionRendererFeature`, is the number of Unity world units represented by one logical world unit. The default is 1, at which every converted value equals its logical value.
- **Range.** The inspector's minimum is 0.001, and the renderer clamps the value to that minimum when reading it.
- **Lifecycle.** The renderer reads the setting at every camera setup, so a change applies from the next camera rendered.
- **Reach.** World Scale changes no transform, camera, collider, Volume bound, light or asset. It acts only where IllusionRP consumes a logical distance.

## Conversion boundary

Volume profiles, renderer settings and component values hold logical values. After the Volume stack has blended them, a pass converts each value exactly once, where it uploads CPU parameters or shader constants.

| Quantity | Conversion |
|---|---|
| Length | Multiplied by World Scale. |
| Squared length | Multiplied by World Scale squared. |
| Inverse length | Divided by World Scale. |
| Meters per unit | Divided by World Scale. |
| Normalized, relative, screen-space and texel values | Unchanged. |
| Radiance, exposure, color, density | Unchanged, unless an integral is compensated as stated below. |
| Counts, modes, masks, angles, time | Unchanged. |

- **Unity-space inputs.** Values obtained from transforms, camera depth, reconstructed world positions, culling or other Unity APIs are already in Unity units and are never converted again. This includes biases derived from a shadow projection and its texel size, and offsets derived from camera-relative vectors.
- **Internal constants.** Fixed metric constants that a pass compares with Unity-space depth or positions (trace distances, depth and reprojection thresholds, distance breakpoints, epsilons) are logical values and follow the same table.
- **Shader access.** Each camera publishes `_WorldScaleParams` in the IllusionRP global constant buffer: x is World Scale, y its inverse, z its square and w its inverse square. A shader that compares an authored logical distance with Unity-space depth or positions multiplies it by the matching component where it uses it, and never in addition to a CPU-side conversion of the same value.

## Converted values

- **Screen space reflection.** `stepSize` is a length. The speed rejection threshold derived from `speedRejectionScalerFactor` is compared with a world-space motion length and is converted as a length. The linear tracer's fixed trace distance, depth thickness and near-plane offset are lengths. `thickness`, `screenFadeDistance`, `steps`, the smoothness fades and accumulation are unchanged.
- **Screen space global illumination.** No Volume parameter is converted: `depthBufferThickness` is depth-relative and `maxRaySteps` and `denoiserRadius` are a count and a multiplier. The diffuse denoiser's distance breakpoint, depth rejection threshold and center-sample radius, and the temporal filter's reprojection distance limit, are lengths. The ray origin offset is relative to the camera vector and is unchanged.
- **Ground truth ambient occlusion.** `radius` is a length, the squared-distance epsilon of the horizon search is a squared length, and `spatialBilateralAggressiveness`, which multiplies a linear eye-depth difference, is an inverse length. `thickness`, `intensity`, `directLightingStrength`, `blurSharpness`, `stepCount`, `directionCount` and `maximumRadiusInPixels` are unchanged.
- **Contact shadows.** `length`, `maxDistance`, `minDistance`, `fadeDistance` and `fadeInDistance` are lengths. `distanceScaleFactor` is an inverse length, so its product with the ray length is invariant. `rayBias`, `thicknessScale`, `sampleCount` and `filterSizeTraced` are unchanged.
- **Percentage closer soft shadows.** `maxPenumbraSize`, `maxSamplingDistance`, `penumbraMaskDilationFadeStart` and `penumbraMaskDilationFadeEnd` are lengths, for the main light and for per-object shadows alike. Angular diameters, `minFilterSizeTexels`, sample counts and the penumbra mask scale and dilation are unchanged.
- **Per-object shadows.** `perObjectShadowLengthOffset` is a length; see [Directional Per-Object Shadows](directional-per-object-shadows.md).
- **Volumetric fog.** `distance`, `baseHeight`, `maximumHeight`, `groundHeight` and `attenuationDistance` are lengths, and the `VolumetricAdditionalLight` radius is a length converted before it is squared. `density`, `anisotropy`, `scattering`, `tint`, the contribution weights, `maxSteps`, `blurIterations` and `transmittanceThreshold` are unchanged. Absorption is derived from the converted attenuation distance, and each step's in-scattering is weighted by the step length divided by World Scale, so transmittance and in-scattered energy along a logical path do not depend on World Scale and `density` keeps its meaning.
- **Subsurface scattering.** A diffusion profile's `worldScale` is the size of one logical unit in meters. The shaders receive the profile value divided by World Scale, the meters per Unity unit. Scattering distance, thickness remap and filter radius are in millimeters and are not converted. The conversion happens in the per-frame shader data; profile assets and caches are never modified.
- **Wet surface decals.** The decal world projection scale is applied once as a squared distance scale; see [Wet Surface Decals](wet-surface-decals.md).

## Not converted

World Scale leaves these values unchanged:

- Light component data: range, intensity, shadow bias and shadow near plane, and the area light shape, fade and shadow settings.
- Camera near and far planes, URP shadow distance, cascades and shadow bias.
- `LODGroup` screen-relative transitions, LOD Bias and Maximum LOD Level.
- Rotations, angles, time, sample, iteration and bounce counts, resolutions and pixel radii.
- Normalized thickness, smoothness, anisotropy and screen-space radii.
- URP Depth of Field.
- Precomputed radiance transfer probe grids, biases and adjustment volumes.
- Baked meshes, probes, lightmaps and any other data that requires re-baking.
- Screen-space post-processing parameters, including all sun shaft parameters.
