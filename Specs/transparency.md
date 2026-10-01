# Transparency

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md), [Water](water.md) |

IllusionRP keeps URP's transparent pass for conventional transparency and adds stages around it in the Forward and Forward+ paths: a transparent depth post pass, a pre-refraction copy of the opaque color, surface data and screen-space reflections for water-like transparent surfaces, Weighted Blended Order-Independent Transparency (OIT), and an optional overdraw that redraws conventional transparency over the OIT result.

Out of scope: the Deferred path, the Water shader and its reflection modes ([Water](water.md)), and the wet response of the Forward GBuffer ([Wet Surface Decals](wet-surface-decals.md)).

## Enablement

| Stage | Renderer feature setting | Runtime switch | Also requires |
|---|---|---|---|
| Transparent depth post pass | `transparentDepthPostPass` | none | A camera other than an Editor Preview camera. |
| Pre-refraction color | `screenSpaceRefraction` | `r.refraction` | A non-Preview camera that does not render only an offscreen depth texture. |
| Water SSR data and transparent SSR | `screenSpaceReflection` and `transparentScreenSpaceReflection` | `r.ssr` and `r.ssr.transparent` | The Screen Space Reflection volume enabled; compute shader support, `preferComputeShader` and `r.computeshader`; a graphics API other than OpenGL ES 3; a non-Preview camera that does not render only an offscreen depth texture. |
| OIT | `orderIndependentTransparency`, filtered by `oitFilterLayer` | none | Not drawn for Editor Preview cameras. |
| Overdraw | `oitTransparentOverdrawPass` with OIT on; stencil from `oitOverrideStencil` | none | A non-Preview camera. |
| Stencil VRS for the overdraw | `enableStencilVrs` | `r.vrs` | Per-image-tile shading rate support. |

- **Disabled stages.** A disabled stage records no passes, with two exceptions that keep their globals bound: the pre-refraction stage always binds `_PreRefractionColorTexture` and `_WaterPreDepthTexture`, and the transparent SSR stage binds `_SsrLightingTexture` for every non-Preview camera that does not render only an offscreen depth texture. When these stages are disabled, `_PreRefractionColorTexture` and `_SsrLightingTexture` are black.
- **Reflection cameras.** Reflection-probe cameras run the water data and transparent SSR stages but always receive the black result.
- **Debugging.** The Rendering Debugger panels and the debug views of the transparent SSR result (`r.debug.ssr.transparent`) and the stencil VRS mask (`r.debug.vrs`) are listed in [Rendering Pipeline](rendering-pipeline.md).
- **Builds.** Player builds strip the `OITTransparent`, `WaterSSRData` and `PostDepthOnly` passes when no renderer in the build enables a stage that draws them; see [Shader Variant Stripping](shader-variant-stripping.md).

## Frame order

For one camera, relative to URP's stages:

1. **After the depth prepass.** When the depth post pass or transparent SSR runs, the opaque camera depth is kept as the pre-depth and copied into the post-depth. The transparent depth post pass, when enabled, then draws.
2. **After opaque geometry, before the skybox.** The pre-refraction copy, which also binds `_WaterPreDepthTexture`; then Water SSR data; then transparent SSR.
3. **URP transparents.** Conventional transparent materials, including Water's Forward pass, draw as URP schedules them.
4. **After URP transparents.** Each when enabled: OIT accumulation and composite; the depth restore for the overdraw; stencil VRS generation; the overdraw; then the color pyramid that the next frame's SSR reads.

The full frame is defined in [Rendering Pipeline](rendering-pipeline.md).

## Transparent depth

- **Pre-depth and post-depth.** The pre-depth is the camera depth texture as the depth prepass left it: opaque and alpha-tested geometry only. The post-depth starts as a single-sample copy of it in the camera depth texture format.
- **Post pass.** The post pass draws `LightMode=PostDepthOnly` from all render queues and all layers, sorted like opaque geometry, into the post-depth and the Forward GBuffer smoothness target, both read-write. It does not write camera normals. All queues are required because opaque-queue Hair carries its fringe coverage in this pass.
- **Shader contract.** A `PostDepthOnly` pass writes depth for the coverage that later transparency stages must see, and zero to the smoothness target. A transparent-surface material that writes this pass declares `_TRANSPARENT_WRITE_DEPTH`; only such transparent materials sample screen-space occlusion, shadows, reflections and global illumination ([Materials and Shaders](materials-and-shaders.md)).
- **Publication.** After the post pass, the post-depth is the frame's camera depth texture and is bound as `_CameraDepthTexture`, and the Forward GBuffer is rebound as `_ForwardGBuffer`. Passes that pair depth with opaque normals or history keep reading the pre-depth; [Render Resources](render-resources.md) lists them. How the post pass overwrites wet smoothness is defined in [Wet Surface Decals](wet-surface-decals.md).
- **Without the post pass.** When only transparent SSR runs, the post-depth exists for water data alone and is never published as the camera depth texture. When neither runs, no pre-depth or post-depth exists.

