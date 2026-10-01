# Materials and Shaders

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |
| Related Specs | [ASE Shader Workflow](ase-shader-workflow.md), [Shader Variant Stripping](shader-variant-stripping.md), [Transparency](transparency.md), [Wet Surface Decals](wet-surface-decals.md), [Rendering Pipeline](rendering-pipeline.md) |

IllusionRP renders materials on the Forward and Forward+ paths. This spec defines the material shader families, their passes and LightMode tags, the Forward GBuffer that material passes write, the stencil bits they own, the `UnityPerMaterial` and keyword rules, and the lighting contracts that apply across families: rectangle area lights and Fabric anisotropy.

Out of scope: template, graph and export rules ([ASE Shader Workflow](ase-shader-workflow.md)); transparency, OIT, refraction and transparent reflections ([Transparency](transparency.md)); the Water shader ([Water](water.md)); frame order ([Rendering Pipeline](rendering-pipeline.md)); render targets and resources ([Render Resources](render-resources.md)); the wet surface response ([Wet Surface Decals](wet-surface-decals.md)); the Deferred path; the `PathTracing` pass and path traced material models ([Path Tracing](path-tracing.md)).

## Ownership

- **Color owner.** A material's color comes from its main color pass, or from `OIT` for order-independent transparent coverage. Every other pass produces data only: depth, normals, smoothness, motion, shadows, subsurface diffuse or water reflection data.
- **Sources.** Templates own pass topology, LightMode tags, fixed render state and `UnityPerMaterial`; graphs own material inputs; generated shaders are rebuilt from both and never hand-edited, as [ASE Shader Workflow](ase-shader-workflow.md) defines. Shared HLSL owns surface setup, BRDFs, coverage, shadows, global illumination and packing.
- **Material state.** The Hybrid Lit material inspector, used by Hybrid Lit, Hybrid Complex Lit and by default by the Hybrid Lit template, keeps pass enabled state, keywords and the hidden stencil properties in sync with the surface type and material options. The packaged template shaders use the ASE material inspector, which changes neither pass state nor stencil properties.
- **Diffusion profiles.** Editor code binds a profile with `DiffusionProfileMaterialUtility.SetProfile(material, profile, propertyName)`, where `propertyName` defaults to `_DiffusionProfile`. It writes the profile asset GUID as a vector to `<propertyName>_Asset` and the profile hash, reinterpreted as a float, to `<propertyName>`. A null profile clears both; a material without both properties, or a profile that is not a saved asset, is rejected with an exception. Callers do not reimplement the encoding, hard-code hashes or read profile internals.

## Shader families

Pass names are listed with their LightMode in parentheses where the two differ.

| Shader | Source | Main color pass | Other passes |
|---|---|---|---|
| `Universal Render Pipeline/Hybrid Lit` | Hand-written | `ForwardLit` (`UniversalForward`) | `OITTransparent` (`OIT`), `ForwardGBuffer`, `DepthOnly`, `ShadowCaster`, `GBuffer` (`UniversalGBuffer`), `Meta`, `Universal2D`, `MotionVectors`, `XRMotionVectors` |
| `Universal Render Pipeline/Hybrid Complex Lit` | Hand-written | `ForwardLit` (`UniversalForwardOnly`) | Same as Hybrid Lit |
| `Universal Render Pipeline/HD Lit` | Hybrid Lit template | `Forward` (`UniversalForward`) | `ForwardGBuffer`, `ShadowCaster`, `GBuffer` (`UniversalGBuffer`), `Meta`, `MotionVectors` |
| `Universal Render Pipeline/HD Skin` | Skin template | `Forward` (`UniversalForward`) | `SubsurfaceDiffuse`, `ForwardGBuffer`, `ShadowCaster`, `MotionVectors` |
| `Universal Render Pipeline/HD Hair` | Hair template | `Forward` (`UniversalForwardOnly`) | `OITTransparent` (`OIT`), `PostDepthOnly`, `ForwardGBuffer`, `ShadowCaster`, `MotionVectors` |
| `Universal Render Pipeline/HD Fabric` | Fabric template | `Forward` (`UniversalForwardOnly`) | `PostDepthOnly`, `ForwardGBuffer`, `ShadowCaster`, `GBuffer` (`UniversalGBuffer`), `MotionVectors` |
| `Universal Render Pipeline/Water` | Water template | `Forward` (`UniversalForwardOnly`) | `WaterSSRData`, `MotionVectors` |

Template options decide which template passes a graph keeps:

