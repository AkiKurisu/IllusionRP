# Ray Traced Global Illumination

| Field | Value |
|---|---|
| Version | 1.4.1 |
| Status | Living |
| Date | 2026-10-14 |
| Related Specs | [Path Tracing](path-tracing.md), [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md) |

Ray traced global illumination (RTGI) adds indirect diffuse and indirect specular lighting to raster cameras. The raster opaque prepass provides the receiving surface; rays from it are traced through the same scene, materials and lights as [path tracing](path-tracing.md); ReSTIR GI reuses the resulting samples across neighboring pixels and frames; NRD RELAX denoises them; and the Forward materials consume the result through the existing indirect lighting seams. Raster direct lighting, shadows, material response and subsurface scattering are unchanged.

## Scope

- **Cameras.** Raster Game base cameras and Scene View cameras on the Forward and Forward+ paths.
- **Receivers.** Opaque and alpha-tested surfaces of the Lit, Skin, Fabric and Hair families, including hand-written Hybrid Lit, and every other surface that receives SSGI, which reads the same result.
- **Transport.** Each sample's GI ray reaches a secondary surface. In Performance mode, the default, that surface is lit by direct light, its own emission and the lighting environment, for one bounce. In Quality mode, its outgoing radiance comes from SHaRC's world-space cache, which carries multiple bounces, or the path continues until the cache can answer. A ray that leaves the scene sees the lighting environment.
- **Signals.** Indirect diffuse and indirect specular, produced, denoised and composited as separate signals.

Not included:

- transparent, OIT, water and refractive receivers, which keep their existing lighting;
- caustics and specular chains, which Performance mode ends at the secondary surface and Quality mode's radiance cache does not resolve;
- ReSTIR for direct lighting at the receiver, which stays raster;
- a replacement for path tracing, whose cameras render no raster passes;
- a screen-space and ray-traced mixed mode.

## Architecture

```mermaid
flowchart TD
    P[Raster opaque prepass: depth, normals, smoothness] --> G[GI ray generation]
    G --> T[Unity traversal of the shared scene]
    T --> H[PathTracing hit evaluation]
    U[Quality: sparse cache update and resolve] --> C[Quality: radiance cache query]
    H --> C
    C --> I
    H --> S[Secondary lighting: direct light, emission, environment]
    T --> E[Miss: lighting environment]
    S --> I[Initial GI reservoir]
    E --> I
    I --> R1[Temporal resampling]
    R1 --> R2[Spatial resampling]
    R2 --> F[Final shading: visibility and MIS]
    F --> D[NRD RELAX]
    D --> O[Publication: indirect diffuse and specular]
    O --> M[Forward materials and subsurface scattering]
```

| Decision | Reason and consequence |
|---|---|
| Share the path tracing scene | RTGI and path tracing see the same instances, coverage, materials, lights and environment. The scene becomes a renderer-level service that either consumer can update; it no longer depends on the path tracing pass existing. |
| Shade secondary surfaces as standard Lit | Secondary hits run each material's `PathTracing` entry point, so inputs, alpha clipping, shadow layers and colored transmission behave as in path tracing, and then shade Skin, Hair and Fabric as a standard Lit surface. A secondary surface is seen only through indirect light, where its specialized lobes cost far more than they contribute. Lit is reduced the same way in Performance mode and keeps its own model in Quality mode. No second material system is introduced. |
| Resample with the RTXDI SDK | Reservoirs, temporal and spatial resampling, Jacobians, bias correction and final shading use the RTXDI SDK's GI resampling; the application bridge is IllusionRP's. Tracing and secondary shading share one ray generation pass, because the secondary surface keeps its IllusionRP material model instead of a reduced material record. |
| Denoise with NRD | Diffuse and specular are denoised as separate signals with their own hit distances, normals, roughness and history. RELAX is the default; REBLUR is selectable. |
| Integrate through the raster seams | Receivers come from the opaque prepass, lights that raster evaluates are not repeated, misses see the lighting environment, and specular composes with SSR and reflection probes without counting any part of the lobe twice. |
| Publish demodulated signals | Materials keep applying their own albedo, Fresnel, FGD, occlusion and layer multipliers exactly once, as they do for SSGI and SSR. The diffuse signal needs no receiver albedo. |
| Keep raster direct lighting | Lights that raster evaluates at the receiver are not repeated by RTGI's first bounce. |
| Offer Performance and Quality modes | Performance mode lights the secondary surface once, by light sampling alone. Quality mode continues the path for multiple bounces. |
| End Quality paths in SHaRC | The raster receiver is the primary surface and the GI ray's hit is the first indirect bounce, where the cache may be queried. The cache's update, resolve and query passes run unchanged, at the cost of voxel-scale averaging and lag at secondary surfaces. |

