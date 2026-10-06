# Wet Surface Decals

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |

Wet surface decals alter opaque surface response inside registered box or sphere volumes. The camera-local result updates both material lighting and the surface data used by screen-space effects.

## Ownership and registration

Enabled scene decals register with the renderer and supply a per-frame snapshot of their volume, wet/dry operation, projection and detail layers. Disabled or destroyed owners do not participate. Registration does not transfer scene-asset ownership to the renderer.

## Enablement

Wet surfaces require renderer capability, runtime permission, Forward/Forward+, at least one registered decal and a base Game or Scene View camera with a color target. Overlay, preview and offscreen-depth cameras do not participate.

The platform must support the wet surface target's blendable format; otherwise the feature reports the limitation and skips the camera. State resets every camera to prevent inherited wet results.

## Frame order and resources

1. Material prepass supplies normals, smoothness and the source material information needed for wet response.
2. After the prepass, volume projection produces a wet mask and separate coverage. Wet volumes accumulate wetness before dry volumes remove it; coverage records participation independently.
3. Coverage-limited, depth-aware smoothing updates camera normals; wet response updates screen-space smoothness.
4. AO, SSR, SSGI and subsequent Forward shading consume the consistent updated surface.

Coverage is separate from stencil because existing material stencil already owns those meanings. The pipeline does not store a full-screen specular color; material shading retains that responsibility.

Transparent post-depth updates the same smoothness resource in place, preserving wet pixels outside its coverage. Without wet processing, transparent depth uses the original Forward GBuffer.

## Projection

Local projection follows the volume transform; world projection remains world-aligned. World-scale conversion happens once at the boundary defined by [World Scale](world-scale.md), without changing actual volume positions.

Detail supports untextured, single-layer and triplanar projection. Channel remapping treats disabled channels as zero. Edge fading and optional sample jitter shape the mask without changing resource ownership or coverage order.

## Material response

Lit, Skin, Fabric and Hair darken diffuse response, raise specular response and smooth the surface according to wetness and source properties. Forward materials and screen-space smoothness use the same response, preserving the distinction between source material data and already modified output.

Skin and Hair apply the response consistently to their secondary lobes while preserving minimum roughness. The response is applied before the lighting stages that consume modified properties; untouched pixels retain their original material. Templates expose the source material inputs required by the prepass. Screen-space wet results are not physical material inputs for [Path Tracing](path-tracing.md).
