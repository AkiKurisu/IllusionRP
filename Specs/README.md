# IllusionRP Specifications

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Living |
| Date | 2026-10-18 |

Core design principles, rendering flows and source workflows for IllusionRP. These specifications explain how the system fits together and the behaviors that changes must preserve.

## Documentation boundary

- **Specifications** define feature boundaries, ownership, producer-consumer relationships, frame and asset flows, resource lifetime, and observable correctness requirements.
- **Usage documentation** explains installation, configuration, authoring and extension through the supported interfaces. Start with the [project guide](../README.md).
- **Source code** defines implementation structure, fields, memory layouts, formulas and algorithms. Do not maintain a second implementation reference here.

Keep an interface or resource name only when it identifies a necessary integration boundary. Describe the contract and link related specifications instead of enumerating every property, method or buffer member. A design principle may constrain implementation without prescribing its class decomposition.

- **Code references.** Leave out console variables, type, method and field names, keywords, texture properties and source paths. Pass names, LightModes and other authoring conventions that shaders or content must match are contracts and may be named.
- **Values.** Leave out tuned constants, measurements, formulas and encodings. State the rule a value satisfies instead.
- **Origins.** State rules without citing reference renderers, talks, samples or guides. Imported components such as the RTXDI SDK, NRD, SHaRC and RTXPT may be named as dependencies; [Third-Party Source Imports](third-party-source-imports.md) owns import tracking.

## Conventions

- One English file per topic, named `<kebab-case>.md`.
- `Version` is the IllusionRP version the spec describes, `Status` is `Draft` or `Living`, and `Date` is the latest substantive update.
- A spec changes before or with the implementation it governs.
- Describe the current design, not an implementation changelog, investigation log or benchmark report. Superseded behavior, porting history, status notes and open questions do not belong in a specification.
- Give each contract one owner and cross-link other topics instead of repeating its rules.

## Index

| Specification | Scope |
|---|---|
| [Rendering Pipeline](rendering-pipeline.md) | Renderer feature ownership, per-camera enablement, frame order, camera eligibility, runtime switches and lifecycle. |
| [Render Resources](render-resources.md) | Resource ownership, publication, per-camera history and release. |
| [Materials and Shaders](materials-and-shaders.md) | Material families, surface and pass ownership, shared shading behavior and authoring boundaries. |
| [Shader Variant Stripping](shader-variant-stripping.md) | Build-target capability aggregation, keyword prefiltering, keyword rules, supplemental URP axes and IllusionRP pass stripping. |
| [Transparency](transparency.md) | Transparent depth, pre-refraction color, water SSR data, transparent SSR, weighted blended OIT and overdraw. |
| [Water](water.md) | The Water shader and template, reflection modes, refraction and path traced water. |
| [Wet Surface Decals](wet-surface-decals.md) | Screen-space wet and dry projection volumes and the wet response of Forward opaque materials. |
| [Directional Per-Object Shadows](directional-per-object-shadows.md) | Per-camera directional light authority, the per-object shadow atlas and screen-space shadow consumption. |
| [Area Lights](area-lights.md) | Rectangle area lights: per-camera producer, shadows, cookies, quality and shadow filtering, disabled state and per-family shading. |
| [Sun Shafts](sun-shafts.md) | Screen-space sun shafts: scope, Volume, anchor and passes. |
| [World Scale](world-scale.md) | Conversion between logical world lengths and Unity world units. |
| [Ray Tracing Scene](ray-tracing-scene.md) | The acceleration structure scene shared by path tracing and ray traced global illumination: ownership, participation, updates and lifetime. |
| [Path Tracing](path-tracing.md) | Reference and Realtime modes, Unity material evaluation, IllusionRP scattering models and RTXPT integration. |
| [Precomputed Radiance Transfer](precomputed-radiance-transfer.md) | Transport baking, sector scheduling, world lighting, cached visibility and per-camera publication. |
| [Ray Traced Global Illumination](ray-traced-global-illumination.md) | Indirect diffuse and specular for raster cameras: receivers, secondary surfaces, radiance cache, ReSTIR GI resampling, NRD denoising and composition. |
| [Inline Ray Tracing](inline-ray-tracing.md) | RTGI Performance mode in compute with ray queries: traversal, exceptions, the scene resource table and inline material evaluation from each material's own `PathTracing` source. |
| [ASE Shader Workflow](ase-shader-workflow.md) | Ownership of templates, graphs, functions and generated shaders, and export convergence. |
| [Third-Party Source Imports](third-party-source-imports.md) | Source pinning, import commits, license notices, host adaptation and difference markers. |
