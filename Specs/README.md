# IllusionRP Specifications

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |

Contracts for IllusionRP features: what each feature must do, not how it is implemented.

## Conventions

- One English file per topic, named `<kebab-case>.md`.
- `Version` is the IllusionRP version the spec describes, `Status` is `Draft` or `Living`, and `Date` is the latest substantive update.
- A spec changes before or with the implementation it governs.

## Index

| Specification | Scope |
|---|---|
| [Rendering Pipeline](rendering-pipeline.md) | Renderer feature ownership, per-camera enablement, frame order, camera eligibility, runtime switches and lifecycle. |
| [Render Resources](render-resources.md) | Resource classes, shared and global resources, per-camera history, render graph declarations and release. |
| [Materials and Shaders](materials-and-shaders.md) | Shader families, passes and LightMode tags, the Forward GBuffer, stencil, `UnityPerMaterial`, keywords and cross-family lighting rules. |
| [Shader Variant Stripping](shader-variant-stripping.md) | Build-target capability aggregation, keyword prefiltering and IllusionRP pass stripping. |
| [Transparency](transparency.md) | Transparent depth, pre-refraction color, water SSR data, transparent SSR, weighted blended OIT and the overdraw. |
| [Water](water.md) | The Water shader and template, reflection modes and refraction. |
| [Wet Surface Decals](wet-surface-decals.md) | Screen-space wet and dry projection volumes and the wet response of Forward opaque materials. |
| [Directional Per-Object Shadows](directional-per-object-shadows.md) | Per-camera directional light authority, the per-object shadow atlas and screen-space shadow consumption. |
| [Sun Shafts](sun-shafts.md) | Screen-space sun shafts: Volume, anchor, passes and the porting boundary. |
| [World Scale](world-scale.md) | Conversion between logical world lengths and Unity world units. |
| [Path Tracing](path-tracing.md) | Reference and Realtime path traced cameras on DXR with HDRP material models. |
| [ASE Shader Workflow](ase-shader-workflow.md) | Ownership of templates, graphs, functions and generated shaders, and export convergence. |
| [Upstream Source Migration](upstream-source-migration.md) | Pinning, importing, adapting, marking and reviewing reference renderer sources. |
| [Validation](validation.md) | Validation layers, rendering comparisons, temporal effects and performance measurement. |
