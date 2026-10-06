# Water

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Transparency](transparency.md), [Materials and Shaders](materials-and-shaders.md), [Path Tracing](path-tracing.md) |

Water combines refraction with reflected lighting. Raster stages and third-party participation are governed by [Transparency](transparency.md); this specification owns Water material behavior.

## Template

Transparent Water contributes a reflection-data pass matching its Forward geometry, normal and coverage. Opaque Water omits it. The graph's scene-color input uses the pipeline refraction copy; reflection and depth inputs follow the selected reflection mode.

Templates and generated shaders follow [ASE Shader Workflow](ase-shader-workflow.md). Reflection-data production never duplicates final color lighting.

## Reflection modes

| Mode | Contract |
|---|---|
| Screen Space, default | Pipeline transparent SSR with environment fallback; water reflection-data pass enabled. |
| Legacy | Material-local raymarch using scene depth and prior color; water reflection-data pass disabled. |

Material options, keywords and pass state remain synchronized, including changes made outside the inspector. Materials may mix modes without scheduling or disabling pipeline stages. Both modes use the same pre-refraction color.

## Screen Space mode

SSR confidence blends valid reflected radiance with the environment after exposure is reconciled. Invalid or offscreen samples have no confidence. Unavailable SSR therefore falls back to reflection probes or sky rather than forcing black.

## Legacy mode

Legacy reflection traces scene depth and reprojects prior color. Invalid reprojection is a miss. Missing scene depth uses its virtual sky behavior independently of the far plane. Historical transparent color may disagree with tested depth, so occlusion and parallax remain approximate.

## Refraction

Refraction samples opaque color independently of SSR. Distortion falls back to undistorted coordinates when it leaves the screen or crosses foreground geometry. Depth-dependent shore and thickness terms read behind the water, not the water's own post-depth. Screen sampling respects stereo and render-target coordinate conventions.

## Shipped Water shader

Raster Water uses scrolling detail normals, thickness tint and Fresnel composition. It casts no shadows and writes depth, so later transparent and OIT draws test against its surface. Alpha controls shoreline coverage, not another copy of the transparency already composited into its color; applying both would double-blend the background.

## Path tracing

Path-traced Water is a refractive Lit interface over an absorbing, non-scattering medium, not a separate material model. Authored normal, index of refraction, interface smoothness and absorption color/distance define its physical inputs.

The interface has no diffuse or emissive response. A path escaping the scene inside the absorbing medium contributes no light. The current shadow path crosses water without attenuation. Raster depth tint, refraction copies, shoreline fade and reflection-mode choices do not become path-tracing inputs; geometry defines the shoreline. See [Path Tracing](path-tracing.md).
