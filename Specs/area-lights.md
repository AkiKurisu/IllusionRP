# Area Lights

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Living |
| Date | 2026-10-18 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md), [Shader Variant Stripping](shader-variant-stripping.md), [Ray Tracing Scene](ray-tracing-scene.md) |

IllusionRP adds realtime rectangle area lights to Forward and Forward+ raster lighting. Unity rectangle lights are the authoring source. A per-camera producer prepares their light data, shadows and cookies, and the material families shade the published set. [Path tracing](path-tracing.md) and [ray traced global illumination](ray-traced-global-illumination.md) evaluate rectangle lights through the [ray tracing scene](ray-tracing-scene.md), not through this producer.

## Enablement

Area lighting requires the renderer capability, runtime permission, an enabled Area Lighting Volume and a Forward or Forward+ camera. Deferred and Preview cameras do not participate. Only realtime rectangle lights that URP reports as visible to the camera are candidates.

## Producer

The producer runs per camera after the prepass, with per-object and contact shadows (see [Rendering Pipeline](rendering-pipeline.md#frame-order)). It selects visible rectangle lights that contribute diffuse or specular light and have not faded out with distance, up to a bounded per-camera light count, and publishes their light data, shadow requests and cookie placement as one consistent set. Receivers are filtered by each light's rendering layers.

Light data and cookie storage belong to the feature. Shadow intermediates are frame-scoped, and lookup tables may be shared. Lighting parameters must respect the combined platform resource budget of the material paths that consume them.

## Shadows

Shadow-casting lights request shadow maps within the Volume's per-camera request budget and per-light resolution limit; all requests share one per-camera atlas. A light whose request does not fit the budget is lit without shadows. Casters draw their normal `ShadowCaster` pass with rendering layer testing.

Area shadows need the shadow-culling context of the current render. When it is missing, area shadows are disabled with a diagnostic while area lighting continues. Preview cameras render no area shadows.

## Cookies

A light may carry an optional cookie, placed in a feature-owned cookie atlas. Placement is reset for each render context. A cookie too small for the atlas is ignored and the light renders without it. Cookies do not introduce a material keyword axis.

## Quality and shadow filtering

Area-light quality and shadow filtering form one coupled renderer capability: the selected filtering tier decides both how the shadow atlas is prepared and how materials sample it, and the two never mix. The tier's global state stays resident while the capability is enabled; Volume and runtime controls change only the published light set. A camera with no active lights publishes an empty light set while keeping that state. [Shader Variant Stripping](shader-variant-stripping.md#keyword-prefiltering) keeps the selected tiers.

## Disabled state

A disabled or ineligible camera receives an empty light set and neutral shadow and cookie atlases. A previous camera's lights, shadows or cookies are never a fallback.

## Material shading

| Family | Area-light response |
|---|---|
| Lit | Surface diffuse and specular models, including the optional coat. |
| Water | Surface diffuse and specular models, including the optional coat, evaluated only in its color pass. |
| Skin | Diffuse and transmission in the split diffuse path when screen-space subsurface scattering runs; specular in the main path. |
| Fabric | Diffuse and sheen; area lights do not apply anisotropy. |
| Hair | Its area-light approximation; cookies are not sampled. |

Auxiliary passes, such as reflection-data passes, never duplicate final lighting.

## Energy convention

All families adapt area-light energy to URP's lighting convention exactly once.
