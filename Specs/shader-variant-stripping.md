# Shader Variant Stripping

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Rendering Pipeline](rendering-pipeline.md), [Validation](validation.md) |

Shader variant stripping removes IllusionRP keyword variants and IllusionRP-only passes that no renderer of the build target can reach. Reachability is aggregated from every URP asset, renderer and Illusion renderer feature the target uses; a variant or pass is removed only when every target renderer can do without it.

Out of scope: compute shader kernels and keywords, resources, analysis of scene, camera or Volume content, inference from the current editor scene or the local machine's capabilities, and URP's own stripping.

## Switch

`IllusionRenderPipelineSettings.stripUnusedVariants`, shown as Strip Unused Variants under Project Settings, Graphics, IllusionRP Global Settings, is the master switch and is on by default. When it is off, the keyword and pass rules remove nothing and, with valid build data, the dynamic prefilter is reset to keep every IllusionRP keyword state. The static keyword filters apply either way.

## Build stages

| Stage | Responsibility |
|---|---|
| Capability gathering | Collects the URP assets of the build target and computes one capability set per renderer, when the build starts and before Unity enumerates variants. |
| Keyword prefiltering | Writes the aggregated result into derived prefilter fields of each Illusion renderer feature, so Unity enumerates only the keyword states the target needs. |
| Keyword stripping | Evaluates the keyword rules per renderer and removes a variant only when every renderer allows it. |
| Pass stripping | After SRP Core and URP have processed a pass, removes every variant of a registered IllusionRP pass that no renderer reaches. |

## Capabilities

- **Sources.** The build target's own URP assets, never the current quality level, open scenes or `SystemInfo`.
- **Per renderer.** Each renderer in each asset's renderer list contributes one capability set. A renderer with an active Illusion renderer feature contributes the capabilities its serialized settings enable; a renderer whose feature is inactive or missing contributes none.
- **Invalid data.** The build data is invalid when the target has no URP asset, an asset is null or has no renderer list, a renderer or its feature list is null, a renderer has more than one Illusion renderer feature, no renderer is found, or gathering throws. With invalid data, nothing is stripped and the derived prefilter fields are not updated.
- **Prefilter cache.** The derived prefilter fields are a build input cache, not runtime quality settings. When a value changes, the renderer feature asset is saved so that variant enumeration reads this target's result.

| Capability | Renderer feature setting |
|---|---|
| Screen-space main light shadow | Always, for an active feature. |
| Screen-space reflection | `screenSpaceReflection` |
| Transparent screen-space reflection | `screenSpaceReflection` and `transparentScreenSpaceReflection` |
| Screen-space global illumination | `screenSpaceGlobalIllumination` |
| Screen-space ambient occlusion | `groundTruthAO` |
| PRT global illumination | `precomputedRadianceTransferGI` |
| Screen-space subsurface scattering | `subsurfaceScattering` |
| Order-independent transparency | `orderIndependentTransparency` |
| Transparent overdraw | `orderIndependentTransparency` and `oitTransparentOverdrawPass` |
| Transparent depth post pass | `transparentDepthPostPass` |
| Transparent per-object shadows | `transparentReceivePerObjectShadows` |
| Fragment shadow bias | `fragmentShadowBias` |
| Contact shadows | `contactShadows` |
| PCSS | `pcssShadows` |
| Area lights, with the Medium or High tier | `areaLights`, with `areaShadowFilteringQuality` |

## Keyword prefiltering

"Some" means at least one renderer has the capability and at least one does not.

| Keyword | No renderer | Some renderers | Every renderer |
|---|---|---|---|
| `_SCREEN_SPACE_SSS`, `_SCREEN_SPACE_REFLECTION`, `_SCREEN_SPACE_GLOBAL_ILLUMINATION`, `_PRT_GLOBAL_ILLUMINATION`, `_TRANSPARENT_PER_OBJECT_SHADOWS`, `_SHADOW_BIAS_FRAGMENT` | Remove | Keep on and off | Keep on only |
| `_CONTACT_SHADOWS`, `_PCSS_SHADOWS` | Remove | Keep on and off | Keep on and off |

The area shadow axis `_`, `AREA_SHADOW_MEDIUM`, `AREA_SHADOW_HIGH` is one keyword set, prefiltered to an exact subset: the off state is kept only when some renderer has no area lights, and each tier only when some renderer selected it. On the Forward and Forward+ paths a renderer with area lights never needs the off state, because the selected tier stays enabled and the Volume, the runtime switch and empty frames set `_AreaLightCount` to zero instead.

Static filters apply regardless of the switch:

