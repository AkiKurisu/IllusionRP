# Precomputed Radiance Transfer

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-06 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md) |

PRT provides diffuse indirect lighting for fixed baked geometry and materials under changing lights and environment. Baking captures transport; runtime raster/compute relighting does not require ray tracing. Probe interpolation and patch averaging are explicit approximations.

## Execution order and scheduling

The sector-based design follows [The Division, GDC 2016](https://mrakobes.com/Nikolay.Stefanov.GDC.2016.pdf). The workflow is:

1. Snapshot geometry, authored materials and probe placement for an offline bake.
2. Partition transport into sectors, preserving material and layer boundaries; associate surface feedback with the closest valid baked probe.
3. Bind the completed asset and initialize committed probe state without uploading all sector transport.
4. Upload and update selected sectors within residency and per-frame work budgets. Prioritize the camera neighborhood while guaranteeing background progress.
5. Relight complete selected sectors, integrate their probes, then commit their results and publish changed camera-volume regions.
6. Sample the published field during normal material shading.

All sectors selected in one frame read the same frame-start feedback. Updating one sector does not wait for the entire grid. Moving a camera window does not trigger full-world relighting. Uploading background work keeps its progress; an oversized sector requires a smaller bake partition rather than an unbounded fallback.

## Offline transport and baking

The asset owns a fixed axis-aligned world grid and independently owned sector transport. Probe identity is global; transport storage is sector-local. Geometry, materials, placement and authoring inputs are snapshotted consistently, and the bake is rejected if those inputs change before completion. A transformed volume cannot reinterpret a bake in a different world space.

Capture uses authored vertex deformation, normal, diffuse and binary coverage inputs through the existing Forward GBuffer authoring path. Screen-space simplification overrides are not substitutes for bake inputs. Fractional transparency continuation, emission transport and full BSDF transport are outside this diffuse first-hit model. Unsupported geometry and material inputs are diagnosed rather than silently omitted.

Bake direction sampling and integration use the same deterministic convention. Geometry transfer and sky visibility remain separate. Geometry validity is distinct from runtime intensity or authoring masks, so removing a mask can restore a geometrically valid probe without rebaking unchanged transport.

Virtual offsets use the captured geometry and do not depend on colliders or modify global physics. Thin transparent or double-sided surfaces do not establish occupancy. An offset that cannot be verified invalidates its probe.

The bake-only capture variant remains available in the Editor and is always stripped from runtime builds. Generated shaders follow [ASE Shader Workflow](ase-shader-workflow.md). Temporary capture state is restored, cancellation completes outstanding readbacks before releasing their resources, and incomplete work never replaces a valid asset.

## Lighting and visibility snapshots

Relighting selects world-space lights against each sector's geometry bounds, not camera-visible light lists or screen-space clusters. Directional, point and spot lights follow raster color, intensity, attenuation and layer conventions. Cookies are diagnosed but not evaluated by this baseline.

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

Each eligible camera publishes a small number of nested cascades. Cascade 0 holds committed probe lighting at the baked spacing; every further cascade doubles the spacing and, with the same probe count, covers twice the extent. Cascades are axis-aligned with the baked grid, snapped to their own spacing, centered on the camera and clamped to the baked domain. A cascade whose predecessor already covers the whole domain is not published.

Coarse lighting is a prefiltered pyramid of committed probe lighting, not separately baked transport. Each coarse node is a tent-weighted average of the finer level around its aligned node, accumulated in the validity-premultiplied form that shading interpolates, so invalid probes never contribute and a fully invalid neighborhood remains invalid. Pyramid levels are global, follow sector commits incrementally and, like committed probe lighting, survive transport eviction. Coarse cascades are a far-field approximation: their wider support may average lighting across thin occluders, so a coarser cascade never replaces a finer cascade that has valid coverage at a point.

Publication uses toroidal addressing on the horizontal axes. A window step publishes only the newly covered slabs and hardware filtering wraps across the physical seam; a vertical step rebuilds that cascade. Sector commits republish only the intersecting slots of each cascade. The content of a toroidal publication equals a full rebuild at the same window position.

Shading uses the finest cascade containing the point and blends into the next coarser cascade across a one-cell band along the axes where its window scrolls. The band is tightened by one cell and centered on the camera position clamped into the window, so the weight depends only on the camera and window steps do not pop. The coarsest published cascade does not fade, and the accumulated result is normalized by its total weight. A cascade with zero valid weight at a point yields to the next coarser one. All PRT consumers share this evaluation.

## Resource declarations and observation

RenderGraph declares transport, lighting, shadow, feedback, readback and publication dependencies. Live publication is recorded with GPU write dependencies. Resource owners remain alive until their asynchronous operations finish.

Diagnostics distinguish budget pressure, update latency, residency, invalid integration and unknown shadow coverage; they never gate normal publication. Probe debug views inspect committed probe lighting independently of interpolated material sampling.

Validation holds geometry, lighting, camera and sampling fixed, compares captured transport against CPU integration, checks camera/resource lifecycle, and verifies that reversing selected-sector order leaves results unchanged. Budget changes may change response latency; stabilized results define static comparisons. Measurements and captures belong outside this living contract.
