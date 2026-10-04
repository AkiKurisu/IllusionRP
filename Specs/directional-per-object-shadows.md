# Directional Per-Object Shadows

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Transparency](transparency.md), [World Scale](world-scale.md) |

Directional per-object shadows render registered casters, typically characters, into a dedicated camera-local shadow atlas for one directional light. They are a quality layer on top of URP's standard shadow atlases: each camera resolves which directional light owns the layer, the screen-space shadow producer turns the atlas into screen-space visibility, and IllusionRP lighting consumes that visibility.

## Light authority

The URP main directional light is the scene-wide main light: `RenderSettings.sun` when it is set and visible, otherwise URP's own main light choice. Rendering layers decide which renderers a light lights or shadows; they never create a second main light.

Per-object shadows follow one directional light per camera, the source. A light is usable as a source when it is an active, enabled Directional light whose shadows are not None. The `PerObjectShadowLightSource` camera component selects the source through its `Source` property:

| Camera selector | Mode |
|---|---|
| No component, component disabled, or `Source` null | Main, following the URP main light when it is usable; otherwise Disabled. |
| `Source` is the URP main light and usable | Main. |
| `Source` is a usable, visible directional other than the main light, and the actual rendering path is Forward+ | AdditionalDirectional, identified by the light's exact index in URP's Forward+ additional light buffer. |
| `Source` is set but unusable, not visible, beyond the additional light limit, or the path is not Forward+ | Disabled for that frame. |

- **Source lifetime.** A `Source` whose Light has been destroyed counts as null.
- **Identity.** The additional light index is URP's buffer index for the current frame.
- **Feature gate.** The renderer feature setting Additional Directional Per-Object Shadows (`additionalDirectionalPerObjectShadows`) allows camera selection. When it is off, cameras ignore the component and always use the Main fallback; per-object shadows themselves stay on.
- **Preview cameras.** Preview cameras have no per-object shadows.
- **Published state.** The mode is published as `_PerObjSceneShadowSourceMode` (0 Disabled, 1 Main, 2 AdditionalDirectional) together with `_PerObjSceneShadowAdditionalLightIndex` and `_PerObjSceneShadowLightDirection`, also when no caster is visible.

## Casters

- **Registration.** A caster implements `IShadowCaster` and registers with `ShadowCasterManager` while it is active. `PerObjectShadowRenderer` is the standard caster component: each of its renderer clusters is one caster, registered while the component is enabled and casting while `isCastingShadow` is set. Registration is global; culling, tile allocation and allocation history are per camera.
- **Draws.** A caster draws the `LightMode=ShadowCaster` pass of every material on its enabled, active renderers whose shadow casting mode is not Off. Caster materials keep that standard pass; the per-object atlas adds to URP's main and additional atlases and never replaces them.
- **Culling.** Each frame a camera culls the registered casters against its view and the source direction, and keeps at most 16 visible casters with the lowest priority values. Lower is more important; nearer and more central casters rank first. The light-space depth range of each caster's projection is at least `perObjectShadowLengthOffset`, a length under [World Scale](world-scale.md).

## Atlas allocation

The `PerObjectShadows` Volume (`Illusion/Per Object Shadows`) controls the atlas. `perObjectShadowDepthBits` sets the depth precision.

| Mode | Contract |
|---|---|
| Fixed (`adaptiveTileResolution` off) | Every caster gets a `perObjectShadowTileResolution` tile in a square grid of ceil(sqrt(n)) tiles per side. |
| Adaptive (`adaptiveTileResolution` on, the default) | A fixed square atlas of `adaptiveAtlasResolution` is shared by projected screen coverage and priority, with tiles from 256 up to `maximumAdaptiveTileResolution`. |

Adaptive allocation:

- **Ladder.** Tile resolutions are 256, 512, 1024, 1280, 1536, 2048, 3072 and 4096.
- **Capacity.** At most as many casters as 256 tiles fit in the atlas are allocated, in priority order.
- **Ideal layout.** Every caster starts at 256. The caster with the highest projected coverage per tile resolution is upgraded one step at a time while the layout still fits; ties prefer the lower priority value, then larger coverage, then the lower caster ID.
- **Packing.** Tiles are placed by MaxRects best short side fit without rotation, ordered by resolution descending, priority ascending and caster ID ascending, so identical input gives an identical layout.
- **Hysteresis.** A higher resolution applies immediately. A lower ideal resolution must persist for 8 consecutive frames before the tile drops one step, never below the ideal. A resolution above the maximum is clamped immediately.
- **Overflow.** When a new caster or a settings change makes the layout no longer fit, the lowest-priority caster (highest priority value, then highest caster ID) drops one step at a time until it fits.
- **Guard band.** Each tile draws inside a 4-pixel guard band. Shadow matrices, tile rectangles and biases use the actual viewport and tile resolution.
- **History.** A change of the allocation (mode, atlas size, caster IDs, tile resolutions or positions) invalidates only the current camera's screen-space shadow temporal history.

## Producer parameters

