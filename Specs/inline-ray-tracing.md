# Inline Ray Tracing

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Draft |
| Date | 2026-10-24 |
| Related Specs | [Ray Tracing Scene](ray-tracing-scene.md), [Ray Traced Global Illumination](ray-traced-global-illumination.md), [Path Tracing](path-tracing.md), [Materials and Shaders](materials-and-shaders.md), [ASE Shader Workflow](ase-shader-workflow.md), [Shader Variant Stripping](shader-variant-stripping.md), [Third-Party Source Imports](third-party-source-imports.md) |

Inline ray tracing traces ray traced global illumination (RTGI) and path tracing without Unity ray tracing shaders. Unity prepares hit groups for every instance of every acceleration structure bound to its ray generation dispatches, on every dispatch, so that path's CPU cost grows with the size of the scene. Instead, each tracing stage is a ray generation program that the native backend dispatches. Rays traverse the scene with ray queries, and each accepted closest hit is shaded by its material's own module after the GPU regroups rays by the material they hit. The shader table holds one record per material evaluator, never one per instance. Each material is still evaluated from its own `PathTracing` pass source, so the result is the image that pass defines.

## Goal

- RTGI and path tracing prepare no hit groups. Their per-frame CPU cost is independent of the number of instances beyond the ray tracing scene's own update.
- Closest-hit shading runs coherently: rays are regrouped by the material they hit before shading, on GPUs that support shader execution reordering, and each material's shading code runs only for its own hits.
- Traversal stays inside the tracing program: deciding whether a candidate surface occludes a ray never leaves the ray generation program.
- Every material is evaluated with its own material inputs, coverage and lighting rules. Converged RTGI and path tracing results do not depend on how rays are traced.
- Candidate cost does not grow with the number of materials: the code that traversal runs for the common clipping case is the same for every material that uses it.
- Materials are evaluated at the hit from their authored textures and properties. The only data derived from them are opacity micromaps, which settle candidates whose outcome is already certain and never change a result.
- Inline evaluation depends only on geometry and material properties. It never depends on what an object represents in the application, its layer or its scene role.

## Scope

- Every tracing stage of RTGI in Performance and Quality modes: the GI ray, secondary surface shading with its light sampling visibility, the radiance cache update, ray traced bias correction in resampling, and final shading.
- Path tracing in Reference and Realtime modes.
- Static meshes, skinned meshes, blend-shaped meshes and meshes whose data is written in place, such as simulated cloth.
- Every hand-written and ASE-generated shader whose `PathTracing` pass builds on the shared PathTracing hit library.
- The scene resource table, the per-material shader table and the native dispatch of tracing programs.

## Non-goals