- `_MAIN_LIGHT_SHADOWS_SCREEN`, `_SCREEN_SPACE_OCCLUSION` and `_SOURCE_DEPTH_NORMALS` are selected with override priority, so URP's own prefiltering does not remove them; the keyword rules below then decide the variants of the first two.
- Both states of `_ILLUSION_RENDER_PASS_ENABLED` are kept.
- `_DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT` and `_DEBUG_SCREEN_SPACE_SHADOW_CONTACT` are always removed.

## Keyword rules

A variant is removable for a renderer when any rule below removes it for that renderer, and it is removed only when it is removable for every target renderer. The global keywords follow the renderer feature settings rather than Volume parameters or runtime switches, as [Rendering Pipeline](rendering-pipeline.md#global-state) defines, so a state the settings never select is unreachable.

| Keyword | Renderer without the capability | Renderer with the capability |
|---|---|---|
| `_SCREEN_SPACE_REFLECTION` | Remove on. | Remove on in transparent variants; keep on and off otherwise. |
| `_SCREEN_SPACE_OCCLUSION` | Remove on. | Remove on in transparent variants; keep on and off otherwise. |
| `_MAIN_LIGHT_SHADOWS_SCREEN` | Remove on. | Remove on in transparent variants; keep on and off otherwise. |
| `_SCREEN_SPACE_GLOBAL_ILLUMINATION`, `_PRT_GLOBAL_ILLUMINATION`, `_TRANSPARENT_PER_OBJECT_SHADOWS`, `_SHADOW_BIAS_FRAGMENT` | Remove on. | Remove off in passes that declare the keyword. |
| `AREA_SHADOW_MEDIUM`, `AREA_SHADOW_HIGH` | Remove both tiers. | Remove the tier the renderer did not select, and the off state in passes that declare the axis. |

- **Transparent variants.** A transparent variant is one with the `_SURFACE_TYPE_TRANSPARENT` keyword enabled. Such a surface does not sample screen-space reflections, ambient occlusion or the screen-space main light shadow unless it writes post-depth ([Materials and Shaders](materials-and-shaders.md#screen-space-receivers)), so a shader that declares `_SURFACE_TYPE_TRANSPARENT` as a keyword never defines `_TRANSPARENT_WRITE_DEPTH`. Shaders that define `_SURFACE_TYPE_TRANSPARENT` as a constant instead of declaring it as a keyword are treated as opaque by these rules.
- **Declared keywords only.** The rules depend on keywords, not on shader identity, and act only on keywords the pass declares; a variant that no rule matches is kept.

## Pass stripping

A pass is a candidate only when its SubShader is tagged `RenderPipeline=UniversalPipeline` and both its name and LightMode match a registered entry. Every variant of a candidate pass is removed when no target renderer reaches it.

| Pass name | LightMode | Kept when any target renderer has |
|---|---|---|
| `OITTransparent` | `OIT` | Order-independent transparency. |
| `SubsurfaceDiffuse` | `SubsurfaceDiffuse` | Screen-space subsurface scattering. |
| `WaterSSRData` | `WaterSSRData` | Screen-space reflection and transparent screen-space reflection on the same renderer. |
| `PostDepthOnly` | `PostDepthOnly` | The transparent depth post pass, or order-independent transparency with transparent overdraw. |

- **Conservative default.** An unregistered pass, a pass whose metadata cannot be read, or one whose RenderPipeline tag, pass name or LightMode is missing or different, is kept.
- **New passes.** A new IllusionRP-only pass gets a stable pass name and LightMode constant and a runtime renderer list consumer before it is added to this table. Stripping never infers a pass's meaning from shader names, GUIDs, folders or source text.

## Shader authoring

Templates and shaders whose passes take part in stripping:

- tag the SubShader with `RenderPipeline=UniversalPipeline`;
- use a stable, unique pass name;
- use a LightMode equal to the runtime `ShaderTagId`;
- use the pass and keyword names that `IllusionShaderPasses` and `IllusionShaderKeywords` define;
- export generated shaders again after a template change, as [ASE Shader Workflow](ase-shader-workflow.md) requires, so the contract survives the export.

## Validation

- With the switch off, or with invalid build data, the keyword and pass rules remove nothing.
- Aggregation is correct for active, inactive, missing and duplicate Illusion renderer features.
- A build with several renderers keeps every variant and pass that any renderer reaches.
- Transparent variants keep no on state of screen-space reflections, ambient occlusion or screen-space main light shadows.
- `OITTransparent`, `SubsurfaceDiffuse`, `WaterSSRData` and `PostDepthOnly` are removed only when no target renderer reaches them.
- The target Player shows no missing variant, pink shader or functional fallback.
- A change to the keyword or pass rules is validated by comparing Player builds with stripping on and off, as [Validation](validation.md) requires.
