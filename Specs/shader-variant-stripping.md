# Shader Variant Stripping

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-05 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Rendering Pipeline](rendering-pipeline.md) |

Shader variant stripping removes IllusionRP keyword variants and unreachable URP or IllusionRP passes that no renderer of the build target can reach. Reachability is aggregated from every URP asset, renderer and Illusion renderer feature the target uses; a variant or pass is removed only when every target renderer can do without it.

## Switch

`IllusionRenderPipelineSettings.stripUnusedVariants`, shown as Strip Unused Variants under Project Settings, Graphics, IllusionRP Global Settings, controls capability-based stripping and is on by default. When it is off, the capability-based keyword and pass rules remove nothing and, with valid build data, the dynamic prefilter is reset to keep every runtime IllusionRP keyword state. Static filters and mandatory Editor-only variant removal apply either way.

## Build stages

| Stage | Responsibility |
|---|---|
| Capability gathering | Collects the URP assets of the build target and computes one capability set per renderer, when the build starts and before Unity enumerates variants. |
| Keyword prefiltering | Writes the aggregated result into derived prefilter fields of each Illusion renderer feature, so Unity enumerates only the keyword states the target needs. |
| Editor-only variants | Removes every variant with `_PRT_CAPTURE` enabled from Player and AssetBundle shader builds, independently of build capabilities and stripping switches. |
| Keyword stripping | Evaluates the keyword rules per renderer and removes a variant only when every renderer allows it. |
| Supplemental URP axis filtering | After SRP Core and URP callbacks, removes SH/atlas combinations no target renderer selects. |
| Pass stripping | After SRP Core and URP have processed a pass, removes unreachable URP and IllusionRP passes, then applies declared content usage to OIT. |

## Capabilities

`_PRT_CAPTURE` is exclusively for Editor probe baking. The shader build callback removes its enabled variants before checking build capability data, pass metadata or stripping switches. Its disabled variants continue through normal runtime stripping rules. Editor shader compilation retains the capture variant for baking.

- **Sources.** The URP assets selected for the build target.
- **Per renderer.** Each renderer in each asset's renderer list contributes one capability set. Deferred/Deferred+ and Renderer2D requirements are collected independently of IllusionRP. An active Illusion renderer feature contributes the capabilities its serialized settings enable. A renderer without an active Illusion feature contributes no Illusion capability; its screen-space occlusion states remain unknown because another producer may own the shared URP keyword.
- **Invalid data.** The build data is invalid when the target has no URP asset, an asset is null or has no renderer list, a renderer or its feature list is null, a renderer has more than one Illusion renderer feature, no renderer is found, or gathering throws. With invalid data, nothing is stripped and the derived prefilter fields are not updated.
- **Prefilter cache.** The derived fields cache the target's prefilter result. When a value changes, the renderer feature asset is saved so that variant enumeration reads that result.

| Capability | Source |
|---|---|
| Deferred rendering | URP renderer requirements select Deferred or Deferred+. |
| 2D rendering | The renderer data is `Renderer2DData`. |
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
| `_SCREEN_SPACE_SSS`, `_SCREEN_SPACE_REFLECTION`, `_SCREEN_SPACE_OCCLUSION`, `_SCREEN_SPACE_GLOBAL_ILLUMINATION`, `_PRT_GLOBAL_ILLUMINATION`, `_TRANSPARENT_PER_OBJECT_SHADOWS`, `_SHADOW_BIAS_FRAGMENT` | Remove | Keep on and off | Keep on only |
| `_CONTACT_SHADOWS`, `_PCSS_SHADOWS` | Remove | Keep on and off | Keep on and off |

The area shadow axis `_`, `AREA_SHADOW_MEDIUM`, `AREA_SHADOW_HIGH` is one keyword set, prefiltered to an exact subset: the off state is kept only when some renderer has no area lights, and each tier only when some renderer selected it. On the Forward and Forward+ paths a renderer with area lights never needs the off state, because the selected tier stays enabled and the Volume, the runtime switch and empty frames set `_AreaLightCount` to zero instead.

Screen-space occlusion uses override priority to preserve IllusionRP's producer contract against URP's prefilter. If any renderer has no active Illusion feature, both occlusion states are preserved by IllusionRP; URP remains responsible for that renderer's own producer. Other targets keep only the state selected by `groundTruthAO`, or their union across renderers.

Static filters apply regardless of the switch:

- `_MAIN_LIGHT_SHADOWS_SCREEN` and `_SOURCE_DEPTH_NORMALS` are selected with override priority, so URP's own prefiltering does not remove them; the keyword rules below then decide screen-space main light shadow variants.
- Both states of `_ILLUSION_RENDER_PASS_ENABLED` are kept.
- `_DEBUG_SCREEN_SPACE_SHADOW_MAINLIGHT` and `_DEBUG_SCREEN_SPACE_SHADOW_CONTACT` are always removed.

## Keyword rules

