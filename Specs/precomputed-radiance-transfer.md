# Precomputed Radiance Transfer

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Living |
| Date | 2026-10-18 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md) |

PRT provides diffuse indirect lighting for fixed baked geometry and materials under changing lights and environment. Baking traces transport with hardware ray tracing; runtime raster/compute relighting does not require it. Probe interpolation and patch averaging are explicit approximations.

## Execution order and scheduling

Transport is partitioned into sectors that are relit independently. The workflow is:

1. Snapshot geometry, authored materials and probe placement for an offline bake.
2. Partition transport into sectors, preserving material and layer boundaries; associate surface feedback with the closest valid baked probe.
3. Bind the completed asset and initialize committed probe state without uploading all sector transport.
4. Upload and update selected sectors within residency and per-frame work budgets. Prioritize the camera neighborhood while guaranteeing background progress.
5. Relight complete selected sectors, integrate their probes, then commit their results and publish changed camera-volume regions.
6. Sample the published field during normal material shading.

All sectors selected in one frame read the same frame-start feedback. Updating one sector does not wait for the entire grid. Moving a camera window does not trigger full-world relighting. Uploading background work keeps its progress; an oversized sector requires a smaller bake partition rather than an unbounded fallback.

## Offline transport and baking

The asset owns a fixed axis-aligned world grid and independently owned sector transport. Probe identity is global; transport storage is sector-local. Geometry, materials, placement and authoring inputs are snapshotted consistently, and the bake is rejected if those inputs change before completion. A transformed volume cannot reinterpret a bake in a different world space.

Capture traces every probe's sample directions against the snapshotted meshes and shades first hits with the materials' [path tracing](path-tracing.md) pass, which supplies the normal, diffuse reflectance and binary coverage. Diffuse reflectance follows the path tracing convention. Each material's cull mode decides which faces a ray can hit; materials without one are two-sided. Geometry is the static or baked skinned mesh, so vertex-shader animation is not captured. Screen-space simplification overrides are not substitutes for bake inputs. Fractional transparency continuation, emission transport and full BSDF transport are outside this diffuse first-hit model. Unsupported geometry and material inputs are diagnosed rather than silently omitted.

Bake direction sampling and integration use the same deterministic convention. Geometry transfer and sky visibility remain separate. Geometry validity is distinct from runtime intensity or authoring masks, so removing a mask can restore a geometrically valid probe without rebaking unchanged transport.

Virtual offsets trace the captured geometry and do not depend on colliders or modify global physics. Thin transparent or double-sided surfaces do not establish occupancy, and solids resting on each other open no exit. An offset that cannot be verified invalidates its probe.

Baking requires DirectX 12 with hardware ray tracing and rejects materials without a path tracing pass; runtime shaders carry no bake-only variants. Temporary capture state is restored, cancellation completes outstanding readbacks before releasing their resources, and incomplete work never replaces a valid asset.

## Lighting and visibility snapshots

Relighting selects world-space lights against each sector's geometry bounds, not camera-visible light lists or screen-space clusters. Directional, point and spot lights follow raster color, intensity, attenuation and layer conventions. Light cookies are not evaluated; a light with a cookie is diagnosed.

Lighting changes and visibility changes are tracked independently. Color changes retain valid visibility; light identity, geometry of the light, shadow settings and layers invalidate incompatible cached visibility. Cache entries cannot migrate accidentally between lights after reordering.

PRT reuses existing URP shadow coverage and never renders its own shadow maps. A compatible covered sample refreshes visibility. Outside coverage, a valid sample for the same light state remains usable; samples carry no age. Missing coverage uses an explicitly unknown, unoccluded approximation for that evaluation without promoting it to valid cached visibility. Unknown coverage does not block publication.

Geometry and surface materials remain fixed by the bake. Runtime observation covers lights, environment, probe settings and existing shadow coverage; geometry or material changes require rebaking.

## GPU residency, feedback and publication

The renderer owns committed global probe lighting and readiness. Sector residents own transport and update scratch state; CPU storage preserves baked transport and evicted visibility. Published lighting survives transport eviction.

Uploading, active and pending-eviction residents all count toward the budget. Eviction preserves visibility asynchronously before release; a failed preservation retains ownership and reports the failure. Reload restores only compatible light identities and visibility. Budget pressure preserves existing publication and reports delayed work instead of uploading the whole world.

Surface feedback uses its nearest baked probe; material shading samples the published camera cascades. Invalid or unready feedback contributes zero. All selected integrations finish reading frame-start state before any sector commit. Publication has no whole-grid copy or CPU-readback gate, and convergence diagnostics never stop continuous lighting updates.

A primary Game camera drives updates, with Scene View as a fallback when no Game camera renders. Other cameras publish shared committed results without duplicating relighting. Each camera tracks its own publication changes; moving windows publish only newly covered slots. Bookkeeping advances when recorded publication executes.

Interpolation respects probe validity and intensity and preserves signed lighting until final irradiance evaluation. Positions outside every cascade, missing publications or zero valid weight in every covering cascade use the consumer's fallback; reflection normalization is neutral without coverage. Spatial filtering is an approximation whose error is evaluated separately from solver arithmetic.

Every eligible camera publishes committed or neutral data even when no sector changes. Reflection/Preview or disabled cameras cannot leave inherited empty state for a later Game camera. Asset replacement resets residency and publications atomically, including ownership of pending work; disabling and re-enabling can recover normally.

## Camera cascades

Each eligible camera publishes a small number of nested cascades. The finest holds committed probe lighting at the baked spacing; each coarser cascade covers a larger extent at a coarser spacing. Cascades are aligned with the baked grid, follow the camera and are clamped to the baked domain; a cascade adds nothing once a finer one covers the whole domain.

Coarse lighting is a prefilter of committed probe lighting, not separately baked transport. Invalid probes never contribute to it, and a fully invalid neighborhood remains invalid. It follows sector commits incrementally and, like committed probe lighting, survives transport eviction. Coarse cascades are a far-field approximation whose wider support may average lighting across thin occluders, so a coarser cascade never replaces a finer cascade that has valid coverage at a point.

A window move publishes only newly covered regions, and a sector commit republishes only the regions it intersects. The published content always equals a full rebuild at the same window position.

Shading uses the finest cascade with valid coverage at a point and blends into the next coarser one near its window boundary. The blend weight depends only on the camera position, so window steps do not pop. All PRT consumers share this evaluation.

## Resource declarations and observation

RenderGraph declares transport, lighting, shadow, feedback, readback and publication dependencies. Live publication is recorded with GPU write dependencies. Resource owners remain alive until their asynchronous operations finish.

Diagnostics distinguish budget pressure, update latency, residency, invalid integration and unknown shadow coverage; they never gate normal publication. Probe debug views inspect committed probe lighting independently of interpolated material sampling.

Validation holds geometry, lighting, camera and sampling fixed, compares captured transport against CPU integration, checks camera/resource lifecycle, and verifies that reversing selected-sector order leaves results unchanged. Budget changes may change response latency; stabilized results define static comparisons.