## Pre-refraction color

- **Copy.** After opaque rendering, the active camera color is copied to `_PreRefractionColorTexture`: camera color format, single sample, no depth, no mipmaps, bilinear clamp. Refraction needs its own copy because a Forward transparent shader cannot read and write the camera color at once, and the color pyramid is built after transparents and holds the previous frame for SSR.
- **Content.** The copy is taken before URP draws the skybox and before any transparent object, so it holds opaque geometry only.
- **Disabled.** When the stage is disabled, the camera is excluded or the source is invalid, `_PreRefractionColorTexture` binds a black texture; it never keeps a previous frame. Refraction and SSR are independent: disabling one never disables the other.
- **Pre-water depth.** The same stage binds `_WaterPreDepthTexture` whether or not refraction runs: the pre-depth when it exists, otherwise the camera depth texture. Shaders that need the scene behind a water surface read it instead of `_CameraDepthTexture`, which may contain the water itself.
- **Consumers.** The Water template and shader read `_PreRefractionColorTexture` for all scene-color samples. The Legacy refraction model of the Hybrid Lit and Fabric templates still reads URP's camera opaque texture.

## Water SSR data

- **Draw.** After clearing `_WaterSSRNormalTexture` to zero, the stage draws `LightMode=WaterSSRData` from the transparent queue range and all layers, sorted as transparent geometry. It runs only when a post-depth exists; the clear runs regardless.
- **Targets.** `_WaterSSRNormalTexture` is full resolution, RGBA half float, point clamp: RGB holds the world-space reflection normal and A the reflection smoothness. A zero normal means no water. Depth is the post-depth, read-write, with depth write on and LessEqual forced regardless of the shader's own state, so each pixel keeps the nearest water surface that opaque geometry and post-depth coverage do not hide. No stencil is written.
- **Depth side effects.** With the depth post pass on, water depth becomes part of the camera depth texture that URP transparents and the overdraw see. OIT is unaffected because it tests the active camera depth.
- **Depth input.** The stage declares a read of the pre-depth. A `WaterSSRData` shader that needs the scene depth behind the water reads `_WaterPreDepthTexture`, never `_CameraDepthTexture`, which may be the post-depth this stage is writing.
- **Geometry.** A `WaterSSRData` pass reproduces its Forward pass's vertex displacement, culling and alpha coverage, so reflection data covers exactly the pixels the Forward pass draws. A pass with nothing to contribute discards.

## Transparent screen-space reflection

- **Inputs.** The ray origin depth comes from the post-depth, so rays start at the water surface. Normal and smoothness come from `_WaterSSRNormalTexture`, with perceptual roughness `1 - smoothness`. Hits are tested against the opaque depth pyramid, and hit colors are reprojected from the previous frame's color pyramid with motion vectors.
- **Mask.** Pixels with a zero water normal produce nothing; the camera stencil is not consulted.
- **Algorithm.** Tracing is full resolution and always uses the compute path, whatever the volume's tracing mode and downsample setting. It never accumulates or uses async compute, and it never reflects the sky. Steps, step size, thickness, roughness fade, edge fade and intensity come from the Screen Space Reflection volume; there is no separate water volume.
- **Output.** `_SsrLightingTexture`, RGBA half float, holds pre-exposed color premultiplied by a confidence stored in A. Misses, off-screen reprojection and negative or non-finite history yield transparent black. Consumers divide by the confidence to get a straight color and multiply by the inverse current exposure multiplier.
- **Fallback.** When the stage is disabled or an input is missing (no previous-frame color, water data, depth pyramid, post-depth or compute path), `_SsrLightingTexture` binds a black texture. Disabling transparent SSR never disables pre-refraction.
- **Global binding.** From this stage on, `_SsrLightingTexture` holds the transparent result instead of the opaque SSR result, so transparent materials drawn afterwards receive no opaque SSR through it.

## Weighted blended OIT

- **Accumulation.** The accumulation pass draws `LightMode=OIT` from all render queues, filtered by `oitFilterLayer` and sorted like opaque geometry; the result does not depend on draw order. It tests LessEqual against the active camera depth without writing it. Its targets are a camera-sized, single-sample RGBA half-float accumulation cleared to 0 and a single-channel half-float revealage cleared to 1.
- **Shader contract.** OIT passes are named `OITTransparent` with `LightMode=OIT`; the build stripper recognizes them by both. They blend `One One` into target 0 and `Zero OneMinusSrcAlpha` into target 1, with depth write off, and weight with the shared `OITWeight` shader library function.
- **Composite.** The composite reads accumulation and revealage as input attachments, writes the active camera color with the camera depth bound read-only, and blends `OneMinusSrcAlpha SrcAlpha`.

