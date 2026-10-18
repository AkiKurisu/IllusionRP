# Inline Ray Tracing

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Draft |
| Date | 2026-10-18 |
| Related Specs | [Ray Tracing Scene](ray-tracing-scene.md), [Ray Traced Global Illumination](ray-traced-global-illumination.md), [Path Tracing](path-tracing.md), [Materials and Shaders](materials-and-shaders.md), [ASE Shader Workflow](ase-shader-workflow.md), [Shader Variant Stripping](shader-variant-stripping.md), [Third-Party Source Imports](third-party-source-imports.md) |

Inline ray tracing runs ray traced global illumination (RTGI) Performance mode entirely as compute work with ray queries. It replaces ray generation dispatches whose hits run material hit shaders. Unity prepares hit groups for every instance of every acceleration structure bound to a ray generation dispatch, on every dispatch, so that path's CPU cost grows with the size of the scene. Inline ray tracing binds no acceleration structure to a ray generation dispatch, so no hit groups are prepared. Each material is still evaluated from its own `PathTracing` pass source, and the result is the image the hit shaders would produce.

## Goal

- RTGI Performance mode prepares no hit groups. Its per-frame CPU cost is independent of the number of instances beyond the ray tracing scene's own update.
- Inline evaluation and hit-shader evaluation of the same hit give the same material inputs, coverage and lighting. RTGI's converged result does not change.
- Materials are evaluated at the hit from their authored textures and properties. Nothing is baked, averaged or precomputed per material.
- Inline evaluation depends only on geometry and material properties. It never depends on what an object represents in the application, its layer or its scene role.

## Scope

- Every stage of RTGI Performance mode: the GI ray, secondary surface shading with its light sampling visibility, ray traced bias correction in resampling, and final shading.
- Static meshes, skinned meshes, blend-shaped meshes and meshes whose data is written in place, such as simulated cloth.
- Every hand-written and ASE-generated shader whose `PathTracing` pass builds on the shared PathTracing hit library.
- The scene resource table that gives compute shaders indexed access to the textures, vertex data and index data of the scene.

## Non-goals

- Path tracing cameras, PRT baking and RTGI Quality mode. They keep hit-shader evaluation.
- A unified or hand-written material evaluator that replaces the materials' own `PathTracing` source.
- Baked material atlases, averaged material colors, or any other per-material precomputation.
- Procedural and AABB geometry.
- Changes to the RTGI estimate, resampling or denoising contracts beyond how rays are traced and hits are evaluated.

## Acceleration structure

RTGI Performance mode traces the [ray tracing scene](ray-tracing-scene.md)'s acceleration structure from compute work only. It does not bind that structure, or any other, to a ray generation dispatch. Binding an acceleration structure to a ray generation dispatch makes Unity prepare hit groups for all of its instances, even when the dispatch uses only ray queries. Participation, change tracking, culling and the once-per-frame build stay as the ray tracing scene defines them.

## Ray traversal

