# Render Resources

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-06 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md) |

IllusionRP passes exchange data through URP frame data, render graph textures, renderer-level render targets, per-camera history and global shader publication. This spec owns the resource classes, the resources that cross passes or reach shaders, the depth that screen-space effects use, history lifetime, render graph declarations, disabled-state publication and release. Pass order and camera rules are owned by [Rendering Pipeline](rendering-pipeline.md).

## Resource classes

| Class | Use | Owner |
|---|---|---|
| URP frame data | Camera color, depth, normals, motion vectors and URP's other per-frame handles. | URP. IllusionRP passes declare reads and writes, and replace a handle only where a spec says so: the transparent post-depth, automatic exposure on a history reset, and DLSS Neural Rendering. |
| Render graph transient | Intermediates of one pass or one recorded chain. | The pass that creates it. |
| Renderer-level target | A current-frame result imported into the render graph so later passes, or the next camera, can use it without re-creating it. | `IllusionRendererData` or the pass that allocates it. |
| Per-camera history | Temporal state that survives to the camera's next frame. | The camera's state in `IllusionRendererData`. |
| Persistent table | Lookup tables and atlases that do not depend on the camera. | The pass or shared object that builds it. |
| Global publication | Stable shader names for produced resources. | The publishing pass; it never changes the resource's owner. |

- **Renderer-level targets are not history.** They are reallocated to each camera's target size and reused by cameras rendered one after another. Only per-camera history carries results from one frame of a camera to its next frame.
- **Internal formats.** Scratch formats inside one feature belong to its implementation. Formats of resources that cross passes or reach shaders are part of this contract.

PRT global SH/ready buffers and byte-budgeted sector residents belong to its renderer pass. Sector scratch results commit only after all frame-start feedback reads, with no full-grid copy or CPU readback gate. Evicting residents remain charged until asynchronous shadow-cache preservation completes. Dirty window slots and typed layout data are published per camera; moving a window rebuilds only that small window. SH is signed FP32, and packed intensity/validity is R32_UInt. Runtime sampling uses nine validity/intensity-weighted FP32 coefficient pages with validity in alpha. World lighting/shadows use explicit graph inputs and never require runtime RT. See [Precomputed Radiance Transfer](precomputed-radiance-transfer.md) for bake ABI, iteration, snapshot and sampling contracts.

## Pipeline resources

`IllusionRenderPipelineResources` is one asset loaded by name from `Resources`. It references every shader, compute shader, lookup texture and mesh the passes use, so that players include them. A shader that a pass finds by name is listed in its `alwaysIncludedShaders`; debug shaders are listed in `debugShaders`.

## Shared resources

