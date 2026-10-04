# Shader Variant Stripping

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Rendering Pipeline](rendering-pipeline.md) |

Shader variant stripping removes IllusionRP keyword variants and IllusionRP-only passes that no renderer of the build target can reach. Reachability is aggregated from every URP asset, renderer and Illusion renderer feature the target uses; a variant or pass is removed only when every target renderer can do without it.

## Switch

`IllusionRenderPipelineSettings.stripUnusedVariants`, shown as Strip Unused Variants under Project Settings, Graphics, IllusionRP Global Settings, is the master switch and is on by default. When it is off, the keyword and pass rules remove nothing and, with valid build data, the dynamic prefilter is reset to keep every IllusionRP keyword state. The static keyword filters apply either way.

## Build stages

| Stage | Responsibility |
|---|---|
| Capability gathering | Collects the URP assets of the build target and computes one capability set per renderer, when the build starts and before Unity enumerates variants. |
| Keyword prefiltering | Writes the aggregated result into derived prefilter fields of each Illusion renderer feature, so Unity enumerates only the keyword states the target needs. |
| Keyword stripping | Evaluates the keyword rules per renderer and removes a variant only when every renderer allows it. |
| Supplemental URP axis filtering | After SRP Core and URP callbacks, removes SH/atlas combinations no target renderer selects. |
| Pass stripping | After SRP Core and URP have processed a pass, removes every variant of a registered IllusionRP pass that no renderer reaches. |

## Capabilities

- **Sources.** The URP assets selected for the build target.
- **Per renderer.** Each renderer in each asset's renderer list contributes one capability set. A renderer with an active Illusion renderer feature contributes the capabilities its serialized settings enable; a renderer whose feature is inactive or missing contributes none.
- **Invalid data.** The build data is invalid when the target has no URP asset, an asset is null or has no renderer list, a renderer or its feature list is null, a renderer has more than one Illusion renderer feature, no renderer is found, or gathering throws. With invalid data, nothing is stripped and the derived prefilter fields are not updated.
- **Prefilter cache.** The derived fields cache the target's prefilter result. When a value changes, the renderer feature asset is saved so that variant enumeration reads that result.

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

## Supplemental URP axes

SRP Core combines registered variant strippers by requiring every stripper to allow removal. IllusionRP's existing keyword rules protect shared states that its producers supply, including screen-space shadows and ambient occlusion. Supplemental filtering runs separately after those callbacks and leaves that protection in place.

- **Source.** Build data initializes URP's target build context and obtains each asset/renderer pair's `RendererRequirements`. Explicit SH modes use the asset's mode; Auto uses URP's resolved SH requirement. Atlas uses URP's resolved atlas requirement, including GPU Resident Drawer behavior.
- **Scope.** Only SubShaders tagged `RenderPipeline=UniversalPipeline` participate. Each rule applies only to the keywords declared by the current Pass, shader stage and compiler platform; an undeclared axis imposes no constraint.
- **SH axis.** The off state selects PerPixel, `EVALUATE_SH_MIXED` selects Mixed, and `EVALUATE_SH_VERTEX` selects PerVertex. Each renderer keeps its resolved mode.
- **Atlas axis.** Each renderer keeps the on or off state of `_REFLECTION_PROBE_ATLAS` selected by its URP requirement.
- **Joint reachability.** A variant is kept when its declared SH and atlas states both match the same target renderer. Removal requires a mismatch for every renderer. Independent unions of the two axes do not define reachable combinations.
- **Switches.** Supplemental filtering runs only when build data is valid and both IllusionRP and URP Strip Unused Variants are enabled. Otherwise it removes nothing.

## Pass stripping

A pass is a candidate only when its SubShader is tagged `RenderPipeline=UniversalPipeline` and both its name and LightMode match a registered entry. Every variant of a candidate pass is removed when no target renderer reaches it.

| Pass name | LightMode | Kept when any target renderer has |
|---|---|---|
| `OITTransparent` | `OIT` | Order-independent transparency. |
| `SubsurfaceDiffuse` | `SubsurfaceDiffuse` | Screen-space subsurface scattering. |
| `WaterSSRData` | `WaterSSRData` | Screen-space reflection and transparent screen-space reflection on the same renderer. |
| `PostDepthOnly` | `PostDepthOnly` | The transparent depth post pass, or order-independent transparency with transparent overdraw. |

- **Unmatched passes.** Passes outside the table, with unreadable metadata or with a different or missing tag, name or LightMode are kept.

## Shader authoring

Templates and shaders whose passes take part in stripping:

- tag the SubShader with `RenderPipeline=UniversalPipeline`;
- use a stable, unique pass name;
- use a LightMode equal to the runtime `ShaderTagId`;
- use the pass and keyword names that `IllusionShaderPasses` and `IllusionShaderKeywords` define;
- export generated shaders with template changes, as [ASE Shader Workflow](ase-shader-workflow.md) defines.
