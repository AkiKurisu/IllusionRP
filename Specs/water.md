# Water

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Transparency](transparency.md), [Materials and Shaders](materials-and-shaders.md), [Path Tracing](path-tracing.md) |

IllusionRP ships a transparent water shader, `Universal Render Pipeline/Water`, and the Amplify Shader Editor template it is built from, `Hidden/Universal/Water`. Water refracts the pre-refraction color, reflects through the pipeline's transparent screen-space reflections with an environment fallback, and can switch per material to a legacy in-shader raymarch. The pipeline stages Water consumes, and the contracts any third-party water shader must meet to use them, are defined in [Transparency](transparency.md).

## Template

- **Surface.** A Transparent surface includes the `WaterSSRData` pass; an Opaque surface excludes it.
- **Water data.** The template's `WaterSSRData` pass shares the graph's vertex stage, writes the graph's Normal port transformed to world space from the selected normal space, and writes a reflection smoothness of 1. With Alpha Clipping on, it clips by the graph's Alpha and Alpha Clip Threshold; it honors LOD cross-fade. It is not compiled for OpenGL ES 3 or OpenGL Core.
- **Scene color.** In every pass, graph scene-color samples read `_PreRefractionColorTexture` through the foreground rejection below, never URP's camera opaque texture.
- **Scene depth.** In the Forward pass, graph scene-depth samples read `_WaterPreDepthTexture` in Screen Space mode and the camera depth texture in Legacy mode. Other passes keep URP's camera depth texture.
- **Environment.** In the Forward pass in Screen Space mode, graph reflection-probe samples return the pipeline's environment reflection (reflection probes or sky) with its probe normalization.

Lighting is defined in [Materials and Shaders](materials-and-shaders.md); template editing and regeneration follow [ASE Shader Workflow](ase-shader-workflow.md).

## Reflection modes

The material property `_WaterReflectionMode`, shown as Water Reflection Mode, selects the mode:

| Mode | Value | Keyword | `WaterSSRData` pass | Reflection source |
|---|---|---|---|---|
| Screen Space (default) | 0 | none | Enabled | Transparent SSR with an environment fallback. |
| Legacy | 1 | `_WATER_REFLECTION_LEGACY` | Disabled; its fragment discards if drawn | In-shader raymarch over the camera depth texture and the previous frame's color. |

- **Consistency.** The material inspector keeps the keyword and the pass state consistent with the property. Code that changes the property must set both.
- **Independence.** Modes can mix in one scene. A material's mode never schedules or stops pipeline stages.
- **Refraction.** Both modes refract `_PreRefractionColorTexture`.

## Screen Space mode

The Forward pass resolves its reflection from the transparent SSR result at the pixel:

```text
s    = _SsrLightingTexture at the pixel
conf = saturate(s.a)
ssr  = conf > 1e-4 ? s.rgb * (1 / E) / conf : 0
reflection = lerp(environment, ssr, conf)
```

Here `E` is the current exposure multiplier. A sample outside the screen or with non-finite values has zero confidence. A miss therefore shows the environment, never black, as long as a reflection probe or sky exists. Where transparent SSR is unavailable, every pixel uses the environment.

## Legacy mode

- **Trace.** The graph raymarches the reflection ray in view space against the camera depth texture.
- **Hit color.** A hit reads the previous frame's color, `_HistoryColorTexture` (black when no history exists), reprojected with motion vectors. A reprojection outside the screen is not a hit.
- **Sky.** Pixels without valid scene depth act as a virtual sky plane at a fixed view depth, so results do not depend on the camera far plane.
- **Accuracy.** The history color can contain transparent objects while hits are tested against depth behind them, so occlusion and parallax are not correct.

## Refraction

The package's Water shader include provides the sampling functions the template uses, which third-party water shaders may reuse:

- **`SamplePreRefractionColor`.** Returns black for UVs outside the screen and for non-finite samples.
- **`SamplePreRefractionColorDistorted`.** Takes the undistorted UV, the distorted UV and the surface's eye depth. It falls back to the undistorted UV when the distorted UV leaves the screen, or when the scene at the distorted UV is at or in front of the water surface, so foreground objects never appear in the refraction.
- **`SampleWaterSceneDepth`.** Returns the depth behind the water: `_WaterPreDepthTexture` in Screen Space mode, the camera depth texture in Legacy mode. Refraction, shore and thickness terms never read the post-depth, which contains the water itself.
- **`SampleTransparentScreenSpaceReflection`.** Returns the straight transparent SSR color and its confidence as resolved above.
- **Screen UVs.** These functions apply the stereo eye transform and the RTHandle scale to normalized screen UVs.

## Shipped Water shader

- **Render state.** The shader draws in the Transparent queue and casts no shadows. Its `UniversalForwardOnly` Forward pass blends `SrcAlpha OneMinusSrcAlpha`, culls back faces and writes depth, so later transparent draws and OIT test against the water surface. It targets Shader Model 3.5 and supports GPU instancing and LOD cross-fade.
- **Surface.** Two scrolling normal layers, projected on world XZ and blended by a blend map, drive refraction and reflection. The refracted color is tinted between shallow and deep colors by water thickness, and the refraction offset fades with the distance of the scene behind the water. The final color is written as emission, a Fresnel blend `lerp(body, reflection, 0.01 + 0.99 (1 - N.V)^5)`; the base color is black.
- **Alpha.** Alpha fades the shoreline only: `saturate((sceneEyeDepth - surfaceEyeDepth) / _EdgeFade)`, clipped below 0.5. Elsewhere the surface is see-through because the refracted background is already in its color. A shader that composites the background into its color must not also lower alpha for the same transparency, or the camera color is blended over that background a second time.

## Path tracing

The template and the shipped shader provide a [path tracing](path-tracing.md) material entry point. As in UE's path tracer, path traced water is a refractive Lit interface over an absorbing medium, not a dedicated water model:

| Property | Display name | Meaning |
|---|---|---|
| `_Ior` | IOR | Index of refraction of the interface, 1.333 by default |
| `_PathTracingSmoothness` | Smoothness | Smoothness of the interface, 0.95 by default |
| `_TransmittanceColor` | Absorption Color | Color that light keeps after crossing Absorption Distance of water |
| `_TransmittanceDistance` | Absorption Distance | Distance at which light keeps Absorption Color |

- **Interface.** The interface takes the graph's Normal port, as raster does. Reflection and refraction follow Fresnel at the IOR; the interface has no diffuse or emissive response.
- **Medium.** Water absorbs and does not scatter. Light crossing distance `d` keeps `_TransmittanceColor` raised to `d / _TransmittanceDistance`. A path that leaves the scene inside the water returns no light.
- **Shadows.** Shadow paths cross the interface and the medium without attenuation.
- **Raster terms.** The pre-refraction color, depth and thickness tint, shoreline fade, reflection modes and Fresnel blend have no path tracing counterpart; the shoreline is where the water geometry meets the scene.
