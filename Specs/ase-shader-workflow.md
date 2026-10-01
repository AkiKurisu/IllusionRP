# ASE Shader Workflow

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Shader Variant Stripping](shader-variant-stripping.md), [Validation](validation.md) |

IllusionRP's material shaders are authored with Amplify Shader Editor (ASE). The package ships one ASE template per shading family; a graph built on a template supplies the material inputs, and ASE exports template, graph and functions into one generated shader. This workflow defines what each source owns, how a change reaches the generated shaders, and when an export is complete.

Out of scope: ASE itself, which is installed separately and is not part of this repository's packages; hand-written shaders such as Hybrid Lit and Hybrid Complex Lit; and the pass, stencil, keyword and `UnityPerMaterial` contracts, which [Materials and Shaders](materials-and-shaders.md) owns.

## Sources

| Source | Owns |
|---|---|
| Template | SubShader and pass topology, pass names and LightMode tags, fixed render state, the `UnityPerMaterial` buffer of every pass and the fields it declares, the master node ports, and the Additional Options that add or remove passes, defines and properties. |
| Graph | Material nodes, textures, properties and the chosen option values. ASE stores the graph inside the generated shader file, in the serialized block after the shader code, and adds the graph's material properties to every pass's `UnityPerMaterial` buffer. |
| ASE function | A reusable node subgraph. Graphs reference it by asset GUID and ASE expands it into the generated code at export. |
| Shared HLSL include | Surface setup, BRDF and lighting, global illumination, coverage and packing. Templates include it by package path and the generated shader keeps the include directive. |
| Generated shader | The code ASE writes from the template, the graph and its functions. It is committed and imported as is, but it is never where a problem is fixed. |

A defect in a generated shader is fixed in the template, graph, function or include, and the shader is exported again. Hand edits to generated code hide the source problem and are lost at the next export.

## Templates and functions

| Template file | Template shader | Graph exported in the package |
|---|---|---|
| `Shaders/Lit/Hybrid Lit Template.shader` | `Hidden/Universal/Hybrid Lit` | `Universal Render Pipeline/HD Lit` |
| `Shaders/Lit/Hybrid Unlit Template.shader` | `Hidden/Universal/Hybrid Unlit` | none |
| `Shaders/Fabric/Fabric Template.shader` | `Hidden/Universal/Fabric` | `Universal Render Pipeline/HD Fabric` |
| `Shaders/Skin/Skin Template.shader` | `Hidden/Universal/Skin` | `Universal Render Pipeline/HD Skin` |
| `Shaders/Hair/Hair Template.shader` | `Hidden/Universal/Hair` | `Universal Render Pipeline/HD Hair` |
| `Shaders/Water/Water Template.shader` | `Hidden/Universal/Water` | `Universal Render Pipeline/Water` |

Paths are relative to the package root, and each generated shader sits next to its template. The package's ASE functions live in `ShaderLibrary/ASE`: `HD Surface Input`, used by HD Lit, HD Fabric, HD Skin and HD Hair, and `Normal Strength` and `Tilling And Offset`, used by Water.

Templates are also a public authoring surface: applications build their own graphs on them. Those graphs keep the template text of their last export, so a template change reaches them only when their owners export again. A change that renames, removes or repurposes a template port, option, property or pass breaks those graphs and is noted in the changelog.

## Impact

Affected graphs are found by asset identity, not by name: a graph names its template and its functions by GUID in its serialized block.

- **Template.** Affects every graph exported from it. Pass layout, `UnityPerMaterial` and shared coverage changes usually affect all of them.
- **Function.** Affects every graph that references it.
- **Graph.** Affects only that graph.
- **Shared include.** Needs no export unless the template text around it changes, but reaches every generated and hand-written shader that includes it on import; all of them are validated.

Only the affected set is exported; a package-wide re-export is not the default. Every graph in the package that a template or function change affects is exported in the same change, so no generated shader lags its template. When an export renames or removes material properties, the repository's materials that use the shader are updated in the same change.

## Export and convergence

- **Idle editor.** An export starts only when Unity has compiled scripts, reloaded the domain, imported assets and compiled shaders.
- **One graph at a time.** Each graph is loaded, exported and saved on its own, and the editor and shader import return to idle before the next one. Several graphs are not exported through shared editor state in one batch, because state from one graph, such as master pass data, can carry into the next.
- **Convergence.** After the whole affected set has been exported once, it is exported again the same way. The change is complete only when an export produces no diff in templates, graphs or generated shaders; until then the set is exported again.

The repeated export is required because:

- Options driven by port connections (`Port:` entries) apply from the connection state seen when the graph loads. A graph whose connections changed in the same session exports without the matching defines, for example `_NORMALMAP`, `_EMISSION`, `ASE_BAKEDGI`, `_GBUFFER_NORMAL_OVERRIDE` or `_GBUFFER_SMOOTHNESS_OVERRIDE`, until it is loaded and exported again.
- After a template pass layout change, the first export can leave stale master pass data in the graph; the next export clears it.
- When a graph's options exclude a pass, connections into that pass's master node are dropped on reload. This is expected and is not an export failure.

## Diff review

- **Together.** Template, function and include diffs are reviewed together with the generated shader diffs.
- **Expected changes only.** A generated diff contains only the expected pass, define, property, graph metadata and checksum changes. Unrelated passes, code from an older template, duplicated properties or broad structural churn mean the wrong template state was loaded: the export is reverted, the template reloaded and the graph exported again.
- **ASE version.** Each generated shader records the exporting ASE version in its header and graph metadata. That version changes only when ASE is upgraded.
- **Text format.** Generated shaders follow the repository's line endings for shader sources (CRLF) and never mix line endings.
- **Cleanup.** Temporary export lists, editor helpers, intermediate assets and orphaned `.meta` files are removed before the change is committed.

## Generated shader checks

- The generated shader satisfies [Materials and Shaders](materials-and-shaders.md) for pass names, LightMode tags, render state, `UnityPerMaterial` layout, keywords and stencil properties.
- Alpha clip, double-sided, vertex offset, normal reconstruction, LOD cross-fade and coverage agree across every pass the template provides: main color, Forward GBuffer, depth, post depth, OIT, motion vector, shadow and subsurface.
- Texture reads outside the fragment stage use an explicit LOD.
- A new resource access in any stage is checked against SRP Batcher, GPU Resident Drawer, DOTS instancing and platform limits.

## Completion

- The affected templates, functions, includes and generated shaders import without errors on the target graphics APIs, and Unity is idle.
- The last export produced no diff.
- No new shader, keyword, `UnityPerMaterial`, SRP Batcher, GPU Resident Drawer or pass layout errors.
- Player builds keep every pass the target renderers reach, as [Shader Variant Stripping](shader-variant-stripping.md) requires.
- The change is validated as [Validation](validation.md) requires for ASE templates and graphs.
