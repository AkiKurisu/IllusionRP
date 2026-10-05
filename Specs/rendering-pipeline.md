# Rendering Pipeline

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md) |

IllusionRP extends URP's Universal Renderer through `IllusionRendererFeature`. For every camera the feature decides which of its passes run, injects them at fixed render pass events around URP's own passes, and publishes the global state its shaders read. This spec defines ownership boundaries, per-camera enablement, frame order, camera eligibility, runtime switches and lifecycle. Resources, formats and history are owned by [Render Resources](render-resources.md).

## Ownership

- **URP.** Culling, the depth and depth-normal prepasses, the camera depth copy, motion vectors, main and additional light shadow maps, opaque, skybox and transparent drawing, built-in post-processing, camera attachments and the renderer lifecycle. IllusionRP adds work through renderer feature passes, frame data, the render graph and shader globals. An internal bridge provides access to URP state such as the actual rendering mode and main light shadow cascades.
- **`IllusionRendererFeature`.** Creates and disposes every IllusionRP pass, reads the serialized settings and decides per camera which passes to enqueue. A renderer holds at most one instance.
- **`IllusionRendererData`.** State shared by the feature's passes: the current camera's state, per-camera history, renderer-level render targets, global constants and neutral textures. `IllusionRendererData.Active` is the instance that set up the most recent camera.
- **`IllusionRenderPipelineResources`.** Shaders, compute shaders, lookup tables and textures, loaded by name from `Resources`.
- **Volume stack.** Per-camera blended parameters and enable flags within the renderer feature's enabled capabilities.
- **`IllusionRuntimeRenderingConfig`.** Runtime switches and debug view state; see [Runtime switches](#runtime-switches).
- **Scene inputs.** Objects that feed the renderer (wet surface decals, sun shaft casters, volumetric lights, per-object shadow casters, probe volumes) register with static registries or managers.

Forward and Forward+ are the supported rendering paths. The feature reads URP's actual rendering mode. In Deferred it sets `_DEFERRED_RENDERING_PATH` and skips the Forward GBuffer, wet surfaces and area lights.

## Enablement

A feature does work for a camera only when every layer allows it:

1. the serialized setting on the renderer feature, which is the capability;
2. the runtime switch in `IllusionRuntimeRenderingConfig`;
3. the feature's Volume component (`enable`, or `IsActive()` for post-processing);
4. camera eligibility ([Cameras](#cameras)) and, for post-processing, the renderer's and the camera's post-processing flags;
5. rendering path and device support: compute shaders, render and blend formats, per-image-tile shading rate.

Runtime switches and Volumes gate the capabilities enabled by renderer settings. Disabled features skip their rendering work while retaining neutral publication, global state resets and history invalidation.

| Feature | Setting | Switch | Volume | Additional conditions | Off for a camera |
|---|---|---|---|---|---|
| Ground truth ambient occlusion | `groundTruthAO` | `r.ssao` | `GroundTruthAmbientOcclusion` | Not offscreen depth. | Not enqueued. |
| Screen space reflection | `screenSpaceReflection` | `r.ssr` | `ScreenSpaceReflection` | Not offscreen depth; Reflection cameras never sample it. | `_SsrLightingTexture` is black while the setting is on. |
| Screen space global illumination | `screenSpaceGlobalIllumination` | `r.ssgi` | `ScreenSpaceGlobalIllumination` | Not offscreen depth or Reflection. | `_IndirectDiffuseMode` is Off. |
| Precomputed radiance transfer GI | `precomputedRadianceTransferGI` | `r.prt` | None | Baked probe transport matching the volume grid; not Reflection or Preview. | No solve work; publish neutral data for this camera. Eligible idle cameras still publish their committed generation. See [PRT](precomputed-radiance-transfer.md). |
| Contact shadows | `contactShadows` | `r.contactshadows` | `ContactShadows` | Reflection cameras never sample them. | `_CONTACT_SHADOWS` off. |
| Percentage closer soft shadows | `pcssShadows` | `r.pcss` | `PercentageCloserSoftShadows` (temporal accumulation only) | Not Reflection. | `_PCSS_SHADOWS` off, no temporal pass. |
| Rectangle area lights | `areaLights` | `r.arealights` | `AreaLighting` | Forward or Forward+. | The pass runs with zero lights and black atlases. |
| Subsurface scattering | `subsurfaceScattering` | None | `SubsurfaceScattering` | URP lighting debug leaves lighting active; not Reflection. | No split lighting or scattering. |
| Wet surface decals | `wetSurfaceDecals` | `r.wetsurface` | None | See [Wet Surface Decals](wet-surface-decals.md). | Wet state reset. |
| Screen-space refraction copy | `screenSpaceRefraction` | `r.refraction` | None | Not offscreen depth. | `_PreRefractionColorTexture` is black. |
| Transparent SSR | `transparentScreenSpaceReflection` | `r.ssr.transparent` | Through SSR | SSR in use, compute shaders and compute preference, not OpenGL ES 3. | See [Transparency](transparency.md). |
| WBOIT, transparent post-depth, overdraw | `orderIndependentTransparency`, `transparentDepthPostPass`, `oitTransparentOverdrawPass` | None | None | See [Transparency](transparency.md). | Not enqueued. |
| Stencil variable rate shading | `enableStencilVrs` | `r.vrs` | None | Per-image-tile shading rate support. | Not generated. |
| Convolution bloom | `convolutionBloom` | `r.bloom` | `ConvolutionBloom` | Post-processing on. | Not enqueued. |
| Volumetric fog | `volumetricFog` | `r.volumetricfog` | `VolumetricFog` | Post-processing on. | Not enqueued. |
| Sun shafts | `sunShafts` | `r.sunshafts` | `SunShafts` | Post-processing on. | See [Sun Shafts](sun-shafts.md). |
| DLSS Neural Rendering | `dlssNeuralRendering` | `r.dlssnr` | `DLSSNeuralRendering` | See [DLSS Neural Rendering](#dlss-neural-rendering). | Not enqueued. |

The Preview camera exclusions in [Cameras](#cameras) apply on top of this table. `preferComputeShader` and `r.computeshader` together select compute implementations where a pass has one, and only on devices with compute shader support.

## Global state

The feature sets these global keywords for every camera at `BeforeRendering`, so no camera inherits them from another:

| Keyword | On when |
|---|---|
| `_SCREEN_SPACE_OCCLUSION` | `groundTruthAO` |
| `_SCREEN_SPACE_REFLECTION` | `screenSpaceReflection` |
| `_SCREEN_SPACE_GLOBAL_ILLUMINATION` | `screenSpaceGlobalIllumination` |
| `_SCREEN_SPACE_SSS` | `subsurfaceScattering` |
| `_PRT_GLOBAL_ILLUMINATION` | `precomputedRadianceTransferGI` |
| `_TRANSPARENT_PER_OBJECT_SHADOWS` | `transparentReceivePerObjectShadows`, except for Preview cameras |
| `_SHADOW_BIAS_FRAGMENT` | `fragmentShadowBias` |
| `_DEFERRED_RENDERING_PATH` | The actual rendering mode is Deferred. |
| `AREA_SHADOW_MEDIUM`, `AREA_SHADOW_HIGH` | `areaLights` outside Deferred, selected by `areaShadowFilteringQuality`. With neither keyword, area lights are compiled out. |

- **Stable variants.** Except for `_TRANSPARENT_PER_OBJECT_SHADOWS`, these keywords follow serialized settings and the rendering path, not runtime switches, Volumes or camera types. SSR, SSGI, precomputed radiance transfer, area lights and wet surfaces publish a neutral input when they are off for a camera while their keyword stays on, as listed in [Enablement](#enablement).
- **Frame keywords.** The screen-space shadow producer sets `_CONTACT_SHADOWS` and `_PCSS_SHADOWS` and switches URP's main light shadows to screen space before opaques; URP's main light shadow keywords are restored after opaques, so transparent surfaces sample the shadow map.
- **Publication.** Global textures, buffers, constants and keywords are a shader compatibility interface. They own no resource and create no render graph dependency; see [Render Resources](render-resources.md).
- **State changes.** A pass that changes global state declares that it does, and the state is either reset each camera or restored at a defined event.
- **Debug views and switches.** Debug views visualize rendering state. Runtime switches gate feature work; when one turns a feature off, its producer and consumers enter their disabled behavior together.

## Frame order

URP records custom passes in render pass event order and, within one event, in the order the feature enqueues them. The table lists that order.

| Event | Work |
|---|---|
| `BeforeRendering` | Global setup, always first: per-camera state, shadow data, fixed exposure, global constants and textures ([Render Resources](render-resources.md)). Pre-integrated FGD tables, the global keywords, and the wet surface reset. |
| `AfterRenderingShadows` | Preview cameras only: per-object shadows reported as absent. |
| `BeforeRenderingPrePasses - 1` | Advanced tonemapping parameters and tonemap keywords. |
| `BeforeRenderingPrePasses` | Forward GBuffer (Forward and Forward+): clears the Forward GBuffer, then draws `ForwardGBuffer` passes into it, the camera normals and depth. |
| `AfterRenderingPrePasses` | Transparent pre-depth capture, depth pyramid, wet surface mask, normals and smoothness. |
| `AfterRenderingPrePasses + 1` | Transparent post-depth (`PostDepthOnly`). |
| `AfterRenderingPrePasses + 2` | Ground truth ambient occlusion. |
| `AfterRenderingPrePasses + 3` | Opaque screen space reflection. |
| `AfterRenderingPrePasses + 4` | Per-object shadow casters, area lights (light data, cookies, shadow atlas, globals), contact shadows. |
| `AfterRenderingPrePasses + 5` | Contact shadow spatial denoise, precomputed radiance transfer relight. |
| `AfterRenderingPrePasses + 6` | Screen space global illumination. |
| `BeforeRenderingOpaques` | Screen space reflection publication, screen-space shadows and their temporal filter, subsurface scattering. |
| `AfterRenderingOpaques` | Main light shadow keyword restore. |
| `AfterRenderingOpaques + 1` to `+ 3` | Pre-refraction color copy, water SSR data, transparent SSR. |
| `AfterRenderingTransparents + 1` | Weighted blended OIT. |
| `AfterRenderingTransparents + 2` to `+ 4` | Post-depth copy back, stencil VRS generation, transparent overdraw. |
| `AfterRenderingTransparents + 5` | Color pyramid, then depth and normal history when required. |
| `BeforeRenderingPostProcessing - 4` | Sun shafts. |
| `BeforeRenderingPostProcessing - 3` | Volumetric fog. |
| `BeforeRenderingPostProcessing - 2` | Automatic exposure. |
| `BeforeRenderingPostProcessing - 1` | History color copy, then convolution bloom. |
| `AfterRenderingPostProcessing` | Stencil VRS debug view, then post-processing history bookkeeping. |
| `AfterRenderingPostProcessing + 1` | DLSS Neural Rendering. |
| `AfterRenderingPostProcessing + 2` | Full-screen debug views. |

- **Dependencies.** A pass that needs another pass's output declares the read on the recorded resource. New passes that depend on each other use different events or one recorded chain; they never rely on enqueue order within an event.
- **URP passes.** URP's prepass, depth copy, motion vectors, opaque, skybox, transparent and post-processing passes stay at their standard injection points; IllusionRP never reorders them.
- **Prepass.** Which material passes feed URP's prepasses and the `ForwardGBuffer` pass is owned by [Materials and Shaders](materials-and-shaders.md).
- **Per-object shadow source.** Before culling, each camera resolves its per-object shadow light; changes the feature makes to light state for that camera are restored when the camera ends. See [Directional Per-Object Shadows](directional-per-object-shadows.md).
- **Setup.** Setup runs while the render graph records, before any other IllusionRP pass. Each pass still validates the frame data handles and resources it needs and skips its work, or publishes its neutral input, when they are missing.

## Cameras

Each pass applies its own camera rules; this table is their union.

| Camera | Behavior |
|---|---|
| Game, base | All features. |
| Game, overlay | As base, except wet surfaces. DLSS only when the camera resolves the final target. |
| Scene View | All features except SSR accumulation, exposure debug and DLSS. TAA follows URP. When the Exposure Volume is not in Fixed mode, Scene View may use fixed exposure at the middle of the Volume's limits instead of automatic exposure: the Volume's `sceneViewPreferFixedExposure` decides when overridden, otherwise `r.sceneview.exposure.fixedfallback` (on by default). |
| Reflection | No SSR, SSGI, contact shadow or PCSS sampling; no depth pyramid, relight, subsurface scattering, wet surfaces or DLSS. Exposure is neutral: no fixed or automatic exposure runs, and the current and previous exposure are a multiplier of 1, so captured radiance stays scene-linear. |
| Preview | No ambient occlusion, SSR or SSGI work, contact shadows, PCSS, area lights, relight, subsurface scattering, wet surfaces, transparent post-depth, WBOIT, overdraw, refraction copy (black), water or transparent SSR, bloom, fog, sun shafts or exposure pass. Per-object shadows are absent and `_TRANSPARENT_PER_OBJECT_SHADOWS` is off. |
| Offscreen depth (depth-format target) | No depth pyramid, ambient occlusion, SSR, SSGI, wet surfaces, water or transparent SSR; the refraction copy is black. |
| Probe capture (Editor) | A camera that renders a precomputed radiance transfer capture records only the capture pass and no other IllusionRP work. |

- **Shared passes.** Every camera of a renderer uses the same pass instances and renderer-level render targets, which reallocate to the current camera's size. Temporal state lives only in per-camera history; see [Render Resources](render-resources.md).
- **Camera state.** Setup establishes the current camera's keywords, globals and resource bindings.

## DLSS Neural Rendering

DLSS Neural Rendering is an experimental full-resolution post-process that replaces the camera color using color, depth and motion vectors.

- **Backend.** The backend is optional. It is compiled only when the UnityRHI package `top.kuanmi.unityrhi` 1.x is installed, which defines `ILLUSION_DLSSNR_EXPERIMENTAL`, only for the Editor and Windows x64, and runs only on Direct3D 12 with the neural rendering runtime available. `IsDLSSNeuralRenderingAvailable` reports whether the setting is on and the backend is usable.
- **Cameras.** Game cameras that resolve the final target with post-processing on. HDR output and XR bypass it, and an unavailable runtime skips it, each with a one-time warning. A failed initialization or dispatch reports one error and leaves the input color as the result.
- **Order.** It runs at `AfterRenderingPostProcessing + 1`, after URP post-processing, and requires an intermediate color target.
- **Parameters.** The `DLSSNeuralRendering` Volume supplies preset, style, intensity, local tone, local structure and skin structure strengths, auto mask, UI correction, motion vector scale, and the camera cut distance and angle.
- **History.** Each camera has its own context, created at the camera's color size and re-created when the size changes. History resets on the first frame, after a skipped frame, when the settings change, when the camera moves farther than the cut distance or turns more than the cut angle, and when the projection changes. `ResetDLSSNeuralRenderingHistory()` and the Rendering Debugger's Reset History button reset every camera.
- **Input debug.** `r.debug.dlssnr` shows a prepared input (color, motion vectors, motion magnitude, device or linear eye depth) and skips evaluation.

## Runtime switches

`IllusionRuntimeRenderingConfig.Get()` returns one process-wide, runtime-only configuration shared by every renderer. When the optional Ceres package is installed its properties are console variables; without it they are set from code. An application may drive them from its own settings.

| Variable | Rendering Debugger (Illusion Features) | Turns off |
|---|---|---|
| `r.ssr`, `r.ssr.transparent`, `r.refraction`, `r.ssgi`, `r.prt`, `r.ssao`, `r.wetsurface`, `r.arealights` | Lighting | The matching feature in [Enablement](#enablement). |
| `r.contactshadows`, `r.pcss` | Shadows | Contact shadows, PCSS. |
| `r.volumetricfog`, `r.bloom`, `r.sunshafts` | Post Processing | Volumetric fog, convolution bloom, sun shafts. |
| `r.dlssnr` | Neural Rendering | DLSS Neural Rendering. |
| `r.computeshader`, `r.vrs` | Graphics API | Compute implementations, stencil VRS. |

- **Debug views.** `r.debug.velocity`, `r.debug.ssr`, `r.debug.ssr.transparent`, `r.debug.perobjectshadow`, `r.debug.vrs`, `r.debug.arealightshadowatlas` with `.min` and `.max`, `r.debug.ssshadow`, `r.debug.exposure` with its histogram options, and `r.debug.dlssnr` with `.motionrange` and `.depthrange`. They appear in the Illusion Debug panel. Debug passes exist only in the Editor and development builds; the exposure debug view draws only for Game cameras, the area light atlas view only while area lights run, and the VRS view only while VRS runs.
- **Editor-only variables.** All debug variables, `r.vrs` and `r.sceneview.exposure.fixedfallback` are console variables only in the Editor.
- **Panels.** The Illusion Features and Illusion Debug panels exist in the Editor and development builds while at least one renderer feature instance is alive. Resetting the Rendering Debugger restores every panel value to its default. The Illusion Debug panel also reports DLSS backend, graphics API, runtime and last result status.
- **Async compute.** Async compute is disabled in this version and has no switch.

## Lifecycle

- **Create.** `Create` releases its previous resources, then loads `IllusionRenderPipelineResources`; in a player a missing asset fails an assertion, and in the Editor the feature builds nothing. It creates the renderer data, managers, every pass, the DLSS backend when present, and registers the Rendering Debugger panels.
- **Settings.** World scale, compute preference, history color, indirect diffuse rendering layers and the per-object shadow settings are read again at every camera setup. Settings that size persistent resources when passes are constructed, such as `areaLightCookieAtlasSize` and `areaLightCookieFormat`, apply when the feature is re-created.
- **Dispose.** Disposing the feature releases every pass, the renderer data with all per-camera states and history, renderer-level render targets, buffers and neutral textures, the DLSS backend with its per-camera contexts, event subscriptions and the debug panels, and URP constant buffers. Each object that allocates a render target, buffer, material, native array or subscription releases it in its own dispose. Tables shared across feature instances, such as the area light LTC table, are reference counted.
- **Camera state.** Per-camera state is created on a camera's first frame and released when the camera is destroyed or has not rendered for 600 frames. DLSS contexts are released when their camera is destroyed.
