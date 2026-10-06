# Directional Per-Object Shadows

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-06 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Transparency](transparency.md), [World Scale](world-scale.md) |

Per-object shadows add a camera-local quality layer for registered casters on top of URP shadowing. One directional source owns the layer; screen-space production and material sampling preserve that source's identity.

## Light authority

URP owns the scene-wide main light. Rendering layers do not create another main light. By default per-object shadows follow a usable main directional light. An enabled camera selector may choose a different visible, shadow-casting directional light on Forward+ when the renderer permits it.

An explicitly selected but unusable source disables the layer for that frame instead of silently switching lights. A missing selector or destroyed source returns to main-light selection. Disabling additional-source selection ignores the selector and retains main-light behavior. Preview cameras have no per-object shadows.

Source identity and mode are established per camera even when no caster is visible; additional-light association must match URP's current light list.

## Casters

Active caster registrations supply renderer groups. Registration is shared, while culling, allocation and history are camera-local. Eligible renderers draw their normal ShadowCaster material pass; the added atlas does not remove that pass or replace standard shadow support.

Culling and priority favor important visible casters within the atlas capacity. Projection uses the source direction and [world-scale](world-scale.md) distance convention.

## Atlas allocation

Fixed allocation gives casters equal tiles. Adaptive allocation distributes bounded atlas space according to projected coverage and priority. Identical inputs produce stable allocation; quality increases respond promptly while decreases use hysteresis to avoid oscillation.

Overflow reduces lower-priority allocations rather than exceeding the atlas. Guard regions and sampling must agree, and any changed allocation invalidates only the affected camera's temporal shadow history.

## Producer parameters

Bias derives from the selected source and actual tile scale. Soft-shadow quality follows the source and pipeline support, independently of whether URP allocated an atlas for it. PCSS runs in screen-space production when enabled; direct material sampling uses PCF.

Shared comparison samplers are valid only when they match the atlas's actual filtering and addressing. Platforms that force point-filtered shadow allocation retain matching sampling rather than inheriting a linear sampler.

## Main atlas caster split

Main-source mode intentionally draws casters into both standard and per-object atlases: transparent, volumetric, PRT and third-party consumers still need a complete standard atlas.

With a valid separate source in Forward+, configured per-object rendering layers may be excluded from the main light's shadow cull. This requires both sources to be usable and visible and the main light to support the layer override. Otherwise the standard atlas remains complete.

The exclusion affects only shadow participation for that camera, not illumination or object visibility. It is applied before culling and restored when the camera ends, before another camera, or when the feature is disabled/disposed. Caster layer assignment remains an explicit content responsibility.

## Screen-space visibility

Screen-space production uses opaque depth. Main-light visibility combines standard shadows, main-mode per-object shadows and contact shadows. Separate-source per-object visibility remains independently identifiable.

Contact shadows always follow the main light. Temporal/spatial filtering and PCSS operate in the producer, so opaque materials do not filter the atlas again. Source identity/direction, main direction and allocation changes invalidate incompatible history.

## Material consumption

Main shading reads main visibility. Only the selected additional directional light receives separate-source visibility, combined with its regular attenuation. Lit, Skin and Fabric receive it; Hair casts but opts out of this additional self-shadow reception.

Opaque surfaces use screen-space visibility when available and direct atlas sampling otherwise. Transparent surfaces never reuse the opaque additional-source visibility; when transparent reception is enabled they sample at their own position. Third-party shaders without IllusionRP lighting receive only the standard main screen-space result.

The normal lighting loop still owns shadowmask, distance fade, layers, cookies and attenuation. Published screen-space values represent realtime visibility only.
