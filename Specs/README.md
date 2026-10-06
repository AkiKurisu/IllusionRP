# IllusionRP Specifications

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-06 |

Core design principles, rendering flows and source workflows for IllusionRP. These specifications explain how the system fits together and the behaviors that changes must preserve.

## Documentation boundary

- **Specifications** define feature boundaries, ownership, producer-consumer relationships, frame and asset flows, resource lifetime, and observable correctness requirements.
- **Usage documentation** explains installation, configuration, authoring and extension through the supported interfaces. Start with the [project guide](../README.md).
- **Source code** defines implementation structure, fields, memory layouts, formulas and algorithms. Do not maintain a second implementation reference here.

Keep an interface or resource name only when it identifies a necessary integration boundary. Describe the contract and link related specifications instead of enumerating every property, method or buffer member. A design principle may constrain implementation without prescribing its class decomposition.

## Conventions

- One English file per topic, named `<kebab-case>.md`.
- `Version` is the IllusionRP version the spec describes, `Status` is `Draft` or `Living`, and `Date` is the latest substantive update.
- A spec changes before or with the implementation it governs.
- Describe the current design, not an implementation changelog, investigation log or benchmark report.
- Give each contract one owner and cross-link other topics instead of repeating its rules.

## Index

| Specification | Scope |
|---|---|
| [Rendering Pipeline](rendering-pipeline.md) | Renderer feature ownership, per-camera enablement, frame order, camera eligibility, runtime switches and lifecycle. |
| [Render Resources](render-resources.md) | Resource ownership, publication, per-camera history and release. |
| [Materials and Shaders](materials-and-shaders.md) | Material families, surface and pass ownership, shared shading behavior and authoring boundaries. |
| [Shader Variant Stripping](shader-variant-stripping.md) | Build-target capability aggregation, keyword prefiltering, keyword rules, supplemental URP axes and IllusionRP pass stripping. |
| [Transparency](transparency.md) | Transparent depth, pre-refraction color, water SSR data, transparent SSR, weighted blended OIT and the overdraw. |
| [Water](water.md) | The Water shader and template, reflection modes, refraction and path traced water. |
| [Wet Surface Decals](wet-surface-decals.md) | Screen-space wet and dry projection volumes and the wet response of Forward opaque materials. |
| [Directional Per-Object Shadows](directional-per-object-shadows.md) | Per-camera directional light authority, the per-object shadow atlas and screen-space shadow consumption. |
| [Sun Shafts](sun-shafts.md) | Screen-space sun shafts: Volume, anchor, passes and the porting boundary. |
| [World Scale](world-scale.md) | Conversion between logical world lengths and Unity world units. |
| [Path Tracing](path-tracing.md) | Reference and Realtime modes, Unity material evaluation, IllusionRP scattering models and RTXPT integration. |
| [Precomputed Radiance Transfer](precomputed-radiance-transfer.md) | Transport baking, sector scheduling, world lighting, cached visibility and per-camera publication. |
| [ASE Shader Workflow](ase-shader-workflow.md) | Ownership of templates, graphs, functions and generated shaders, and export convergence. |
| [Upstream Source Migration](upstream-source-migration.md) | Source revisions, import history, host adaptation and difference markers. |