```text
a'  = max(min(1, max(c.r, c.g, c.b) * a), a)
w   = a' * clamp(0.03 / (1e-5 + (z / (2000 * n))^4), 0.01, 3000)
accumulation += (c * w, w)
revealage    *= 1 - a
C   = accumulation.rgb * E / clamp(accumulation.a, 1e-4, 5e4)
dst = C * (1 - revealage) + dst * revealage
```

Here `c` is the shader's lit and fogged color divided by the current exposure multiplier `E`, `a` its alpha, `z` its positive view depth and `n` the camera near plane.

- **One ABI.** The accumulation output, the revealage meaning, the exposure handling and the blend states change together or not at all.
- **Ordering.** OIT runs after URP transparents. Conventional transparency always lies beneath the OIT result unless the overdraw redraws it; there is no exact ordering between the two.
- **Disabled.** With OIT off, no OIT targets or passes are recorded, and anything drawn only through OIT disappears: transparent Hybrid Lit and Hybrid Complex Lit materials with `_OrderIndependent` on, which have no other color pass ([Materials and Shaders](materials-and-shaders.md)), and the Hair fringe. Hair keeps its core.
- **Accuracy.** Weighted blending is an approximation and loses accuracy as alpha approaches 1.

## Hair fringe

Multi Pass Hair, such as HD Hair, splits into a core and a fringe as [Materials and Shaders](materials-and-shaders.md) defines. For transparency:

- **Matching coverage.** The fringe's `OIT` and `PostDepthOnly` passes clip at the same `_TransparentAlphaCutoff`, so the depth the overdraw restores matches the OIT coverage.
- **All queues.** Hair materials normally sit in the opaque queue, so the OIT and post-depth renderer lists keep all queues; restricting either to the transparent range drops the fringe.

## Overdraw

- **Depth restore.** The frame's camera depth texture, which is the post-depth when the post pass ran, is copied into the active camera depth, which keeps it for the rest of the frame.
- **Redraw.** The overdraw then draws the transparent queue range from all layers with `SRPDefaultUnlit`, `UniversalForward` and `UniversalForwardOnly`, sorted back to front, testing LessEqual without writing depth. Objects without a matching pass draw with the error shader. Conventional transparency in front of OIT coverage is thus redrawn over the OIT result.
- **Stencil.** When `oitOverrideStencil` has its override on, the overdraw uses its reference, read mask, comparison and pass, fail and depth-fail operations. These values are project configuration, interpreted against the stencil layout in [Materials and Shaders](materials-and-shaders.md), not a fixed classification; changing them requires re-validating the restored depth, VRS and material stencil writes.
- **VRS.** When stencil VRS runs, the overdraw shades at a rate derived from the camera stencil: full rate where it marks SSR receivers or subsurface scattering, 2x2 where it marks only hair or skin, and 4x4 elsewhere. The overdraw is the only consumer of this rate image.
- **Approximation.** The overdraw mitigates OIT bleed-through; it is not a transparency sorter. Every pixel that passes its depth and stencil tests is blended a second time. Without the depth post pass the restored depth carries no transparent coverage, so all conventional transparency in front of opaque geometry is blended twice. High alpha, refraction, intersecting media and exact order across owners are not guaranteed.

## Limitations

- **Refraction content.** `_PreRefractionColorTexture` contains neither the skybox nor transparent objects drawn earlier in the frame.
- **History.** Transparent SSR reprojects the previous frame's color, so fast camera or object motion and disocclusion show history artifacts.
- **Hits.** Transparent SSR only reflects on-screen content present in the opaque depth pyramid. Transparent objects are never hit surfaces, although they may appear in the history color.
- **One water layer.** Only the nearest water surface per pixel has SSR data; a water surface seen through another samples the nearer surface's reflection.

## Validation

- Opaque-queue Hair still appears in the OIT accumulation and transparent post depth renderer lists, and both OIT targets receive its coverage.
- The composite attachments, exposure handling and blend state match this spec.
- With OIT off, no OIT targets or passes are recorded and conventional transparency keeps URP behavior.
- With the depth post pass and transparent SSR off, no pre-depth, post-depth or `PostDepthOnly` draw is recorded.
- `_PreRefractionColorTexture` is the current frame's opaque color without water, and black when refraction is off while reflections still work.
- Water SSR data writes the post-depth and `_WaterSSRNormalTexture` and records a read of the pre-depth.
- With SSR off, `_SsrLightingTexture` is black and refraction still works.
- Water, conventional transparency, Hair core and fringe, and the overdraw draw in the order above.
