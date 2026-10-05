# ASE Shader Workflow

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-05 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Shader Variant Stripping](shader-variant-stripping.md) |

IllusionRP's template-based material shaders are authored with Amplify Shader Editor (ASE). The package ships one ASE template per shading family; a graph supplies the material inputs, and ASE exports template, graph and functions into one generated shader. This workflow defines source ownership and how changes reach the generated shaders.

## Sources

| Source | Owns |
|---|---|
| Template | SubShader and pass topology, pass names and LightMode tags, fixed render state, the `UnityPerMaterial` buffer of every pass and the fields it declares, the master node ports, and the Additional Options that add or remove passes, defines and properties. |
| Graph | Material nodes, textures, properties and the chosen option values. ASE stores the graph inside the generated shader file, in the serialized block after the shader code, and adds the graph's material properties to every pass's `UnityPerMaterial` buffer. |
| ASE function | A reusable node subgraph. Graphs reference it by asset GUID and ASE expands it into the generated code at export. |
| Shared HLSL include | Surface setup, BRDF and lighting, global illumination, coverage and packing. Templates include it by package path and the generated shader keeps the include directive. |
| Generated shader | The code ASE writes from the template, the graph and its functions. |

Changes to generated code are made in its template, graph, function or include and exported again; an export overwrites direct code edits.

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

Templates are also a public authoring surface: applications build their own graphs on them. Those graphs keep the template text of their last export, so a template change reaches them when they are exported again. Ports, options, properties and passes form the template's authoring interface.

## Impact

Graphs reference their template and functions by GUID in their serialized block.

- **Template.** Affects every graph exported from it. Pass layout, `UnityPerMaterial` and shared coverage changes usually affect all of them.
- **Function.** Affects every graph that references it.
- **Graph.** Affects only that graph.
- **Shared include.** Reaches every generated and hand-written shader that includes it on import. An export is needed only when the template text around the include changes.

Affected package graphs are exported with their template or function change. Renamed or removed material properties also update the package materials that use them.

## Export and convergence

- **Editor state.** Each graph is loaded, exported and saved separately because shared editor state, such as master pass data, can carry into the next graph.
- **Authored pass selection.** Available Passes is separate from Custom Options. Preserve both when rebuilding template nodes; restore pass visibility by pass name. Background exports apply the option actions before saving, so their selected defines and pragmas reach the generated code. Unless pass topology is intentionally changed, the exported pass names and LightMode tags must match the graph's previous output.
- **Port-driven options.** `Port:` entries use the connection state seen when the graph loads. After connections change in the same session, reload and export applies the matching defines, such as `_NORMALMAP`, `_EMISSION`, `ASE_BAKEDGI`, `_GBUFFER_NORMAL_OVERRIDE` and `_GBUFFER_SMOOTHNESS_OVERRIDE`.
- **Pass layout.** After a template pass layout change, the first export can leave stale master pass data in the graph; a subsequent reload and export clears it. Options that exclude a pass drop connections into that pass's master node on reload.
- **Export metadata.** Each generated shader records the exporting ASE version in its header and graph metadata. Generated shader sources use CRLF line endings.

## Generated shader contract

- The generated shader satisfies [Materials and Shaders](materials-and-shaders.md) for pass names, LightMode tags, render state, `UnityPerMaterial` layout, keywords and stencil properties.
- Alpha clip, double-sided, vertex offset, normal reconstruction, LOD cross-fade and coverage agree across every pass the template provides: main color, Forward GBuffer, depth, post depth, OIT, motion vector, shadow and subsurface.
- Texture reads outside the fragment stage use an explicit LOD.
- Resource access remains compatible with SRP Batcher, GPU Resident Drawer, DOTS instancing and the target platform limits.
