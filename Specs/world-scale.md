# World Scale

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |

World Scale converts authored logical lengths into Unity world units at the renderer boundary. It lets content size change without rewriting Volume, renderer or diffusion-profile assets.

## Setting

World Scale expresses Unity units per logical unit, is positive, and defaults to identity. It is read during camera setup. It changes no transforms, camera properties, collider bounds, lights or baked assets; it affects only IllusionRP values authored as logical distances.

## Conversion boundary

After Volume blending, a consuming pass converts logical quantities exactly once. Length, squared length and inverse length follow their dimensions. Already reconstructed Unity-space positions, camera depth, geometric biases and offsets are not converted again.

Shader-side and CPU-side conversion are alternatives for a given value, not cumulative operations. Fixed internal distances used against world-space geometry follow the same convention as exposed distances. Dimensionless ratios, screen-space values, angles and counts remain unchanged.

## Converted values

World-distance trace extents, AO radii, contact-shadow distances, penumbra extents, volumetric heights/distances and per-object shadow lengths follow World Scale. Distance-based rejection and bilateral weighting scale dimensionally; relative thickness and texel/pixel quantities do not.

Volumetric absorption and in-scattering preserve energy over the same logical path. Diffusion profiles retain their physical-unit meaning while conversion changes meters per Unity unit at consumption; profile assets and caches are not rewritten.

Wet world projection uses a squared-distance conversion exactly once. [Wet Surface Decals](wet-surface-decals.md) owns the projection and material flow.

## Not converted

Unity light and camera data, URP settings, transforms and culling results already describe the actual scene and remain unchanged. Baked meshes, lightmaps, PRT grids and adjustment volumes cannot be rescaled by this renderer setting. Re-baking and content transformation are separate operations.

Normalized material values, LOD screen thresholds, sample counts, time and screen-space post-processing remain unchanged. No sun-shaft parameter represents a world distance.
