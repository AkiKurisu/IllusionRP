# Render Resources

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-06 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md) |

IllusionRP resources connect producers, material consumers and temporal rendering. [Rendering Pipeline](rendering-pipeline.md) owns frame order; feature specifications own the meaning of their outputs.

## Resource classes

| Resource | Ownership and lifetime |
|---|---|
| URP frame data | URP owns camera attachments and frame handles. IllusionRP declares access and replaces published handles only at an explicit pipeline boundary. |
| Transient intermediates | Owned by one recorded feature chain and released with the frame. |
| Renderer-level targets | Owned by the allocating pass or renderer data, reused across sequential cameras and resized to the current target. They are not history. |
| Per-camera history | Belongs exclusively to one camera and survives only while valid for that camera. |
| Persistent tables and atlases | Owned by the producing feature; shared tables have shared lifetime ownership. |
| Global publication | Exposes an output to shaders without transferring ownership or establishing execution dependencies. |

## Pipeline resources

The renderer resource asset references the shaders, lookup textures and meshes required by its features so builds include them. Resources found indirectly must still have an explicit inclusion path. Development-only resources remain separate from runtime requirements.

## Shared resources

| Output | Producer → consumers |
|---|---|
| Forward GBuffer and camera normals | Material prepass → ambient occlusion, SSR, SSGI and denoisers. Wet surfaces update the same surface description before those consumers. |
| Opaque depth pyramid | Opaque camera depth → screen-space traces and depth history. Transparent proxy depth never becomes its source. |
| Ambient occlusion | AO producer → opaque lighting. |
| SSR lighting | Opaque SSR → opaque materials; transparent SSR replaces the published result before transparent materials. |
| Indirect diffuse | SSGI → eligible material lighting. |
| Screen-space shadow visibility | Shadow production and filtering → opaque lighting. See [Per-Object Shadows](directional-per-object-shadows.md). |
| Subsurface lighting | Split diffuse lighting and scattering → Skin composition. |
| Color pyramid | Camera color after transparents → subsequent-frame reflection and indirect-light consumers. |
| Exposure | Fixed or automatic exposure → lighting and post-processing, with isolated camera history. |
| Material lookup tables | Persistent lighting tables → material families. |
| Wet mask and coverage | Registered volumes → wet surface response and screen-space surface updates. |
| Transparent depth, refraction and water data | [Transparency](transparency.md) → transparent material and composition stages. |
| PRT publication | Committed relighting → material sampling; transport residency and camera publication are separate lifetimes. See [PRT](precomputed-radiance-transfer.md). |

Producer and consumer must agree on resource meaning, coordinate space and encoding. Internal scratch formats and buffer layouts belong to source code, not this specification.

## Area lights

The area-light producer prepares visible light data, shadow requests and cookie placement per camera, then publishes a consistent set for material shading. Light data and cookie storage belong to the feature; shadow intermediates are frame-scoped and lookup tables may be shared.

Disabled cameras receive no lights and neutral atlases. Missing shadow-culling context disables area shadows with a diagnostic while area lighting continues. Cookie placement is reset for each render context. Lighting parameters must respect the combined platform resource budget of the material paths that consume them.

## Depth

- Opaque effects pair depth with matching opaque normals, motion and visibility. Their pyramid is built before transparent depth is added.
- Screen-space shadows and subsurface scattering retain the pre-transparent depth when a transparent depth chain exists.
- Transparent post-depth is published only at the boundary defined by [Transparency](transparency.md).
- Replacing camera depth updates both frame data and shader publication; neither may refer to a different surface set.

## Render graph declarations

Every producer and consumer declares actual texture, attachment, buffer and renderer-list access, including resources sampled indirectly through materials or global bindings. Persistent resources are imported with the same explicit dependencies as transients.

Publication, history writes and other externally visible effects must survive graph culling. Multi-step effects pass their outputs through a declared dependency chain; global state does not substitute for ordering or synchronization.

## Disabled state

A disabled feature publishes its defined neutral input or disables its consumer mode. SSR and refraction use black; area lights use an empty light set; PRT uses an empty publication; wet state resets for every camera. A previous camera's result is never a fallback.

Reflection cameras use neutral exposure and own no exposure history. On a post-processing history reset, automatic exposure starts from neutral exposure and adjusts the current camera color consistently. Earlier results may be retained only as explicitly supported, valid history.

## History

Color, exposure, SSR, SSGI, depth/normal and screen-space shadow histories are isolated per camera. Each consumer owns readiness, reprojection validity and invalidation on allocation, camera cuts, skipped work or incompatible configuration.

History sizing follows the camera target; shrinking resets storage to the new size. Frame-local readiness is reset without pretending persistent contents are newly produced. First-frame and post-processing resets cannot reproject through an unrelated previous camera state. Sampling must not introduce read-write feedback within a frame.

Temporal readiness reporting distinguishes missing camera state, warmup and invalid histories. Its result does not imply that exposure adaptation or every other history source has converged. Camera destruction, prolonged inactivity and renderer disposal release history.

### History color

Previous color is selected from the first valid source: the camera color pyramid for Game/Scene View cameras, URP TAA history, explicitly requested history color, then URP opaque color. With no valid source it is black. A camera that did not generate a pyramid cannot inherit another camera's mip availability.

## Global constants

Every camera establishes its own camera transforms, reprojection state, exposure, lighting controls and [world-scale conversion](world-scale.md). Current and previous transforms refer to the same camera; reset frames use the current transform as their previous state.

Global publication is a producer/consumer contract, not resource ownership. CPU and shader representations must remain consistent without duplicating their field layouts here.

## Asynchronous work

Compute paths require renderer preference, runtime permission and device support. Async compute is currently disabled. Any asynchronous producer must synchronize through declared resource dependencies, never through global publication alone.

## Release

The allocator owns release of persistent targets, buffers, materials, native storage and subscriptions. Renderer disposal releases camera states and neutral resources. Camera-specific backend contexts are released with their camera or backend. Pending asynchronous work remains owned until completion and cannot access disposed state.