A variant is removable for a renderer when any rule below removes it for that renderer, and it is removed only when it is removable for every target renderer. The global keywords follow the renderer feature settings rather than Volume parameters or runtime switches, as [Rendering Pipeline](rendering-pipeline.md#global-state) defines, so a state the settings never select is unreachable.

| Keyword | Renderer without the capability | Renderer with the capability |
|---|---|---|
| `_SCREEN_SPACE_REFLECTION` | Remove on. | Remove off in passes that declare the keyword, including transparent variants. |
| `_SCREEN_SPACE_OCCLUSION` | Remove on when IllusionRP owns the state; otherwise keep both. | Remove off in passes that declare the keyword, including transparent variants. |
| `_MAIN_LIGHT_SHADOWS_SCREEN` | Remove on. | Remove on in transparent variants; keep on and off otherwise. |
| `_SCREEN_SPACE_GLOBAL_ILLUMINATION`, `_PRT_GLOBAL_ILLUMINATION`, `_TRANSPARENT_PER_OBJECT_SHADOWS`, `_SHADOW_BIAS_FRAGMENT` | Remove on. | Remove off in passes that declare the keyword. |
| `AREA_SHADOW_MEDIUM`, `AREA_SHADOW_HIGH` | Remove both tiers. | Remove the tier the renderer did not select, and the off state in passes that declare the axis. |

- **Transparent variants.** A transparent variant is one with the `_SURFACE_TYPE_TRANSPARENT` keyword enabled. Such a surface does not sample screen-space reflections or ambient occlusion unless it writes post-depth ([Materials and Shaders](materials-and-shaders.md#screen-space-receivers)), but the renderer keeps their global keywords enabled for the whole camera. Those on variants must remain available. The main light shadow post pass explicitly disables its screen-space keyword before transparent drawing, so that axis retains its transparent-specific removal rule. A shader that declares `_SURFACE_TYPE_TRANSPARENT` as a keyword never defines `_TRANSPARENT_WRITE_DEPTH`. Shaders that define `_SURFACE_TYPE_TRANSPARENT` as a constant instead of declaring it as a keyword are treated as opaque by these rules.
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

A pass is a candidate only when its SubShader is tagged `RenderPipeline=UniversalPipeline`. URP `UniversalGBuffer` and `Universal2D` LightModes are removed, regardless of pass name, when no target renderer uses Deferred/Deferred+ or Renderer2D respectively; these rules require both URP and IllusionRP Strip Unused Variants. They do not affect IllusionRP `ForwardGBuffer`.

IllusionRP passes require both the name and LightMode in the table below. Every variant is removed when no target renderer reaches the pass.

| Pass name | LightMode | Kept when any target renderer has |
|---|---|---|
| `OITTransparent` | `OIT` | Order-independent transparency. |
| `SubsurfaceDiffuse` | `SubsurfaceDiffuse` | Screen-space subsurface scattering. |
| `WaterSSRData` | `WaterSSRData` | Screen-space reflection and transparent screen-space reflection on the same renderer. |
| `PostDepthOnly` | `PostDepthOnly` | The transparent depth post pass, or order-independent transparency with transparent overdraw. |

- **Unmatched passes.** Passes outside the URP LightModes and IllusionRP table, or with unreadable metadata or a different or missing required tag, name or LightMode, are kept.

## Content pass usage

An Editor content builder may open `IllusionShaderBuildScope.BeginPassUsage(IEnumerable<ShaderPassUsage>)` around its shader build. `ShaderPassUsage` is a readonly value containing `Shader`, `LightMode` and `Used`. The caller supplies the complete build dependency closure and includes runtime states that may enable a pass; IllusionRP does not discover content or infer usage from the currently open scene.

Only `OIT` declarations constrain stripping. An explicit `Used=false` removes a registered OIT pass; `Used=true` does not override an unreachable renderer capability. An absent declaration is unknown and keeps the pass when renderers can reach it. Duplicate declarations for one Shader keep the pass if any says used. Other LightModes are ignored.

The scope snapshots declarations before becoming active, is disposed with `using` even when a build fails, and restores the enclosing scope. A nested scope supplies its own complete declaration set, rather than inheriting absent declarations. Scopes are disposed in reverse nesting order; repeated disposal is ignored. Shader usage applies only while capability stripping is enabled with valid build data.

## Build cache invalidation

When Scriptable Build Pipeline is installed, the optional `Illusion.RenderPipelines.SBP.Editor` assembly registers a shader/compute callback with SBP's `VersionedCallbackAttribute`, using `IllusionShaderBuildScope.PolicyVersion`. Increment that constant whenever these stripping or prefilter semantics change. SBP includes this attribute in `ShaderCallbackVersionHash`; Unity's `BuildCallbackVersionAttribute` versions scene callbacks and is not the shader cache contract used here.

The callback version identifies code semantics, not per-build content usage. Content builders must include both `PolicyVersion` and pass-usage declaration state in their Shader cache inputs, including shaders with unknown usage. This also invalidates caches that decide whether to invoke SBP at all. The scope does not clear caches or persist build declarations into renderer assets.

## Shader authoring

Templates and shaders whose passes take part in stripping:

- tag the SubShader with `RenderPipeline=UniversalPipeline`;
- use a stable, unique pass name;
- use a LightMode equal to the runtime `ShaderTagId`;
- use the pass and keyword names that `IllusionShaderPasses` and `IllusionShaderKeywords` define;
- export generated shaders with template changes, as [ASE Shader Workflow](ase-shader-workflow.md) defines.