- **Forward Only** switches the main pass between `UniversalForward` and `UniversalForwardOnly` and drops `GBuffer`; Transmission, Translucency and Clear Coat force it on.
- **Cast Shadows**, **Motion Vectors**, **XR Motion Vectors** and **Meta Pass** keep or drop `ShadowCaster`, `MotionVectors`, `XRMotionVectors` and `Meta`.
- **Multi Pass** (Hair) keeps `OITTransparent` and `PostDepthOnly`; **Receive Occlusion** (Fabric) keeps `PostDepthOnly`; a transparent Water surface keeps `WaterSSRData`.
- The Hybrid Unlit template never emits `ForwardGBuffer`, and the Water template has none.

The `UniversalGBuffer` passes are part of the existing topology, but IllusionRP does not support the Deferred path for these materials; they do not define a Deferred material ABI.

## Pass contract

| LightMode | Role |
|---|---|
| `UniversalForward`, `UniversalForwardOnly` | Final surface color with direct and indirect lighting, drawn by URP. |
| `ForwardGBuffer` | Smoothness, camera normals and depth for screen-space lighting, drawn from every render queue. |
| `SubsurfaceDiffuse` | Skin diffuse irradiance and albedo for screen-space subsurface scattering, drawn from the opaque queue. It writes no final color. |
| `OIT` | Weighted blended OIT accumulation and revealage, drawn from every render queue within the OIT layer mask. |
| `PostDepthOnly` | Depth of transparent coverage after the prepass; it also writes the Forward GBuffer smoothness target. |
| `WaterSSRData` | Transparent water normal, smoothness and depth for transparent screen-space reflections, drawn from the transparent queue. |
| `ShadowCaster` | URP shadow maps, per-object shadows and area light shadows. |
| `DepthOnly` | URP depth requirements; it never replaces `ForwardGBuffer`. |
| `MotionVectors`, `XRMotionVectors` | URP motion vectors. |
| `Meta` | Lightmap baking inputs. |

[Transparency](transparency.md) owns the behavior of `OIT`, `PostDepthOnly` and `WaterSSRData`.