- **Coverage.** A candidate hit on non-opaque geometry runs its material's coverage function inside the traversal loop. Coverage, stochastic coverage, face culling, shadow-caster and shadow-layer rules and colored transmission are the same as the hit shaders' rules for that ray kind ([Path Tracing](path-tracing.md#scene-and-material-inputs)).
- **Closest hits.** A GI ray's accepted closest hit is evaluated by its material's inline evaluator.
- **Visibility.** Visibility rays accumulate colored transmission through transmitting surfaces, as the hit shaders do.
- **Misses.** A ray that reaches no accepted hit sees the lighting environment.

## Exceptions

An instance has no inline evaluator for a submesh in three cases:

- the submesh's material has no `PathTracing` pass
- the pass uses hit-shader features that inline evaluation does not provide, so its evaluator cannot be generated
- the resource table cannot hold the instance's resources

That submesh is evaluated as the same diagnostic surface that path tracing shows for unsupported material entry points: an opaque surface of a fixed diagnostic color. It is never substituted with another material model. Exceptions are reported once per material in the Editor and development builds. A material whose shader gains an evaluator leaves the exception list at the next scene update.

## Scene resource table

The ray tracing scene service owns a shader-visible resource table that compute shaders index. It holds the textures that materials reference and the vertex and index data of participating geometry. Instance, geometry and material records refer to its entries by index.

- **Provider.** The table is provided by the optional UnityRHI native backend that RTGI's denoiser already requires. Its native module enlarges Unity's shader-visible descriptor heap and gives compute root signatures an indexed resource range without moving their other resources. Ray tracing pipelines are not extended.
- **Loading.** The module must load before the graphics device is created. The Editor places it in the project, which the Editor and players load at startup, and asks for an Editor restart when it changes.
- **Fallback.** Without the backend, or on a device without ray queries, RTGI Performance mode uses hit-shader evaluation for every instance, as it did before inline ray tracing. The reason is reported once.
- **Imports.** The native hooking library and the descriptor-table technique are imported sources tracked by [Third-Party Source Imports](third-party-source-imports.md).
- **Registration.** A resource gets an entry when a record first references it. Its entry is released once no record references it and no GPU work in flight still reads it.
- **Replacement.** A texture reimport, a mesh upload, a recreated render texture or a deforming renderer's buffer swap changes the native resource behind an entry. The entry is updated before the frame's inline work reads it, so inline work never reads a released or stale resource.

## Geometry records

- **Geometry order.** A traversal reports a geometry index in the order the acceleration structure assigns to an instance's submeshes. That order leaves out disabled submeshes. Each instance's records map those indices back to their submeshes and materials.
- **Attributes.** Records describe the mesh's vertex layout and formats. Inline evaluation reconstructs the hit's interpolated attributes from them: positions, normals, tangents, texture coordinates and vertex color, with the precision the hit shaders receive. Read/Write import is not required.
- **Deforming geometry.** Skinned, blend-shaped and in-place meshes reference the deformed data the acceleration structure was built from in the same frame. A renderer that alternates between deformed buffers from frame to frame updates its entry every frame. RTGI does not need previous-frame geometry at secondary surfaces.

## Inline material evaluation

- **Single source.** A shader's inline evaluator is the pass's own `PathTracing` program, compiled once per variant with its keywords and libraries, with its closest-hit and any-hit entry points called as functions. No material family has separate evaluator code, and passes need no changes for it.
- **Hit environment.** The inline environment stands in for everything the hit shaders receive from Unity: ray and instance intrinsics, the geometry's vertex and index data, per-object transforms and the material's properties and textures. Evaluators of different shaders and variants are isolated from one another, while the scene and frame globals they share are declared once.
- **Material inputs.** Material property values, texture scale and offset, and renderer property overrides are captured into material records. Change tracking recaptures them when the material or override content changes. Each hit reads its own record, so one dispatch shades many materials of a family.
- **Texture sampling.** Texture lookups resolve through the scene resource table. They keep each texture's filtering, including the anisotropic filtering that quality settings force, and its addressing, and use the same footprint and level selection as hit-shader evaluation.
- **Variants.** A material's enabled `PathTracing` keywords select its evaluator variant. Inline evaluators are generated when shaders import, follow [Shader Variant Stripping](shader-variant-stripping.md), and are reachable in a build exactly when RTGI is. A variant with no evaluator in a player is an exception.
- **Work grouping.** Secondary surfaces are shaded in groups that share one evaluator. A shading wave never runs the code of unrelated material families.
- **Generation.** Evaluators are regenerated when shader sources, their includes or the materials that select their variants change. A variant whose evaluator does not compile is reported with its compiler errors and becomes an exception. The other evaluators are unaffected.

## RTGI integration

- **Initial samples.** The GI ray is traced inline. Its hit is shaded in compute with the RTGI [secondary surface contract](ray-traced-global-illumination.md#secondary-surfaces): the standard Lit reduction, next-event estimation over the shared light set with inline visibility, and the raster light rule.
- **Resampling and final shading.** Ray traced bias correction and final shading trace their visibility rays inline.
- **Quality mode.** Unchanged: the radiance cache update and the initial samples keep hit-shader evaluation.
- **Validation.** In the Editor and development builds, a switch selects hit-shader evaluation for Performance mode, to compare both paths on the same frame.

## Acceptance

- In RTGI Performance mode, no hit groups are prepared, and render-thread cost does not grow with the scene's instance count.
- Converged RTGI matches the hit-shader validation path in these cases:
  - scenes with textured, alpha-tested, dithered, emissive and property-overridden props
  - skinned characters in motion, including skin, hair cards and cloth
- Alpha-tested and dithered geometry and hair cards occlude and transmit the same in both paths, for GI and visibility rays.
- Exceptions render as the diagnostic surface and are reported.
- Meshes without Read/Write participate in players.
- Without the native backend, on unsupported devices, or with the validation switch on, RTGI renders as it did before inline ray tracing.
- Reimporting textures and meshes, changing materials or overrides, and adding or removing renderers or characters at runtime leave no stale or released resource reachable from inline work.