- PRT baking. It keeps Unity ray tracing shaders.
- A unified or hand-written evaluator of material shading that replaces the materials' own `PathTracing` source. Coverage that follows the standard model is evaluated from captured material data instead (see [Standard coverage](#standard-coverage)); it is not a separate model of the material.
- Baked material atlases, averaged material colors, screen-space coverage approximations, or any other precomputation that stands in for evaluating a material at the hit. [Opacity micromaps](#opacity-micromaps) are not such a substitute: they only skip candidates whose evaluation could not change the outcome.
- A hit-shader path for RTGI or path tracing.
- Procedural and AABB geometry.
- Changes to the RTGI estimate, resampling or denoising contracts, or to path tracing integration, beyond how rays are traced and hits are evaluated.

## Tracing programs

- **Stages.** Each tracing stage is one ray generation program: RTGI Performance initial samples, RTGI Quality initial samples, the radiance cache update, RTGI temporal resampling, spatial resampling and final shading, path tracing Reference, and the two Realtime path tracing passes. A program holds the stage's own integration and the traversal, not material shading.
- **Dispatch.** Unity binds a stage's resources, globals and output targets to a compute binding carrier as it would for any compute pass. The native backend replaces that carrier's dispatch with the stage's ray generation dispatch over the same bindings, on the same command list and in the same pass order. A stage never sees Unity's ray tracing shader bindings.
- **Shader table.** The native backend owns one shader table per program. It holds one record per material evaluator, plus the miss record. Adding, removing or moving instances does not change it. It changes only when the set of evaluators changes.
- **Pipelines.** A program's pipeline links the stage with the material modules. It is created when the program or the module set changes, never per frame. Pipeline creation may take noticeable time the first time a driver sees it; it is not a per-frame cost.
- **Visibility stages.** RTGI resampling and final shading trace visibility rays only. Their programs link the coverage library and no material modules, so their pipelines do not change with the module set.

## Acceleration structure

Tracing programs trace the [ray tracing scene](ray-tracing-scene.md)'s acceleration structure. Neither binds it, or any other structure, to a Unity ray generation dispatch, because binding one makes Unity prepare hit groups for all of its instances, even when the dispatch uses only ray queries. Participation, change tracking, culling and the once-per-frame build stay as the ray tracing scene defines them.

Unity builds the structure. The native backend does not replace it; it only attaches [opacity micromaps](#opacity-micromaps) to the bottom-level structures Unity builds for eligible geometry. Instances, their order, their geometry indices and the top-level build are unchanged.

## Ray traversal

- **Coverage.** A candidate hit on non-opaque geometry decides its coverage inside the traversal loop, in one of two forms. A material whose `PathTracing` pass declares [standard coverage](#standard-coverage) is decided by the shared standard evaluation from its captured coverage data. Any other material runs its own coverage, compiled once into a shared coverage library that each tracing program links when its pipeline is created. In both forms coverage, stochastic coverage, face culling, shadow-caster and shadow-layer rules and colored transmission are the same as the material's `PathTracing` rules for that ray kind ([Path Tracing](path-tracing.md#scene-and-material-inputs)). A candidate never calls into a material module, so traversal state stays with the program.
- **Opaque geometry.** Geometry whose materials have no coverage is traversed as opaque and runs no candidate code.
- **Micromaps.** On geometry with opacity micromaps, rays that may use them resolve certainly opaque and certainly clear regions without candidate code. Only the remaining candidates run coverage.
- **Closest hits.** A ray's accepted closest hit is shaded by its material's module. Rays are regrouped by the material they hit before the module runs, on GPUs that support shader execution reordering. Other GPUs shade in the original order with the same result.
- **Visibility.** Visibility rays accumulate colored transmission through transmitting surfaces during traversal and need no closest-hit shading.
- **Misses.** A ray that reaches no accepted hit sees the lighting environment.

## Exceptions

An instance has no inline evaluator for a submesh in three cases:

- the submesh's material has no `PathTracing` pass
- the pass uses hit-shader features that inline evaluation does not provide, so its evaluator cannot be generated
- the resource table cannot hold the instance's resources

That submesh is evaluated as the same diagnostic surface that path tracing shows for unsupported material entry points: an opaque surface of a fixed diagnostic color. It is never substituted with another material model. Exceptions are reported once per material in the Editor and development builds. A material whose shader gains an evaluator leaves the exception list at the next scene update.

## Scene resource table

The ray tracing scene service owns a shader-visible resource table that tracing programs and the scene's compute work index. It holds the textures that materials reference and the vertex and index data of participating geometry. Instance, geometry and material records refer to its entries by index.

- **Provider.** The table is provided by the optional UnityRHI native backend that RTGI's denoiser already requires. Its native module enlarges Unity's shader-visible descriptor heap, gives compute root signatures an indexed resource range without moving their other resources, and creates the tracing programs' pipelines over the same range. Unity's own ray tracing pipelines are not extended.
- **Loading.** The module must load before the graphics device is created. Once a renderer in the project enables RTGI or path tracing, the Editor places it in the project, which the Editor and players load at startup, and offers to restart the Editor when it changes. Both start off in a new renderer feature, so a project that never enables them gets neither the module nor generated programs. Loading that early also lets it select the D3D12 runtime the backend ships ([Runtime](#opacity-micromaps)).
- **Unavailable.** Without the backend, on a device without ray queries, or when a stage's program or records are not ready, RTGI does not render its indirect lighting and path tracing renders black. The reason is reported. There is no hit-shader fallback.
- **Imports.** The native hooking library and the descriptor-table technique are imported sources tracked by [Third-Party Source Imports](third-party-source-imports.md).
- **Registration.** A resource gets an entry when a record first references it. Its entry is released once no record references it and no GPU work in flight still reads it.
- **Replacement.** A texture reimport, a mesh upload or a recreated render texture changes the native resource behind a record's reference. The record moves to an entry for the new resource before the frame's tracing work reads it, and the old entry is released like any other. An entry is never rewritten while GPU work in flight can still read it, so tracing never reads a released or stale resource.

## Geometry records

- **Geometry order.** A traversal reports a geometry index in the order the acceleration structure assigns to an instance's submeshes. That order leaves out disabled submeshes. Each instance's records map those indices back to their submeshes and materials.
- **Attributes.** Records describe the mesh's vertex layout and formats. Inline evaluation reconstructs the hit's interpolated attributes from them: positions, normals, tangents, texture coordinates and vertex color, with the precision a hit shader would receive. Read/Write import is not required.
- **Deforming geometry.** Skinned, blend-shaped and in-place meshes reference the deformed data the acceleration structure was built from in the same frame. Each deformed buffer has its own entry. A renderer that alternates between deformed buffers from frame to frame switches its records between those entries every frame. Neither consumer needs previous-frame geometry at secondary surfaces.

## Standard coverage

Most clipped and blended surfaces decide coverage the same way, and differ only in their data. Those materials share one evaluation instead of each running a copy of its own.

- **Model.** Coverage alpha is the alpha of one material texture, sampled at its finest level at one of the hit's texture coordinate sets, with the material's scale and offset when the pass applies one, times the alpha of one material color. Alpha-tested surfaces reject hits whose alpha is below the material's cutoff. Blended surfaces transmit `1 - alpha` to visibility rays and accept other rays stochastically by alpha, unless the material's own rules treat the surface as fully covering those rays. Shadow-caster and shadow-layer rules, face culling, transmission skipping and random-walk recording apply as they do for any coverage.
- **Declaration.** A shader's `PathTracing` pass declares that its coverage follows the model and names the properties that supply the texture, its scale and offset, the texture coordinate set, color and cutoff, and the keywords that select alpha testing, blending and full coverage. Hand-written passes declare it in source; ASE templates expose it as a template option. A pass without the declaration keeps its own coverage.
- **Data.** Each material's coverage inputs are captured into its material record with the rest of its inputs: the texture's resource table entry, scale and offset, the color and the cutoff. Change tracking recaptures them like any other input. The behavior follows from the variant's keywords.
- **Equivalence.** For a declared pass, the standard evaluation decides the same coverage the pass's own coverage would, including the texture's filtering at its finest level. A pass whose coverage the model cannot express, such as hair, does not declare it.
- **Cost.** The standard evaluation is one compact routine in the tracing programs. Adding materials or variants that declare standard coverage adds data, not code.

## Opacity micromaps

Alpha-tested surfaces such as foliage spend most of their traversal on candidates whose coverage texture is plainly solid or plainly empty where the ray crosses it. An opacity micromap records, for small regions of each triangle, whether coverage there is certainly opaque, certainly clear or uncertain. Traversal accepts or skips certain regions itself and runs coverage only for uncertain ones.

- **Eligibility.** Static geometry whose materials declare [standard coverage](#standard-coverage) in its alpha-tested form, while playing: in Play Mode and in players. Blended surfaces, coverage that is not standard, deforming geometry and the Editor outside Play Mode have no micromaps.
- **Certainty.** A region is certain only when the standard evaluation would reach the same decision for every point in it, with the filtering coverage applies at the texture's finest level, for every material that shares the geometry's bottom-level structure. Everything else is uncertain. A micromap therefore never changes which hits are accepted.
- **Rays.** Every tracing program requests micromaps. Traversal skips certainly clear regions and treats certainly opaque regions as opaque, unless the ray forces every surface to be a candidate. Rays that must see every covered surface, such as rays that apply shadow-layer exceptions or record random-walk exits, force candidates and still see every covered region; micromaps only skip regions their coverage would reject. Tracers that cannot request micromaps, such as Unity's ray tracing shaders for PRT baking, run only outside Play Mode and so never trace a structure that has them.
- **Generation.** Micromaps are generated on the GPU at runtime from the coverage data the standard evaluation reads and the geometry's texture coordinates, so content loaded at runtime gets them too. They are kept per geometry and coverage data. Until a micromap is ready, its geometry is traced without one.
- **Attachment.** The native backend extends Unity's bottom-level builds of eligible geometry with its micromaps; Unity's later compaction of those structures keeps them. Micromaps are prepared before the first build of their geometry's structure.
- **Lifetime.** Unity can keep a renderer's bottom-level structures after the renderer leaves every acceleration structure, for example while it is culled or disabled, and reuse them when it returns. A micromap therefore lives as long as any renderer whose structure may reference it, not only while its geometry is in the scene.
- **Coverage changes.** Unity rebuilds a renderer's structure only when the structure's build settings change, and can return to the earlier structure when they change back. When the coverage data of geometry already traced with a micromap changes, or a material with different coverage starts sharing it, the geometry loses its micromap: its renderers, and renderers that use it later, move to structures built without micromaps for the rest of their lifetime. Their coverage then runs for every candidate, so images stay correct.
- **Runtime.** Micromaps in ray queries need a D3D12 runtime with Shader Model 6.9. The native backend ships such a runtime and selects it for the process before the graphics device is created. When the process already runs another runtime, micromaps are off, the reason is reported, and everything else behaves as without them.

## Inline material evaluation

- **Single source.** A shader variant's `PathTracing` program is compiled once with its keywords and libraries. Its closest-hit entry point becomes the material module that shades accepted hits. Its coverage, the any-hit entry point, is compiled from the same source into the shared coverage library unless the pass declares standard coverage. No material family has separate evaluator code, and passes need no changes for it.
- **Code placement.** Tracing programs hold the stage's integration and the traversal; they link the materials' coverage and do not hold their shading. Each program traverses from a single site that its scattered and visibility rays share, so the linked coverage is merged into it once. Shading code lives only in the modules, so a module runs only for its own hits and has its own register budget, and a program does not grow with the materials' shading.
- **Hit environment.** The inline environment stands in for everything a hit shader receives from Unity: ray and instance intrinsics, the geometry's vertex and index data, per-object transforms and the material's properties and textures. Evaluators of different shaders and variants are isolated from one another, while the scene and frame globals they share are declared once.
- **Material inputs.** Material property values, texture scale and offset, and renderer property overrides are captured into material records. Change tracking recaptures them when the material or override content changes. Each hit reads its own record, so one module shades every material of its variant.
- **Texture sampling.** Texture lookups resolve through the scene resource table. They keep each texture's filtering, including the anisotropic filtering that quality settings force, and its addressing, and use the same footprint and level selection as a hit shader would. Coverage samples at the finest level, as [path tracing](path-tracing.md#scene-and-material-inputs) defines.
- **Variants.** A material's enabled `PathTracing` keywords select its evaluator variant. Evaluators are generated when shaders import, follow [Shader Variant Stripping](shader-variant-stripping.md), and are reachable in a build exactly when RTGI or path tracing is. A variant with no evaluator in a player is an exception.
- **Generation.** Generation runs only while a renderer in the project enables RTGI or path tracing. Turning both off keeps the existing outputs and stops regenerating them. Modules, the coverage library and tracing programs are regenerated when shader sources, their includes or the materials that select their variants change. A change to a material's coverage rebuilds the coverage library, not the tracing programs. A variant whose evaluator does not compile is reported with its compiler errors and becomes an exception. The other evaluators are unaffected. Outputs that no current program references are removed.

## RTGI integration

- **Initial samples.** In both modes the GI ray is traced by the mode's program. Its hit is shaded with the RTGI [secondary surface contract](ray-traced-global-illumination.md#secondary-surfaces), including next-event estimation over the shared light set with visibility traced the same way.
- **Radiance cache.** In Quality mode the cache update is traced by its own program.
- **Resampling and final shading.** Ray traced bias correction and final shading trace their visibility rays from their own programs.

## Path tracing integration

Reference and Realtime path tracing trace every path segment with their programs, with the [path tracing](path-tracing.md) material and lighting contracts unchanged.

## Acceptance

- With RTGI in either mode or path tracing on, no hit groups are prepared, and render-thread cost does not grow with the scene's instance count.
- Converged RTGI and path tracing match the materials' `PathTracing` contracts in these cases:
  - scenes with textured, alpha-tested, dithered, emissive and property-overridden props
  - skinned characters in motion, including skin, hair cards and cloth
- Alpha-tested and dithered geometry and hair cards occlude and transmit correctly for GI and visibility rays, with no call into a material module during traversal.
- Materials that declare standard coverage render the same images as with their own coverage, and the coverage code that traversal runs does not grow with their number.
- With opacity micromaps, images are the same as without them, and alpha-tested static geometry runs coverage only in uncertain regions. Content loaded at runtime gets micromaps without an Editor step.
- Without the shipped D3D12 runtime, micromaps are reported as unavailable and every other behavior is unchanged.
- Exceptions render as the diagnostic surface and are reported.
- Meshes without Read/Write participate in players.
- Without the native backend or on unsupported devices, the reason is reported and no hit-shader path runs.
- Reimporting textures and meshes, changing materials or overrides, and adding or removing renderers or characters at runtime leave no stale or released resource reachable from tracing work.
