# Transparency

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md), [Water](water.md) |

IllusionRP preserves URP conventional transparency and adds depth, refraction, transparent reflections and weighted blended OIT around it. These are coordinated producer chains, not an exact global transparency sorter.

## Enablement

Renderer capability and camera eligibility gate each stage. Refraction and transparent SSR have independent runtime controls; transparent SSR additionally requires opaque SSR, an active SSR Volume and the supported compute path. OpenGL ES 3 does not support this path. Preview cameras skip the transparency extensions; offscreen-depth cameras skip refraction and reflection work.

OIT uses its configured object-layer selection. Overdraw requires OIT, and its optional VRS requires device support. Reflection cameras receive a neutral transparent reflection result. Disabled or unavailable reflection/refraction producers publish black rather than another camera's output.

Build reachability follows [Shader Variant Stripping](shader-variant-stripping.md), including declared OIT content use.

## Frame order

1. After the opaque prepass, preserve pre-depth and prepare post-depth when transparent depth or transparent SSR needs it; draw transparent post-depth when enabled.
2. After opaques and before skybox, copy refraction color, produce water surface data, then resolve transparent SSR.
3. URP draws conventional transparent color.
4. Accumulate/composite OIT, restore depth for optional overdraw, generate optional VRS, redraw conventional transparency, then generate next-frame color history.

The enclosing schedule is defined by [Rendering Pipeline](rendering-pipeline.md).

## Transparent depth

Pre-depth contains only opaque and alpha-tested geometry. Post-depth begins as its copy and adds authored transparent coverage. The post pass includes all render queues because opaque-queue Hair carries fringe depth there. It updates smoothness but not opaque normals.

Materials opting into post-depth must match their later transparent coverage. Published post-depth replaces both frame-data and shader depth references; opaque effects retain the matching pre-depth. Wet smoothness outside new transparent coverage is preserved.

When only water SSR needs post-depth, its private depth is not published as the general camera depth. With neither consumer, the chain allocates neither depth copy.

## Pre-refraction color

Refraction copies current opaque color before skybox and transparents so transparent shading never reads its own output attachment. It is independent of previous-frame color used by SSR.

Water consumes this copy. The legacy refraction model in Lit/Fabric continues to consume URP opaque color. Depth behind water is published separately from depth that may contain the water itself. Missing or disabled sources are neutral, never stale history.

## Water SSR data

Transparent water contributes reflection normal, smoothness and nearest visible depth through `WaterSSRData`. That pass must match Forward geometry, culling and coverage and cannot write final color. Scene-depth sampling uses pre-water depth rather than the attachment being written.

Only the nearest water surface per pixel supplies reflection data. When transparent post-depth is enabled, water depth becomes visible to later depth consumers; OIT still tests the active camera depth until the explicit restore stage.

## Transparent screen-space reflection

Transparent SSR traces against the opaque depth pyramid and reprojects previous-frame color. It does not accumulate its own temporal result. Missing history, water data, depth or compute support produces a neutral reflection result.

Its publication replaces opaque SSR before transparent color rendering. Transparent materials cannot accidentally receive the earlier opaque reflection texture. Reflection confidence drives environment fallback as defined by [Water](water.md).

## Weighted blended OIT

The `OITTransparent` pass with `OIT` LightMode accumulates weighted lit color and coverage without writing depth, testing against active camera depth. All queues participate within the configured layer selection. Composition resolves the accumulated color over camera color with exposure applied consistently.

Accumulation and revealage are initialized for each camera. Shared weighting and composition must agree, and results do not depend on draw order. Weighted blending remains approximate, particularly for nearly opaque coverage.

OIT runs after URP transparency. Conventional color therefore lies beneath its result unless overdraw corrects that relation. Disabling OIT removes OIT-only surfaces and Hair fringes; it does not silently switch their materials to another color pass. [Materials and Shaders](materials-and-shaders.md) owns color-pass synchronization.

## Hair fringe

Hair uses opaque core and OIT fringe as disjoint color owners. Fringe OIT and post-depth share coverage thresholds, deformation and culling. Both renderer lists include opaque queues; restricting them to transparent queues would drop normal Hair fringe materials.

## Overdraw

Before redraw, published camera depth is restored into active depth for the remainder of the frame. Conventional transparent passes are then redrawn back-to-front, with depth testing and optional configured stencil restriction. Optional stencil VRS is consumed only by this redraw.

This mitigates OIT bleed-through but blends every passing conventional pixel again. Without transparent post-depth, all such pixels in front of opaques can be blended twice. Stencil interpretation must agree with [Materials and Shaders](materials-and-shaders.md#stencil).

## Limitations

Refraction contains neither skybox nor previous transparent draws. SSR is limited to on-screen opaque hit geometry and historical color, so motion and disocclusion can artifact. Multiple water layers share the nearest surface's reflection. High alpha, intersecting media, refraction and exact cross-owner ordering are not guaranteed by OIT or overdraw.