- **Bias.** Depth and normal bias come from the source light, or from the pipeline asset when the light uses pipeline settings, scaled by the tile's world texel size and, for soft shadows, by the filter kernel radius. They do not depend on URP allocating its own atlases.
- **Strength and quality.** In Main mode, consumers use URP's main light shadow strength and soft shadow quality. In AdditionalDirectional mode they use the source's shadow strength and soft shadow quality, which is Off unless the pipeline asset supports soft shadows and the light uses soft shadows.
- **PCF.** Off, Low, Medium and High take 1, 4, 9 and 16 comparison samples. The choice is made at runtime from the quality value and does not depend on URP's `_SHADOWS_SOFT*` keywords.
- **PCSS.** When PCSS shadows are active, the screen-space producer filters per-object tiles with a per-object PCSS path using the same Volume parameters and penumbra mask as the main light, instead of PCF.
- **Globals.** The atlas and its per-tile data are published as `_PerObjSceneShadowMap`, `_PerObjSceneShadowCount`, `_PerObjSceneShadowMatrices`, `_PerObjSceneShadowMapRects`, `_PerObjSceneShadowParams`, `_PerObjShadowBiases` and the `_PerObjShadowPcss*` arrays.

## Main atlas caster split

In Main mode both URP's main atlas and the per-object atlas draw the registered casters. The duplicate draw is intentional: the standard atlas stays complete for transparent, volumetric, precomputed radiance transfer, offscreen and third-party consumers.

In AdditionalDirectional mode the per-object atlas belongs to the selected source, and URP's main atlas keeps scene casters but excludes renderers on the renderer feature's Per Object Shadow Rendering Layer (`perObjectShadowRenderingLayer`):

- **Scope.** Only the main light's shadow rendering layers change, for the current camera's cull. Its light rendering layers, culling mask, intensity and shadow type are untouched.
- **Conditions.** The exclusion applies only when Additional Directional Per-Object Shadows is on, the layer mask is not empty, the path is Forward+, the camera's enabled selector names a usable source, `RenderSettings.sun` is a different usable directional with URP additional light data, and both lights are visible to the camera. Otherwise the main atlas keeps drawing the casters.
- **Timing.** The exclusion is established in the renderer feature's pre-cull callback, because render passes run after URP takes its culling snapshot.
- **Restore.** The changed fields are restored when that camera finishes rendering, at the next camera's pre-cull, when the setting is turned off and when the feature is disposed, so no scene state is left modified.
- **Layer assignment.** Casters that should leave the main atlas must carry that layer. `PerObjectShadowRenderer` assigns its `renderingLayerMask` to all of its renderers.

## Screen-space visibility

The screen-space shadow producer writes `_ScreenSpaceShadowmapTexture` from the opaque depth, before transparent depth is added.

| Channel | Meaning |
|---|---|
| R | Main light visibility: URP's main light shadow, the Main-mode per-object visibility and contact shadows. |
| G | Per-object visibility of the AdditionalDirectional source; 1 in other modes. |

- **Format.** R8 when Additional Directional Per-Object Shadows is off and RG8 when it is on; B8G8R8A8 when the preferred format cannot be blended.
- **Contact shadows.** Contact shadows trace toward the URP main light and only ever affect R; they never follow the per-object source or write G. With temporal accumulation they are composited into R after temporal and spatial filtering.
- **Filtering.** PCSS penumbra estimation, temporal accumulation and spatial denoising happen in the producer and cover both channels. Opaque materials never filter the atlas again.
- **History.** Temporal history is invalidated when the main light direction, the source mode, the source light or its direction changes, and when the atlas allocation changes.

## Material consumption

- **Main light.** Main light shading reads R only.
- **Additional lights.** `IllusionGetAdditionalLight(i, inputData, shadowMask, receivePerObjectShadow)` applies per-object visibility only on Forward+, only in AdditionalDirectional mode and only when `i` equals the published index, and combines it with URP's additional shadow attenuation by taking the minimum.
- **Lighting models.** Lit, Skin and Fabric receive through that function. Hair passes `false`, so dense alpha cards do not band from self-shadowing; Hair renderers still cast.
- **Opaque and alpha-tested.** Opaque and alpha-tested surfaces read G while screen-space main light shadows are active, and otherwise sample the atlas directly with PCF.
- **Transparent.** Transparent surfaces, including those that write transparent depth, never read G. With Transparent Receive Per Object Shadows (`transparentReceivePerObjectShadows`, keyword `_TRANSPARENT_PER_OBJECT_SHADOWS`) they sample the atlas directly at their own position with PCF; otherwise they receive no visibility from the source. See [Transparency](transparency.md).
- **Other shaders.** Shaders without the IllusionRP lighting includes receive Main-mode per-object visibility only through URP's screen-space shadow R channel and never receive AdditionalDirectional visibility.
- **Lighting loop.** Baked shadowmask, shadow distance fade, light layers, cookies and distance and cone attenuation stay with the normal lighting loop; the screen-space channels carry realtime visibility only.
