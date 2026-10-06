# Materials and Shaders

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [ASE Shader Workflow](ase-shader-workflow.md), [Shader Variant Stripping](shader-variant-stripping.md), [Transparency](transparency.md), [Wet Surface Decals](wet-surface-decals.md), [Rendering Pipeline](rendering-pipeline.md) |

IllusionRP materials target Forward and Forward+. This contract connects material authoring, auxiliary rendering, screen-space consumers and build reachability. Existing URP GBuffer passes do not establish a supported Deferred material path.

## Ownership

- Templates own pass topology and fixed rendering contracts; graphs own material inputs; shared shading code owns surface and lighting evaluation. Generated shaders follow [ASE Shader Workflow](ase-shader-workflow.md).
- Main color and OIT are the final-color owners. Depth, normals, motion, shadows, subsurface diffuse and water data are auxiliary outputs.
- Material inspectors and asset import pipelines must keep surface options and enabled passes consistent. Programmatic import must not depend on an inspector having been opened. Pass synchronization alone does not replace keyword, queue or stencil normalization.
- Diffusion profile binding preserves both the profile asset identity and its runtime lookup identity. Clearing a profile clears both; an unsaved or incompatible binding is invalid.

## Shader families

Lit and Complex Lit share the general surface contract. Skin splits diffuse lighting when screen-space subsurface scattering runs. Hair separates core and fringe coverage. Fabric supports optional transparent-depth reception. Water supplies transparent reflection data. Unlit has no Forward GBuffer.

Template options select auxiliary passes such as shadows, motion and baking. Features that require Forward-only lighting keep that choice consistent across authored options and generated output.

## Pass contract

Pass LightModes are the public boundary between shaders, renderer lists and build stripping:

| LightMode | Purpose |
|---|---|
| `UniversalForward`, `UniversalForwardOnly` | Final surface color through URP. |
| `ForwardGBuffer` | Surface depth, normals and smoothness for screen-space lighting. |
| `SubsurfaceDiffuse` | Opaque Skin diffuse inputs, not final color. |
| `OIT` | Order-independent accumulation for enabled coverage, including opaque-queue Hair fringes. |
| `PostDepthOnly` | Transparent coverage depth and corresponding screen-space smoothness update. |
| `WaterSSRData` | Transparent water reflection surface information. |
| `ShadowCaster` | Standard, per-object and area-light shadow production. |
| `DepthOnly`, motion and baking passes | URP depth, motion and lightmap requirements; none replaces Forward GBuffer. |

Transparent Hybrid Lit and Complex Lit use either their main color pass or OIT, never both, and do not write Forward GBuffer. Opaque materials restore the main pass and Forward GBuffer and disable OIT. Hair intentionally uses both color owners for disjoint core/fringe coverage; see [Transparency](transparency.md#hair-fringe).

## Forward GBuffer

The renderer draws enabled Forward GBuffer passes before screen-space lighting. This is the material depth/normal prepass, not a Deferred lighting buffer. It owns opaque depth, world-space normals and smoothness, clears its camera-local output, and publishes it consistently to consumers.

Every contributing pass matches main-color geometry, alpha clipping, LOD transitions and culling. Transparent materials do not contribute; opaque-queue Hair cores do. Optional graph overrides may simplify screen-space normal and smoothness but cannot change coverage, stencil or depth.

Wet surfaces extend the surface description and update it before screen-space consumers; see [Wet Surface Decals](wet-surface-decals.md). [PRT baking](precomputed-radiance-transfer.md) reuses authored material inputs through a bake-only capture variant and keeps its output separate from normal rendering.

## Screen-space receivers

Opaque surfaces receive supported screen-space lighting subject to material opt-outs. Ordinary transparency cannot sample those opaque surface results. A transparent material that participates in post-depth may opt into screen-space reception; its coverage must match the depth it supplies. Post-depth receivers retain screen-space main-light shadows after the camera restores shadow-map keywords; ordinary transparent surfaces use shadow maps. Multipass Hair additionally supports screen-space indirect lighting.

Sampling eligibility and keyword reachability are different: global SSR/AO keywords remain camera-wide even where a material does not sample their output. [Shader Variant Stripping](shader-variant-stripping.md) preserves the reachable state.

## Stencil

Material stencil expresses ambient-occlusion opt-out and reflection reception without disturbing unrelated bits. Skin and Hair do not write those flags and use their default AO/SSR behavior. The material inspector owns flag synchronization; generated materials without it retain authored defaults.

Wet coverage uses a separate resource rather than claiming an existing stencil bit. VRS classification and XR motion overlap existing stencil meanings; they cannot be treated as independent material classifications. New consumers must reconcile existing writers before assigning meaning.

## Shader data layout

All passes of a material share a consistent material parameter layout, including optional features. Pass-local defines cannot change that layout. Texture resources remain outside the material constant buffer.

Generated and hand-written paths preserve coverage and material behavior across instancing, DOTS, SRP Batcher and GPU Resident Drawer. Runtime drawing and stripping agree on pass identity. Combined feature configurations must stay within platform resource limits; sampler sharing requires equivalent filtering and addressing, including platform-specific shadow behavior.

## Keywords

Renderer-capability keywords remain stable while runtime switches and Volumes control work through neutral inputs. Material-local options remain material-local. Changing a runtime switch cannot require a stripped material variant; see [Rendering Pipeline](rendering-pipeline.md#global-state).

## Rectangle area lights

Area-light quality and shadow filtering form one coupled capability. Cameras with no active lights publish an empty light set while retaining that capability's keyword state. Cookies do not introduce a material keyword axis.

Lit and Water use their surface diffuse/specular models, including optional coat; Water evaluates lighting only in its color pass. Skin evaluates diffuse/transmission in the split diffuse path when active and specular in the main path. Fabric supports diffuse and sheen but not area-light anisotropy. Hair uses its approximation and does not sample cookies.

All families adapt area-light energy to URP's lighting convention exactly once. Auxiliary reflection-data passes never duplicate final lighting.

## Fabric anisotropy

Anisotropy affects punctual direct lighting and follows the material's tangent frame, normal and smoothness. It blends consistently with the cloth sheen response. Environment reflections remain isotropic; area lighting does not apply anisotropy. Energy normalization is applied once at the shading-convention boundary.