## Shared scene

- **Owner.** One scene service per renderer feature owns the acceleration structure, the instance and material tables, emissive geometry, the collected lights with their rectangle light proxies, and the environment importance data. Path tracing and RTGI are its consumers. Accumulation, stable planes, Ray Reconstruction and the adaptive light sampling feedback that path tracing derives from its own camera outputs stay with path tracing.
- **Participation.** The Path Tracing Volume's participation layers and directional light angular size define the scene for both consumers, so RTGI traces the scene that path tracing renders. A renderer participates when it is an enabled mesh or skinned mesh renderer, active in its hierarchy, on a participation layer, with a ray tracing mode other than Off, and either saved with its scene or created by the pipeline itself, as rectangle light proxies are.
- **Update.** Each participation configuration is built at most once per frame, whichever consumer requests it first; its LOD selection follows that first camera, as in path tracing. A camera that renders RTGI requests it before URP culls the camera, because collecting skinned renderers into the acceleration structure after the camera's shadow casters are culled invalidates that culling. Path tracing cameras cull no raster shadow casters and request it while the render graph records.
- **Change tracking.** A configuration finds its participating renderers with one scene scan, then follows the engine's change notifications: renderer creation, destruction, enablement, activity, layer, ray tracing mode, shadow casting, rendering layers, materials, property overrides, mesh and parent, plus material content and mesh data. Only the renderers these touch are read again, so per-frame cost follows the changes rather than the scene size; the acceleration structure is still culled every frame for transforms and LOD. A configuration that goes more than 32 renderer frames without an update scans again, because the gap can outlast the engine's change history. A content change to a referenced material advances the scene state that path tracing accumulation and RTGI validation compare, even when the tables are unchanged; rewriting a material or property override with identical values does not. Mesh data written in place, such as simulated cloth, advances it only when the tables change.
- **Lifetime.** A configuration that no consumer has used for 60 frames is released; disposing the feature releases the service.
- **Availability.** The service exists when either path tracing or RTGI is enabled in the renderer settings and the device supports ray tracing.

## Receiver

- **Surface.** The receiving surface is the opaque raster surface: the camera depth before transparent post-depth, the camera normals and the Forward GBuffer smoothness. Positions are reconstructed through the projection the camera depth was rendered with, jitter included, so perspective, orthographic, lens shifted and oblique cameras all receive where raster drew. The view direction points toward the camera position, or against the camera's forward direction for an orthographic camera. Pixels at the far plane are invalid and produce no sample.
- **Normal.** The receiver normal is the camera normal, including GBuffer normal overrides and wet normals. Skin's low-frequency ambient normal and the original normal that wet surfaces keep for indirect diffuse are approximated by it. It also serves as the geometric normal for the sampling hemisphere and ray offsets: ray origins move along it by the ray bias plus a small fraction of the view depth, since a reconstructed position loses precision with distance.
- **Material.** No receiver material record beyond the Forward GBuffer smoothness exists. Both signals are demodulated, so the receiver is shaded with a white diffuse albedo and a white specular reflectance, and its own albedo, reflectance and Fresnel are applied by the Forward materials. Specular is evaluated at a perceptual roughness of at least 0.03.

## Secondary surfaces

