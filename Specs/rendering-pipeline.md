# Rendering Pipeline

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md) |

IllusionRP extends URP's Universal Renderer through a renderer feature rather than owning a separate raster pipeline. Forward and Forward+ are the supported material paths. URP retains its standard stages; IllusionRP inserts feature work and publishes the state its materials consume.

## Ownership

- URP owns camera culling, standard shadows, attachments, opaque/skybox/transparent drawing, motion and built-in post-processing.
- The Illusion renderer feature owns pass lifetime, serialized capabilities and per-camera scheduling. Each renderer has at most one instance.
- Renderer state owns shared current-frame resources and isolated per-camera histories; [Render Resources](render-resources.md) defines their lifetimes.
- Resource assets include required shaders and lookup data. Volumes blend camera parameters; runtime configuration gates enabled capabilities.
- Scene components supply registered inputs such as decals, light anchors, shadow casters and probes without transferring scene ownership.

Deferred cameras skip Forward GBuffer, wet surfaces and area lights. Existing Deferred pass topology does not imply support for IllusionRP's Forward material contract.

## Enablement

Work requires serialized capability, runtime permission, active Volume where applicable, eligible camera and supported rendering path/device. Post effects additionally require pipeline and camera post-processing. Compute preference never overrides missing compute support.

Turning off a feature skips its work while preserving neutral publication, state reset and history invalidation. Runtime and Volume controls cannot enable a capability removed from the build. Feature-specific eligibility belongs to its specification; [Transparency](transparency.md), [PRT](precomputed-radiance-transfer.md) and [Wet Surfaces](wet-surface-decals.md) define their producer chains.

## Global state

Camera setup establishes global state before IllusionRP consumers run, so no camera inherits another camera's keywords or bindings. Stable SSR, AO, SSGI, subsurface, PRT, area-light and material capability states follow serialized settings. Runtime disablement uses neutral inputs without requiring another material variant.

Frame-varying shadow production differs: screen-space main-light shadows are enabled for opaque drawing and restored to shadow-map state after opaques. Transparent per-object shadow eligibility also respects preview-camera exclusions.

Global names form a shader interface but neither own resources nor establish graph dependencies. A producer that changes state must restore it or publish its replacement at a defined boundary. [Shader Variant Stripping](shader-variant-stripping.md) follows these actual reachable states.

## Frame order

| Boundary | IllusionRP work in dependency order |
|---|---|
| Camera setup | Camera state, exposure inputs, global capabilities, lookup publication and wet reset. |
| Before/at depth prepass | Tonemapping setup, then Forward GBuffer for supported paths. |
| After prepass | Preserve opaque depth; depth pyramid; wet surface updates; transparent post-depth; AO; opaque SSR; per-object shadows, area lights and contact shadows; contact denoise and PRT; SSGI. |
| Before opaques | SSR publication, screen-space shadow production/filtering and split subsurface lighting. |
| After opaques, before skybox | Restore main-shadow state; pre-refraction copy; water data; transparent SSR. |
| URP transparents | Conventional transparent color. |
| After transparents | OIT; post-depth restore; stencil VRS and transparent overdraw; color pyramid and required depth/normal history. |
| Before post-processing | Sun shafts; volumetric fog; automatic exposure; history-color copy; convolution bloom. |
| After post-processing | VRS debug and history bookkeeping; optional DLSS Neural Rendering; final debug views. |

New dependencies use explicit recorded resource chains or distinct injection points, not incidental enqueue order. URP's own stages remain at their standard points. Per-object source selection and temporary light-layer changes happen before culling and are restored when the camera ends.

## Cameras

| Camera | Contract |
|---|---|
| Base Game | Eligible for all supported features. |
| Overlay Game | Uses base-camera feature rules except wet surfaces; DLSS only on the final resolving camera. |
| Scene View | No SSR accumulation, exposure debug or DLSS. TAA follows URP; automatic exposure may use the configured fixed fallback. |
| Reflection | No opaque SSR/SSGI, contact or PCSS sampling, depth pyramid, relight, subsurface, wet surfaces or DLSS. Exposure is neutral and captured radiance stays scene-linear. |
| Preview | No IllusionRP screen-space lighting, per-object/area shadows, relighting, subsurface, wet surfaces, transparency extensions or post-processing work; relevant outputs are neutral. |
| Offscreen depth | No screen-space lighting pyramid, AO, SSR, SSGI, wet or water-reflection work; refraction is neutral. |
| Probe bake capture | Only the capture chain, without unrelated IllusionRP effects. |

Pass instances may be shared between cameras; temporal contents must not be. Every camera establishes its own state and valid bindings before consumption.

## DLSS Neural Rendering

This optional experimental full-resolution stage replaces post-processed color using matching color, depth and motion inputs. It requires its backend/runtime on supported Windows DX12 configurations. Only final-resolving Game cameras with post-processing participate; HDR output, XR and unavailable backends bypass it with diagnostics.

Each camera owns its context. Size, configuration, camera cuts, projection changes or skipped work invalidate history; explicit reset affects all contexts. Initialization/dispatch failure preserves the input image. Input debug displays prepared inputs without evaluation.

## Runtime switches

Runtime configuration is shared across renderers but is not serialized authoring state. Applications may drive it directly or through the optional console integration. Feature controls and debug views are distinct: one gates rendering work, the other visualizes results.

Debug rendering and panels are limited to Editor/development builds. Panels exist while renderer features are alive and reset consistently. Async compute is currently disabled.

## Lifecycle

Feature creation releases replaced resources, establishes renderer state and required resources, creates owned work and registers diagnostics. Missing required resources prevent a usable feature rather than leaving partially initialized consumers.

Camera-dependent settings are read during setup; settings that determine persistent allocation apply when the feature is recreated. Feature disposal releases passes, shared state, camera histories, contexts and subscriptions through their owners. Shared tables remain alive while referenced. Destroyed or long-inactive cameras release temporal state.