- **Single owner.** A transparent Hybrid Lit or Hybrid Complex Lit material is shaded by the main color pass or by `OIT`, never both. With `_OrderIndependent` on, the inspector disables the main color pass and enables `OIT`; with it off, the reverse. For opaque materials it disables `OIT`.
- **Hair multipass.** Hair splits one material into an opaque-queue core, shaded by the main pass, and a fringe, shaded by `OIT`; the split and its cutoffs are defined in [Transparency](transparency.md#hair-fringe). Every Hair pass shares vertex deformation, culling and alpha, so no seam opens between core and fringe.

## Forward GBuffer

On the Forward and Forward+ paths, the renderer draws every enabled `ForwardGBuffer` pass of every render queue in one prepass, sorted like opaques, with depth writes on and a less-or-equal depth test regardless of the pass's own depth state. Stencil comes from the pass. Material shaders have no `DepthNormals` or `DepthNormalsOnly` pass: `ForwardGBuffer` is their depth and normals output. [Rendering Pipeline](rendering-pipeline.md) places this prepass in the frame.

| Attachment | Content |
|---|---|
| Color 0 | `_ForwardGBuffer`, smoothness written to every channel. |
| Color 1 | `_CameraNormalsTexture`, the world-space unit normal in RGB and zero in A, matching URP's depth-normals packing without octahedral encoding. |
| Depth | The camera depth target. |

- **Format.** `_ForwardGBuffer` is R8 UNorm when that format supports blending, otherwise B8G8R8A8 UNorm. With wet surfaces it uses the RGBA8 packed layout that [Wet Surface Decals](wet-surface-decals.md) defines.
- **Lifetime.** The target is cleared to zero before the pass and published as the global `_ForwardGBuffer`, with `_CameraNormalsTexture`, after it.
- **Parity.** Every `ForwardGBuffer` pass matches its main color pass in vertex deformation, alpha clip, LOD cross-fade and culling. Changing one of these in only one pass breaks the contract.
- **Transparency.** Transparent surfaces do not draw `ForwardGBuffer`; the multipass Hair core draws it from the opaque queue. The Hybrid Lit inspector disables the pass for transparent materials and enables it for opaque ones.
- **Override ports.** The templates' `ForwardGBuffer` pass has two ports not linked to the main pass: `GBuffer Normal` (tangent space) and `GBuffer Smoothness`. A connected port defines `_GBUFFER_NORMAL_OVERRIDE` or `_GBUFFER_SMOOTHNESS_OVERRIDE` in that pass only, as a define rather than a keyword, and the pass writes the port value; an unconnected port follows the main pass's `Normal` or `Smoothness`. Overrides may only simplify the normal and smoothness that screen-space consumers see; they never change alpha clip, depth, stencil or coverage. The `Wet Base Color`, `Wet Metallic` and `Wet Specular` ports belong to [Wet Surface Decals](wet-surface-decals.md).
- **Hand-written passes.** The Hybrid Lit `ForwardGBuffer` pass samples only what alpha, normal and smoothness need, unless the wet layout needs the full surface, and declares the same smoothness-source keywords as the main pass, including `_METALLICSPECGLOSSMAP`.
- **Normal encoding.** `_GBUFFER_NORMALS_OCT` belongs to URP's Deferred GBuffer; `ForwardGBuffer` passes do not declare it.

## Screen-space receivers

- **Opaque surfaces.** Opaque surfaces sample screen-space ambient occlusion, reflections, global illumination and the screen-space main light shadow whenever the matching keyword is on; the stencil bits below opt pixels out of ambient occlusion and reflections.
- **Transparent surfaces.** A transparent surface (`_SURFACE_TYPE_TRANSPARENT`) samples them only when it also defines `_TRANSPARENT_WRITE_DEPTH`, which marks a surface that writes its depth in `PostDepthOnly`, as the Fabric template's Receive Occlusion option does. Such a surface keeps the screen-space main light shadow after the cascade shadow keywords are restored for transparents; other transparent surfaces use the main light shadow map.
- **Hair.** With the Hair template's Multi Pass option on, Hair samples screen-space global illumination even when its surface type is transparent.

## Stencil

Material passes own two stencil bits:

| Bit | Meaning | Reader |
|---:|---|---|
| `0x01` | The surface does not receive screen-space ambient occlusion. | Ambient occlusion skips the pixel. |
| `0x04` | The surface receives screen-space reflections. | SSR traces only pixels with the bit set. |

- **Writes.** Writer passes write `_StencilRefDepth` under `_StencilWriteMaskDepth` with compare Always and pass Replace. The write mask is `0x05`, so the two bits are updated together without touching any other bit.
- **Writers.** The `ForwardGBuffer` pass of Hybrid Lit, Hybrid Complex Lit and the Lit and Fabric templates; the `DepthOnly` pass of the Lit, Fabric and Water templates; and the Fabric `PostDepthOnly` pass. The Skin and Hair templates write neither bit, so their surfaces receive ambient occlusion and are not traced by SSR.
- **Values.** The Hybrid Lit inspector rebuilds the reference from zero: bit `0x04` when `_ScreenSpaceReflections` is on and bit `0x01` when `_ScreenSpaceAmbientOcclusion` is off. Shaders without that inspector keep the property defaults, reference 4 and mask 5: traced by SSR and receiving ambient occlusion.
- **No other material ABI.** The `IllusionStencilUsage.CharacterSkin` and `CharacterHair` constants, and the shader-side skin, hair and subsurface bits (`0x01`, `0x02`, `0x08`), have no writer among the package's shaders, and no feature may treat them as a stable classification. Stencil VRS reads bits `0x01` to `0x08` as skin, hair, SSR and subsurface to choose a shading rate, so its classification overlaps the AO opt-out bit.
- **Other writers.** URP's `XRMotionVectors` passes write bit `0x01` for XR object motion. The transparent overdraw stencil state comes from the renderer feature's `oitOverrideStencil` settings, not from a fixed bit.
- **Later features.** Rectangle area lights and wet surfaces add no stencil bits; wet coverage is a separate target because bit `0x01` belongs to ambient occlusion.
- **New bits.** Before a bit is added or reinterpreted, URP's depth-stencil usage, every writer pass, Stencil VRS, OIT overdraw and the shaders applications build on the templates are reviewed, and this spec records the bit's single owner.

## Shader data layout

- **One layout.** Every SubShader and pass of a shader declares the same `UnityPerMaterial` fields, with the same types, order and packing. Template option fields, such as transmission or tessellation values, are declared identically in every pass.
- **No resources.** Textures and samplers are never declared in `UnityPerMaterial`.
- **Pass defines.** Pass-specific defines do not change the final buffer layout.
- **Reachable paths.** Alpha clip, normal map, LOD cross-fade, instancing, DOTS instancing, SRP Batcher and GPU Resident Drawer paths reach the same code in the template and in the generated shader.
- **Pass interface.** Pass names and LightMode tags are the interface between runtime renderer lists and build stripping; neither side is renamed alone ([Shader Variant Stripping](shader-variant-stripping.md)).
- **Cascade biases.** `_MainLightShadowCascadeBiases` is a global array of five vectors. The first four hold the world-space receiver bias of each main light cascade, computed from URP's shadow bias for the main light with that cascade's projection and resolution; cascades that are not in use stay zero. The fifth entry is always zero and serves positions outside every cascade. Fragment shadow bias reads it.

## Keywords

- **Global keywords.** Material passes compile the global keywords that [Rendering Pipeline](rendering-pipeline.md#global-state) defines. They follow the renderer feature's serialized settings, not Volumes or runtime switches, so a runtime switch never changes a material's variant.
- **Material keywords.** Fabric declares `_ANISOTROPY_ON` and `_SHEEN_VELET` as local keywords. Water declares `_WATER_REFLECTION_LEGACY`, which [Water](water.md) owns.

## Rectangle area lights

- **Keyword axis.** Area lighting compiles under `multi_compile_fragment _ AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH`. The axis is declared by the main color pass of the Lit, Skin, Hair, Fabric and Water templates, the Hair `OITTransparent` pass, the Skin `SubsurfaceDiffuse` pass, and the `ForwardLit` and `OITTransparent` passes of Hybrid Lit and Hybrid Complex Lit. Shaders derive `_AREA_LIGHTS` in the fragment stage from either tier keyword and never declare it.
- **Resident tier.** While the renderer feature's `areaLights` setting is on, the selected tier keyword stays enabled. When area lights are off for a camera ([Rendering Pipeline](rendering-pipeline.md#enablement)) or no rectangle light is visible, `_AreaLightCount` is zero and each family skips its area loop with a dynamic branch. The off variant serves only renderers without area lights.
- **Coupled quality.** `areaShadowFilteringQuality` selects both the keyword tier and the area shadow filtering; neither is changed alone.
- **Lit and Water.** A diffuse lobe matching the family's punctual diffuse model, a GGX specular lobe and an optional clear coat lobe. Water uses Lit lighting, evaluates area lights only in its main color pass, and `WaterSSRData` still writes only normal, smoothness and depth.
- **Skin.** Diffuse and transmission are evaluated with the diffuse lighting, in `SubsurfaceDiffuse` while `_SCREEN_SPACE_SSS` is enabled and in the main pass otherwise; the dual-lobe specular is evaluated in the main pass.
- **Fabric.** Lambert diffuse lit from both sides, and a GGX base lobe and a Charlie sheen lobe mixed by the sheen intensity. Anisotropy is not applied.
- **Hair.** A most-representative-point approximation instead of LTC.
- **Units.** Area diffuse and specular results are multiplied by π once, at the shared evaluation exit, to match URP's direct light convention, which omits the 1/π of Lambert diffuse.
- **Cookies.** Cookies add no keyword: LTC evaluation samples the cookie in a dynamic branch when a light's cookie mode is not none. The Hair approximation does not sample cookies.

## Fabric anisotropy

`_ANISOTROPY_ON`, toggled by `_Anisotropy_On` and compiled in the main color pass, selects anisotropic direct lighting. The tangent and bitangent come from the mesh tangent frame tilted by the tangent-space normal scaled by `_NormalAniso`, and `_Anisotropy_Intensity` is the anisotropy `A` in [-1, 1]. Let `a` be URP's `BRDFData.roughness`, the perceptual roughness squared, and `s` the sheen intensity:

```text
aT = max(a * (1 + A), 0.001)
aB = max(a * (1 - A), 0.001)
anisoSpecular = PI * DV_SmithJointGGXAniso(aT, aB) * F_Schlick(F0, VdotH)
directSpecular = lerp(anisoSpecular, sheenSpecular, s)
```

- **Width.** The base highlight width follows the material smoothness through URP's roughness mapping; `BRDFData.roughness2` is not used.
- **π adaptation.** The anisotropic GGX lobe is normalized like HDRP's, so it is multiplied by π exactly once, before it is mixed with sheen, to match URP's direct light convention. The sheen lobe uses distributions without the 1/π factor and is not scaled again.
- **Reach.** Anisotropy applies to the main and additional punctual lights only. Environment reflection stays isotropic and is never scaled by π, and area lights drop anisotropy.

## Validation

- A material change is reviewed across the template, the affected graphs, the generated shaders, the material inspector, the runtime renderer lists and variant stripping together.
- Acceptance confirms that color ownership follows this spec, that every data pass matches the main pass's coverage, that `ForwardGBuffer` outputs and render state match this spec, that `UnityPerMaterial` is identical in every pass, and that a repeated export converges as [ASE Shader Workflow](ase-shader-workflow.md) requires.