| Resource | Format | Producer | Consumers | Lifetime |
|---|---|---|---|---|
| `_ForwardGBuffer` | See [Materials and Shaders](materials-and-shaders.md#forward-gbuffer). | Forward GBuffer pass; the wet smoothness pass and transparent post-depth replace or update it. | SSR, SSGI and their denoisers; wet surfaces. | Renderer-level target, cleared every camera. With wet surfaces the published texture is the wet result, a transient. |
| `_CameraNormalsTexture` | URP's normal encoding. | Second target of the Forward GBuffer pass; wet surfaces blend normals back into it. | Ambient occlusion, SSR, SSGI, normal history. | URP frame data. |
| `_DepthPyramid` | R32 float packed mip atlas; consumers bind its mip offsets as `_DepthPyramidMipLevelOffsets`. | Depth pyramid pass, from the opaque camera depth. | Ambient occlusion, SSR, SSGI, transparent SSR, depth history. | Renderer-level target. Not built for Preview, Reflection or offscreen depth cameras. |
| `_ScreenSpaceOcclusionTexture`, `_AmbientOcclusionParam` | R8, falling back to B8G8R8A8 on the raster path or R32 float on the compute path. | Ambient occlusion. | Opaque lighting. | Transient, published for the frame. |
| `_SsrLightingTexture` | Internal. | Opaque SSR; black when SSR is off for the camera. Transparent SSR publishes its own result under the same name after opaques ([Transparency](transparency.md)). | Opaque materials, then transparent surfaces. | Frame-scoped. The opaque result is written into the camera's SSR accumulation history; only Game cameras using PBR accumulation on the compute path accumulate across frames. |
| `_IndirectDiffuseTexture` | Internal. | SSGI. | Opaque materials while `_IndirectDiffuseMode` is ScreenSpace. | Frame-scoped, with two history buffers when denoising. |
| `_ScreenSpaceShadowmapTexture` | See [Directional Per-Object Shadows](directional-per-object-shadows.md). | Screen-space shadows, or their temporal filter. | Opaque lighting. | Renderer-level target; temporal history per camera. |
| `_ContactShadowMap` | Internal. | Contact shadows and their optional spatial denoise. | Screen-space shadows. | Renderer-level target. |
| `_SubsurfaceLighting`, `ShaderVariablesSubsurface` | Internal. | Subsurface scattering. | Skin materials with `_SCREEN_SPACE_SSS`. | Pass-owned targets reallocated per camera. |
| `_ColorPyramidTexture` | RGBA16 float with mips. | Color pyramid, from the camera color after transparents. | Next frame's SSR, SSGI, transparent SSR and history color. | Per-camera history. |
| `_CameraPreviousColorTexture` | Camera color format. | History color copy, before convolution bloom, when `requireHistoryColor` is on and TAA is off. | History color. | Renderer-level target. |
| `_HistoryColorTexture` | Selected source. | Setup. | Shaders that read the previous frame's color. | Global publication; see [History color](#history-color). |
| `_ExposureTexture`, `_PrevExposureTexture` | 1×1 RG32 float: R is the exposure multiplier, G the EV100. | Fixed exposure at setup or automatic exposure before post-processing. | Lighting, SSGI, post-processing. | Per-camera history, two buffers. |
| `ShaderVariablesGlobal`, `_MainLightShadowCascadeBiases`, `_AmbientProbeData` | Constant buffer, vector array, buffer of seven float4. | Setup. | All IllusionRP shaders. | Pushed every camera; see [Global constants](#global-constants). |
| `_PreIntegratedFGD_GGXDisneyDiffuse`, `_PreIntegratedFGD_CharlieAndFabric` | Internal. | Pre-integrated FGD passes. | Lighting. | Persistent tables, built once and bound every camera. |
| `_WetSurfaceMask`, coverage | R32 float mask; R8 coverage. | Wet surface mask pass. | Wet normals, wet smoothness, Forward materials. | Mask is a pass-owned target; coverage is transient. See [Wet Surface Decals](wet-surface-decals.md). |
| `_PreRefractionColorTexture`, `_WaterSSRNormalTexture`, transparent depth, OIT targets | See [Transparency](transparency.md). | Transparency passes. | Transparent and water shaders. | Renderer-level targets and transients. |
| `StencilVRSData` | Shading rate color mask and shading rate image. | Stencil VRS generation. | Transparent overdraw. | Transient frame data. |

## Area lights

| Resource | Contract |
|---|---|
| `_AreaLightDatas` | Structured buffer of up to 16 rectangle lights per camera; the C# and HLSL layouts are maintained together. |
| `_HDShadowDatas` | Structured buffer of up to 32 shadow requests; the `AreaLighting` Volume's `maxShadowRequests` (1 to 32) limits each camera. |
| Area light parameters | Global uniforms: `_AreaLightCount`, `_AreaShadowAtlasSize`, `_CachedAreaShadowAtlasSize` (equal to the atlas size; no cached atlas exists), `_CookieAtlasSize`, `_CookieAtlasData`. They share Unity's global uniform buffer rather than consuming a dedicated constant-buffer slot alongside Forward+, DOTS, fog and PRT. |
| `_ShadowmapAreaAtlas` | A square atlas of the `AreaLighting` Volume's `shadowAtlasResolution`, each light limited to `maxShadowResolution`. With Medium filtering, a half-resolution RG32 float EVSM moment atlas; with High, the depth atlas at the Volume's `shadowAtlasDepthBits`. `_AreaShadowAtlasSize` always reports the full depth atlas size. `_CachedAreaLightShadowmapAtlas` names the same texture. |
| `_LtcData` | 64×64×8 RGBA16 float texture array of LTC matrices. |
| `_CookieAtlas` | Power-of-two, mipmapped atlas whose size and format come from `areaLightCookieAtlasSize` and `areaLightCookieFormat` (default 2048, R11G11B10). Each cookie is prefiltered per mip before it is placed. |

- **Producer.** The area light pass builds light data, shadow requests, cookies and the atlas per camera and binds all of them in one globals pass. Buffers are uploaded on that pass's command buffer, never inside a native render pass.
- **Lifetime.** Light and shadow buffers are pass-owned and re-uploaded every camera. The shadow atlas and moment targets are transients. The LTC table is shared by all feature instances and reference counted. The cookie atlas is pass-owned, persistent, and its placements reset at the start of every render context.
- **Disabled.** When area lights are off for a camera, the count is 0 and the shadow and cookie atlases are black. With no shadow request the shadow atlas is black.
- **Shadows.** Area light shadow culling needs the render context captured at the start of the frame; without it shadows are skipped with a one-time warning while lighting continues.

## Depth

- **Opaque match.** Screen-space opaque effects use depth that matches the opaque normals, motion vectors and visibility. The depth pyramid is built from the camera depth before transparent post-depth is added, so ambient occlusion, SSR and SSGI never see transparent proxy depth.
- **Pre-transparent depth.** Screen-space shadows, the PCSS penumbra mask and subsurface scattering sample the depth captured before transparent post-depth when it exists.
- **Post-depth.** When the transparent depth chain runs it publishes its post-depth as the camera depth texture for later passes; the chain is owned by [Transparency](transparency.md).
- **Replacement.** A change to published camera depth updates both the frame data handle and `_CameraDepthTexture`.

## Render graph declarations

- **Real dependencies.** Every pass declares each texture, attachment, depth attachment, buffer and renderer list it reads or writes through the render graph builder.
- **Global access.** Textures accessed through materials or globals also have builder declarations.
- **Imported targets.** Renderer-level targets and history are imported with declared access, which establishes their execution dependencies.
- **Culling.** Passes with side effects outside the graph, such as global publication or history writes, disable pass culling.
- **Chains.** Steps inside one feature (trace and denoise, accumulate and composite, copy and generate) are linked through the attachments and texture handles they pass on.

## Disabled state

When a feature is off for a camera or its inputs are invalid, its producer publishes the neutral resource its spec defines (black, white, gray or empty), or turns its keyword or mode off so that no consumer reads the resource. It keeps an earlier result only when that result is supported history that passes its validity check.

- **Defined neutrals.** SSR, transparent SSR and pre-refraction color publish black. SSGI publishes `_IndirectDiffuseMode` Off. Area lights publish a zero count and black atlases. Precomputed radiance transfer publishes an empty probe grid. Wet surfaces reset their state every camera.
- **No inherited results.** A renderer-level target filled by a previous camera is never a valid output for the current camera.
- **Neutral exposure.** Reflection cameras bind the renderer data's neutral exposure texture, a multiplier of 1, as both `_ExposureTexture` and `_PrevExposureTexture`; they run no exposure pass and own no exposure history. The same neutral texture replaces automatic exposure for a camera whose post-processing history is being reset, and on that frame automatic exposure re-exposes the camera color into a new camera color target. The neutral texture is released with the renderer data.

## History

The per-camera history buffers are identified by `IllusionFrameHistoryType`:

| History | Buffers | Written when |
|---|---|---|
| Color pyramid | 1 | SSR or SSGI is in use for the camera. |
| Exposure | 2 | Exposure control applies to the camera. |
| SSR accumulation | 2 | Opaque SSR runs. |
| Depth, Normal | 1 each | SSGI or screen-space shadow temporal accumulation is in use; copied with the color pyramid, from depth pyramid mip 0 and the camera normals. |
| SSGI, SSGI second pass | 1 each | SSGI denoising, and its second pass. |
| Screen-space shadow | 1 | Screen-space shadow temporal accumulation runs. |

- **Size.** Every frame the camera's history system swaps and takes the camera target size as its reference size. IllusionRP does not scale history render targets (`_RTHandleScaleHistory` is 1); when the target shrinks, the history system is reset to the exact size.
- **Per-frame flags.** Readiness flags reset at the start of every camera frame; history contents and reprojection state are kept.
- **Post-processing reset.** A camera's post-processing history resets on the first frame of its camera state and is cleared after that frame's post-processing. During the reset frame the previous inverse view-projection equals the current one.
- **Validity.** Each temporal consumer owns its validity: whether its history was reallocated, whether it is valid this frame, and how camera cuts and post-processing resets invalidate it. The existence of a render target does not make a history sampleable.
- **Isolation.** History is written only by its camera. History reuse avoids read-write feedback within a frame and never crosses cameras.
- **Mip count.** The color pyramid's usable mip count is tracked per camera and reset to 1 for a camera that does not generate a pyramid.
- **Readiness.** `IllusionRendererData.TryGetTemporalCaptureStatus(camera, out status)` reports whether the camera state exists and its temporal history is ready. `IsReady` requires a camera state and no `Blockers`; the blockers describe warmup, post-processing or TAA resets, and invalid SSGI, SSR or screen-space shadow history. `RecommendedWarmupFrames` is the relevant history warmup recommendation. This status does not cover color-pyramid history or exposure adaptation.
- **Release.** A camera's history is released when the camera is destroyed, after 600 frames without rendering, or when the renderer data is disposed.

### History color

Setup publishes `_HistoryColorTexture` from the first valid source:

1. for Game and Scene View cameras, the camera's color pyramid history;
2. with TAA, URP's TAA accumulation;
3. with `requireHistoryColor`, `_CameraPreviousColorTexture`;
4. URP's opaque texture.

When no source is valid it publishes black. Setup also publishes `_MotionVectorTexture` from URP's motion vectors.

## Global constants

Setup pushes `ShaderVariablesGlobal` for every camera:

| Field | Meaning |
|---|---|
| `_GlobalViewMatrix`, `_GlobalViewProjMatrix`, `_GlobalInvProjMatrix`, `_GlobalInvViewProjMatrix` | GPU camera matrices for the current target; the view-projection includes TAA jitter and the target's Y flip. |
| `_GlobalPrevInvViewProjMatrix` | The previous frame's inverse view-projection of the same camera; the current one on its first frames and during a post-processing reset. |
| `_RTHandleScaleHistory` | Always 1. |
| `_TaaFrameInfo` | Y is the camera's frame count, Z the TAA frame index from 0 to 7, W is 1 when TAA is on. |
| `_ColorPyramidUvScaleAndLimitPrevFrame` | UV scale and limit of the previous frame's color pyramid. |
| `_WorldScaleParams` | See [World Scale](world-scale.md). |
| `_MicroShadowOpacity` | The `MicroShadows` Volume opacity, or 0 when it is off. |
| `_IndirectDiffuseMode` | ScreenSpace when SSGI is sampled for the camera, otherwise Off. |
| `_IndirectDiffuseLightingMultiplier`, `_IndirectDiffuseLightingLayers` | The main light's bounce intensity and rendering layers; all layers unless `enableIndirectDiffuseRenderingLayers` is on. |

`_MainLightShadowCascadeBiases` is defined in [Materials and Shaders](materials-and-shaders.md#shader-data-layout). `_AmbientProbeData` holds the scene ambient probe as seven float4 coefficients.

## Asynchronous work

- **Compute preference.** Compute implementations are used only when `preferComputeShader` and `r.computeshader` are on and the device supports compute shaders.
- **Queues.** Async compute is disabled in this version. An asynchronous producer synchronizes with its graphics consumers only through render graph resource dependencies; global publication never synchronizes queues. SSR republishes its result at `BeforeRenderingOpaques` so opaque consumers read it after a declared dependency.

## Release

- **Owners.** Whoever creates a renderer-level target, material, buffer, native array or history releases it in its dispose or in the camera state prune.
- **Renderer data.** Disposing the renderer data releases every camera state and its history, the renderer-level targets, the depth mip offset and ambient probe buffers, the debug exposure resources, and the neutral exposure and default texture wrappers.
- **Per-camera contexts.** DLSS Neural Rendering contexts are owned by its backend and released with it or when their camera is destroyed.
