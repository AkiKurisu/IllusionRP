# Wet Surface Decals

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |

Wet surface decals are box or sphere volumes that wet or dry the opaque surfaces inside them. Each camera builds a screen-space wetness mask from the registered volumes, and Forward materials darken, smooth and brighten their specular response by that mask, together with the screen-space normals and smoothness that SSR, SSGI and ambient occlusion read.

Out of scope: transparent and Unlit materials, the Deferred path, particles and weather systems, and path tracing.

## Ownership and registration

The renderer never references application component types. An application component implements `IWetSurfaceDecal` and registers itself with `WetSurfaceDecalRegistry` while it is enabled. Each frame the renderer asks every registered decal for a `WetSurfaceDecalData` snapshot; a decal that returns no data, or whose owning object was destroyed, is skipped. The registry is cleared when the player starts.

A snapshot is immutable and holds:

- the volume transform (world-to-local and local-to-world matrices) and its shape, a unit cube or a sphere of radius 0.5;
- the mode: wet adds wetness, dry removes it;
- saturation, edge fade-off and face sharpness;
- the layer mode (None, Single or Triplanar) and the projection mode (Local or World);
- up to three detail layers (X, Y and Z), each a mask texture with scale and offset and, per RGBA channel, an input start and extent and an output start and end;
- sample jitter and its amount;
- the world projection scale, the ratio between the caller's content units and world units.

## Enablement

Wet surfaces run for a camera only when all of these hold:

- the renderer feature's `wetSurfaceDecals` setting is on;
- the runtime switch `r.wetsurface` is on (also exposed in the Rendering Debugger under Illusion Features, Lighting);
- the renderer uses the Forward or Forward+ path;
- the camera is a Scene View camera or a base Game camera, and does not render only an offscreen depth texture;
- at least one decal is registered.

Wet surfaces need a blendable RGBA8 Forward GBuffer. When the platform lacks it, the renderer reports an error once and renders the camera without them. Each camera resets the wet state first, so a camera that skips wet surfaces never reads another camera's results.

## Frame order and resources

- **Forward GBuffer.** The Forward GBuffer keeps two targets, smoothness and camera normals. Its smoothness target is R8 without wet surfaces and RGBA8 with them: R holds the screen-space smoothness, G the material's source smoothness quantized to 8 bits, and B and A the complementary receive flag and specular gate. Materials keep computing their specular color themselves; no full-screen specular color is stored.
- **Mask.** After the depth and normal prepass, the mask pass reconstructs world positions from the visible depth and draws every volume into a single-channel float mask, together with a coverage target that records the pixels inside any volume. Both are cleared at the start of the pass. Volumes draw in the order wet cubes, wet spheres, dry cubes, dry spheres. Wet volumes accumulate as `1 - (1 - old)(1 - new)` and dry volumes erase as `old (1 - new)`. Cubes fade by the largest absolute local coordinate and spheres by the local radius; an edge fade-off of zero is a hard edge. Coverage is a separate target rather than a stencil bit, because stencil bit 0 belongs to ambient occlusion.
- **Normals.** Inside the coverage, normals are blurred horizontally and then vertically, each pass averaging the center with four depth-gated neighbors, and blended back into the camera normals by `saturate((w - 0.7) / 0.3)`, where `w` is the mask.
- **Smoothness.** A smoothness pass computes the wet screen-space smoothness from the packed source smoothness and gate, and publishes it as the camera's Forward GBuffer.
- **Readers.** SSR declares its read of that texture; ambient occlusion, SSR, SSGI and the Forward materials drawn afterwards read the patched normals and smoothness.
- **Transparent depth.** When transparent depth post passes run, they write over the wet smoothness in place, so wet pixels that no transparent surface covers stay wet. Without wet results they use the original Forward GBuffer.

## Projection

- **Layer modes.** None wets surfaces by how much they face the volume's Y axis, raised to the face sharpness. Single multiplies that by the Y layer. Triplanar blends the X, Y and Z layers by the surface normal in volume space divided by the diagonal of the volume's local-to-world matrix, so rotation changes the weights, raised to the face sharpness and normalized.
- **Channels.** Each layer remaps every RGBA channel from its input range to its output range and takes the largest channel. A channel with a zero input extent is disabled and contributes exactly zero.
- **Local projection.** Layer textures follow the volume transform.
- **World projection.** Layer textures stay aligned to the world: the texture coordinate is the world position divided by the diagonal of the world-to-local matrix, which scales with squared length. The world projection scale is applied once, as a squared distance scale; volume positions always come from the real transform.
- **Jitter.** Sample jitter offsets every layer sample by the red and green channels of a tiled blue noise, minus 0.5, times the jitter amount in texels. The tiling is (29, 31), and the texel size is the smallest among the active layer textures.

## Material response

Lit, Skin, Fabric and Hair Forward materials read the mask and their source properties before main lighting. Let `w` be the mask, `S` the source specular color and `r` the source smoothness:

```text
g = 1 - saturate(1000 * (max(S) - 0.5))
d = 0.8 * g * saturate((0.8 - r) / 0.9) * saturate(w / (0.8 * g))
t = saturate((w - 0.8 * g) / (1 - 0.8 * g))
diffuse *= 1 - d
specular = lerp(S, max(S, 0.3), t)
baseWetSmoothness = 1 - (1 - r) * (1 - d)
smoothness = lerp(baseWetSmoothness, 1, t)
```

- **Gate.** At 8-bit precision the gate `g` is 0 or 1. When `g` is 0, `d` is 0.
- **Threshold.** A mask below 0.001 leaves the material unchanged.
- **Applied lighting.** Emission and the ambient and baked lighting evaluated before reflections are multiplied by `1 - 0.35 d`. Reflections and real-time lights use the modified properties.
- **Shared response.** Forward materials and the screen-space smoothness pass share one response function. Skin and Hair apply the same smoothness response to their secondary lobes and keep their own minimum roughness limits.
- **Worked example.** A non-metallic surface reaches a smoothness of 1 at `w = 1`; a source smoothness of 0.7 becomes about 0.727, 0.863 and 1 at `w` of 0.8, 0.9 and 1.
- **Template ports.** Template shaders expose Wet Base Color, Wet Metallic and Wet Specular ports in their Forward GBuffer pass, so a graph supplies the source properties the response uses.
