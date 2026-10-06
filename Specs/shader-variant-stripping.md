# Shader Variant Stripping

| Field | Value |
|---|---|
| Version | 1.3.3 |
| Status | Living |
| Date | 2026-10-06 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Rendering Pipeline](rendering-pipeline.md) |

Stripping removes only states that the complete build target cannot reach. Renderer capabilities and content usage are separate inputs; neither the currently open scene nor one changed asset represents the whole build.

## Switch

Strip Unused Variants controls capability-based removal. Turning it off preserves runtime capability states. Static authoring filters remain independent. Invalid target data conservatively keeps variants and does not overwrite derived target prefilters.

## Build stages

1. Gather every target URP asset and renderer before variant enumeration.
2. Prefilter keyword axes to the aggregate reachable states.
3. Apply keyword rules across the target renderer set.
4. Supplement URP/Core processing with reachable SH/reflection-atlas combinations and whole-pass rules.
5. Apply build-scoped content evidence to OIT, then compile the retained programs.

## Capabilities

Each target renderer contributes its requirements, including Deferred and 2D requirements even without IllusionRP. IllusionRP capability comes from active serialized feature settings, not current runtime toggles or Volume values.

A variant may be removed only if every target renderer can do without it. Missing or inconsistent renderer data prevents capability stripping. Shared features whose producer is outside IllusionRP remain unknown rather than being assumed disabled; this applies to screen-space occlusion on renderers without an active Illusion feature.

Derived prefilters are a target-specific cache, not authoring intent. They are updated before Unity enumerates variants.

## Keyword prefiltering

Stable capability axes keep enabled, disabled or both states according to the complete renderer set. Runtime-varying contact-shadow and PCSS axes retain both when supported. Area lighting keeps the selected quality tiers and keeps its off state only for renderers without area lighting.

IllusionRP preserves states supplied by its own producers against URP prefiltering, including screen-space shadow and depth-normal requirements. Unknown shared-producer states remain available. Runtime render-pass alternatives remain available, while unsupported debug-only shader alternatives are removed.

## Keyword rules

Rules apply only to axes declared by the current pass. The state retained for a stable global feature follows the actual renderer keyword, even when a particular material does not consume that feature's output.

In particular, ordinary transparency's SSR/AO sampling restrictions do not make the camera-wide enabled keyword state unreachable. Transparent variants retain that state. Screen-space main-light shadows differ: runtime switches back to shadow-map state before transparent drawing, so their transparent-specific removal remains valid.

Surface classification follows the declared transparent keyword. A shader that hardcodes its surface type is not implicitly reclassified by these keyword rules. Authoring must keep that convention consistent with its pass behavior.

## Supplemental URP axes

SRP Core requires agreement among its registered strippers. IllusionRP retains that combination policy and applies supplemental reachability after those callbacks.

SH evaluation and reflection-probe atlas requirements come from URP's resolved target requirements, including automatic choices and GPU Resident Drawer. A retained combination must match one actual target renderer; independent unions of separate axes must not invent combinations no renderer uses.

Supplemental filtering is limited to URP-tagged passes and their declared axes and requires valid data with both URP and IllusionRP stripping enabled.

## Pass stripping

URP GBuffer and 2D passes are removed only when no target renderer needs Deferred/Deferred+ or Renderer2D respectively, with both stripping switches enabled. IllusionRP Forward GBuffer is a separate contract and remains unaffected.

| Pass / LightMode | Required capability |
|---|---|
| `OITTransparent` / `OIT` | Order-independent transparency. |
| `SubsurfaceDiffuse` | Screen-space subsurface scattering. |
| `WaterSSRData` | Opaque and transparent SSR on the same renderer. |
| `PostDepthOnly` | Transparent depth, or OIT with transparent overdraw. |

IllusionRP pass rules require matching pass identity and URP pipeline tagging. Unknown, unreadable or unmatched pass metadata is preserved.

## Content pass usage

The Editor build scope accepts complete Shader pass-use declarations for one build. The content builder owns dependency coverage and runtime-use knowledge; IllusionRP does not discover assets. Unknown usage preserves the pass. Explicitly unused OIT can be removed; any used declaration wins when references share a Shader. A used declaration cannot create renderer support that does not exist.

This content rule is limited to OIT. It does not generalize disabled material passes into arbitrary removal. Capability stripping must be enabled with valid target data.

Contexts live only for their build, restore an enclosing context on exit and are cleared on failure. A nested build supplies its own complete declaration set rather than inheriting unspecified entries.

## Build cache invalidation

Stripping policy changes invalidate compiled Shader caches. The optional SBP integration contributes the policy version to SBP's callback cache identity without making the core Editor assembly depend on SBP.

Content builders also include policy and per-Shader usage in their own dependency identity, including unknown usage. This must invalidate both incremental decisions made before SBP and affected Shader write operations inside SBP. Per-build usage is not persisted into renderer settings, and global cache deletion is not an invalidation strategy.

## Shader authoring

Runtime renderer lists, templates and stripping agree on stable pass names, LightModes and pipeline tags. Template changes reach generated shaders through [ASE Shader Workflow](ase-shader-workflow.md). Retained material signatures must compile and render correctly without relying on closest-variant fallback.