- **Traversal.** GI rays and their visibility rays traverse the shared scene with path tracing's instance masks, coverage and shadow layer rules. Visibility rays return colored transmission. In Performance mode, GI rays pass through every surface that transmits shadows, transparent surfaces and Lit's thin and refractive transmission; final shading's visibility test still applies their colored transmission.
- **Material.** A secondary hit evaluates its material inputs through its `PathTracing` entry point. Skin, Hair and Fabric are then reduced to a standard Lit surface: diffuse color, specular color as F0, perceptual roughness, shading normal and emission. Skin loses subsurface scattering and its second specular lobe, Hair its strand lobes and dual scattering, and Fabric its sheen, anisotropy and transmission. In Performance mode Lit is reduced the same way: it keeps its coat and loses transmission, and every specular lobe, the coat included, has a perceptual roughness of at least 0.5. In Quality mode Lit keeps its own model, including coat and transmission. Path tracing keeps the full material model.
- **Ray kind.** GI rays are scattered rays, not camera chains: Unlit surfaces contribute only their baked emission, and multiply overlays and camera-only backdrops are passed through.
- **Lighting.** The secondary vertex is lit with the [path tracing lighting contract](path-tracing.md#lighting-and-path-integration): next-event estimation over the shared light set (directional, point, spot and rectangle lights, emissive geometry and the environment), physical attenuation, no range cutoff or cookies, and rendering layer semantics evaluated for the secondary renderer. In Performance mode light sampling alone lights the secondary surface, which scatters no ray. In Quality mode, every vertex that the cache does not resolve also scatters a ray, as a path tracing vertex does, and combines light sampling by MIS with the emission that ray reaches. Light sampling is always on, whatever the Path Tracing Volume selects.
- **Raster lights.** A GI ray from the receiver that reaches the emitter of a light that raster evaluates directly, such as a rectangle light proxy or a directional light's disc, adds no radiance. Those lights reach the receiver only through secondary surfaces. Emissive geometry and the lighting environment are not raster lights and contribute when reached.
- **Environment.** Rays that leave the scene see the lighting environment, never the camera background.
- **Estimate controls.** The luminance of each initial sample's radiance is clamped before resampling, so that rare caustic paths, such as a glossy surface reflecting the sun toward a diffuse receiver, do not spread through reuse. The limit is a multiple of the radiance that the current exposure maps to middle gray, 100 by default; 0 disables the clamp and keeps the estimate unbiased.

## Radiance cache

Quality mode only.

- **Cache.** Each camera owns one SHaRC cache of 2^22 entries: hash entries, per-frame accumulation and resolved radiance, 160 MiB, cleared when created and on every history reset. The grid is centered on the camera position, with logarithm base 2, scene scale 50 and level bias 0. Entries store outgoing radiance including albedo, without material demodulation, directional encoding or responsive lighting. An entry leaves out its surface's own emission, so that light sampling's MIS stays intact: an update path passes on the emission a hit adds to the vertex before it, and emissive hits are never queried.
- **Update.** Each frame, one random receiver in every 5×5 block of GI pixels traces an update path. Its GI ray leaves the receiver as an initial sample's does. Every hit is shaded with Quality mode's secondary-surface material and lighting contract, with every specular lobe, the coat included, at least 0.4 in perceptual roughness, adds its light sampling and emission to the cache, and continues by sampling its BSDF, up to eight vertices; the cache's resampling ends paths early. A miss adds the lighting environment. The receiver is not cached, and the GI ray from it follows the raster light rule.
- **Resolve.** A compute pass resolves every entry each frame, accumulating up to 32 frames and evicting entries that received no sample for 64 frames.
- **Query.** An initial sample queries the cache at its GI ray's hit when the segment is longer than √3 voxels and the cone of the receiver lobe that launched it is wider than a voxel. A cached value becomes the sample's secondary radiance and ends the path. Otherwise the hit is lit and scatters as a path tracing vertex, and the next hit is queried with the same tests, up to eight vertices. Emissive hits, raster light emitters included, are not queried and stay path tracing vertices, so that their emission keeps its weight against light sampling; a raster light's emitter reached from the receiver still adds no radiance.
- **Requirements.** The cache needs native 16-bit shader types and 64-bit buffer atomics. A device without them runs RTGI in Performance mode and reports it once.

## Resampling

- **Initial sample.** Each valid receiver pixel traces one GI ray per frame. The specular lobe is chosen with the probability of its reflectance against the diffuse albedo, sampled from GGX's visible normals, and the diffuse lobe from the cosine distribution; the sample's density is the mixture of both lobes, with the exact visible-normal density for the specular lobe. Without specular, every ray samples the cosine distribution. The sample is the secondary point, its normal and its outgoing radiance toward the receiver. A reflection sampled below the receiver contributes nothing but still counts as a sample, so resampling averages over every sample the lobes generate.
- **Temporal.** Temporal resampling reprojects each receiver with the camera's motion vectors, which URP renders after the depth prepass for RTGI and which carry object and skinned motion; a camera without motion vectors reprojects through the previous frame's camera. Previous receivers are rebuilt from their stored device depth through the same reconstruction as current ones, so a receiver that did not move is the same point in both frames. The expected previous depth assumes a static surface. Previous receivers are accepted by depth, normal and material similarity, bounded by a maximum history length and a maximum history age. The age counts the frames since the pixel's history restarted, not since its sample was selected, because a sample age keeps zero-weight samples aging and dropping them biases dark receivers bright. No boiling filter runs.
- **Spatial.** Spatial resampling draws neighbors within a configured radius and sample count with the same similarity tests.
- **Bias correction.** Off, Basic or Ray traced, per resampling stage. Ray traced is the default, since Basic converges measurably darker where neighbors' samples are occluded.
- **Final shading.** Final shading reads the output of the last enabled resampling stage, re-tests visibility, and combines the resampled sample with the initial sample by MIS.
- **Measures.** Reconnection Jacobians use the secondary point and its geometric normal. Reservoir weights and the final estimator use the same target function.
- **Sources.** The imported RTXDI, NRD and SHaRC sources keep their NVIDIA notices, are distributed under the NVIDIA RTX SDKs license, and change only where marked `@IllusionRP`.

## Denoising

- **Denoiser.** NRD RELAX by default, REBLUR selectable: the diffuse variant while only diffuse is produced, the diffuse and specular variant with specular. None publishes the resampled signal undenoised, for validation.
- **Inputs.** View depth, motion, packed normal and roughness, and radiance with normalized hit distance per signal, all at the GI resolution and in NRD's units and encodings. In checkerboard mode the radiance inputs hold the traced half packed horizontally, NRD's checkerboard mode is black, and NRD's frame index is the resampling frame index, so its active field is the traced one; specular reaches NRD in the diffuse field. They describe the same frame, view and jitter. Motion is the camera's motion vectors; a camera without them gives NRD no surface motion, and NRD takes the camera motion from the view matrices.
- **Revision.** NRD and its shader front end are pinned together to one revision, with the normal and roughness encodings that the native library was built with.
- **Backend.** The backend is optional. It is compiled only when the UnityRHI package `top.kuanmi.unityrhi` 1.x is installed, only for the Editor and Windows x64, and runs only on Direct3D 12.

## Output and composition

- **Diffuse.** The diffuse signal is scene-linear irradiance at the receiver divided by π, not multiplied by any receiver albedo. It is published pre-exposed in `_IndirectDiffuseTexture` with `_IndirectDiffuseMode` RayTraced, so every receiver that reads SSGI reads it unchanged: materials apply their diffuse color, FGD, ambient occlusion, wet ambient factor and rendering layer multiplier once, and Skin takes it through `SubsurfaceDiffuse` into subsurface scattering. It replaces the SSGI, probe or baked diffuse that the receiver would otherwise use; lightmapped receivers keep their lightmaps, as with SSGI. Precomputed radiance transfer reflection normalization is unaffected.
- **Specular.** The specular signal is the incident radiance averaged over the receiver's white GGX lobe, the same quantity SSR provides: final shading divides the lobe integral by its directional albedo from the pre-integrated FGD table, and materials apply their own FGD. It is published pre-exposed through `_SsrLightingTexture`: valid SSR hits keep their weight, RTGI specular covers the rest of each receiver, and reflection probes cover only what neither provides. A receiver is a pixel with a valid RTGI receiver that SSR traces (stencil bit `0x04`, [Materials and Shaders](materials-and-shaders.md#stencil)) and that no transparent post-depth surface covers; Skin and Hair, which SSR does not trace, keep their reflection probes. No part of the lobe is counted twice.
- **Validity.** Validity is carried separately from color; a valid black result is not replaced by a fallback.
- **Resolution.** GI tracing, resampling and denoising run at full or half resolution. A half resolution pixel takes its receiver from the first camera texel of its 2×2 block, and both signals are upsampled to the camera size with depth-aware bilateral weights before publication, as SSGI's half resolution is.
- **Checkerboard.** While a denoiser runs, a black checkerboard mode can trace, resample and shade only half of the GI pixels each frame, alternating the field every frame; reservoirs cover that half. The denoiser reconstructs the other half, and the published signals cover every pixel. Every temporal reuse then comes from a neighboring pixel of the other field, so the clamped reconnection Jacobian applies to all of it, and high-variance interiors converge a few percent darker than without the checkerboard. Without a denoiser every GI pixel is traced.

## Enablement

| Feature | Setting | Switch | Volume | Additional conditions | Off for a camera |
|---|---|---|---|---|---|
| Ray traced global illumination | `rayTracedGlobalIllumination` | `r.rtgi` | `RayTracedGlobalIllumination` | Forward or Forward+; ray tracing and Direct3D 12 support; the selected denoiser's backend is available; Game base or Scene View camera; not path traced. | The camera uses SSGI when that applies, otherwise `_IndirectDiffuseMode` is Off; RTGI specular is absent from the SSR seam. |

- **Precedence.** When both SSGI and RTGI apply to a camera, RTGI is used and SSGI does no work.
- **Keyword.** `_SCREEN_SPACE_GLOBAL_ILLUMINATION` is on when `screenSpaceGlobalIllumination` or `rayTracedGlobalIllumination` is on.
- **Keyword.** `_SCREEN_SPACE_REFLECTION` is also on when `rayTracedGlobalIllumination` is on, and the opaque SSR stage then runs for every eligible camera so that `_SsrLightingTexture` is black where neither SSR nor RTGI specular applies.
- **Volume.** `RayTracedGlobalIllumination` holds the enable flag, the mode, resolution, the checkerboard and specular switches, resampling mode, bias correction, temporal and spatial parameters, the radiance clamp and the denoiser. Performance mode, half resolution, checkerboard and specular are the defaults; full resolution costs about four times the tracing time, and checkerboard halves it. Quality mode adds the radiance cache, 160 MiB per camera. With specular off, the diffuse signal is the one the receiver's cosine sampling produces, and `_SsrLightingTexture` is SSR's alone.
- **Unavailable backend.** An unavailable device or denoiser backend reports its reason once and leaves the camera on its non-RTGI path.
- **Validation.** In the Editor and development builds, `r.debug.rtgi.accumulate` averages the published result over frames while the view, scene and lights stay unchanged, so it converges to the estimate it is sampling.
- **Radiance cache views.** In the Editor and development builds, `r.debug.rtgi.view` replaces the final image of a Quality mode camera with a view of its radiance cache. Hash Grid colors each receiver by its cache voxel and level; Cache Occupancy draws the leading hash entries as blocks, colored when occupied and black when empty; Hash Collisions colors each receiver by how far the search for its entry probes from its hash slot, so a receiver without an entry shows where the search stopped. Pixels without a receiver are black, and Performance mode cameras keep their image.

## Frame order

| Event | Work |
|---|---|
| `AfterRenderingPrePasses + 6` | RTGI: in Quality mode the radiance cache update and resolve, then ray generation, secondary lighting, resampling, final shading, denoising, diffuse publication in place of SSGI, and specular merged into the opaque SSR result, which `BeforeRenderingOpaques` publishes. |

The scene service updates before culling for RTGI cameras and while the render graph records for path tracing cameras; its GPU build is declared as the first work of the first consumer in the frame.

## History and lifecycle

- **Per camera.** Quality mode's radiance cache, the reservoirs, secondary surface records, the previous receiver depth, normal and roughness with the previous view, and the NRD instance with its history belong to one camera.
- **Reset.** History resets on the camera's first frame, a camera cut (a move of more than two meters or a turn of more than 45 degrees), a size, resolution mode or checkerboard change, a gap of more than a few rendered frames such as a switch to path traced rendering, and any change to the estimate's definition such as the mode, resampling mode, denoiser, radiance clamp or participation layers. A reset also clears the radiance cache. Normal motion is reprojected, not reset.
- **Release.** A camera's RTGI state is released once the camera has not rendered RTGI for 60 frames; disposing the feature releases every instance. The denoiser targets exist only while a denoiser runs, and the validation accumulation targets only while accumulation is on, so a camera without RTGI runs no RTGI work and keeps no RTGI resources beyond that delay.

## Build

A ray traced global illumination capability, aggregated from renderer settings, keeps the `PathTracing` material passes, the shared scene support shaders and the `_SCREEN_SPACE_GLOBAL_ILLUMINATION` and `_SCREEN_SPACE_REFLECTION` variants. Shaders that only path tracing uses are stripped when only RTGI is enabled, and the RTGI compute shaders are stripped when no renderer enables it; ray tracing shaders are always included. [Shader Variant Stripping](shader-variant-stripping.md#resource-stripping) owns the rules.

## Acceptance

- In a furnace scene, converged RTGI diffuse matches path tracing Reference limited to the same transport depth within the stated tolerance: the secondary vertex in Performance mode, eight vertices in Quality mode.
- In the character lookdev scene, converged RTGI matches that reference without receiver albedo or Fresnel applied twice, and without raster lights counted twice.
- Skinned characters in motion, camera cuts, resizes and switches between path traced and raster cameras produce no stale history.
- With RTGI off, SSGI, SSR, precomputed radiance transfer, wet surfaces, subsurface scattering and path tracing render as before.
- A player built with only RTGI keeps the passes and support shaders it needs, and a player built without it strips every RTGI shader that a build callback can reach.
- GPU time and resource peaks at 1440p, full and half resolution, are recorded for each mode.

## Open questions

- The furnace tolerance and the 1440p budget.
