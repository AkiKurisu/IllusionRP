# Upstream Source Migration

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Render Resources](render-resources.md), [ASE Shader Workflow](ase-shader-workflow.md) |

IllusionRP ports algorithms from reference renderer sources into its URP renderer feature and shader library: Unity Graphics (SRP Core, URP and HDRP) and third-party renderers or shader libraries imported as C#, shader or HLSL source. This workflow defines source identity, import history and adaptation boundaries so that ports remain mechanically comparable with their origins.

## Pinning

- **Revision.** A port identifies one source revision: for Unity Graphics, the Unity, SRP Core, URP and HDRP versions and the tag or commit; for any other source, the repository and commit.
- **Algorithm slice.** The unit of migration is a complete algorithm slice, not a single kernel: C# orchestration and render graph scheduling; raster and compute shaders and shared HLSL; constants and generated layouts; resource declarations, history and denoisers; and the sampling, packing and validity logic they depend on directly.

## Import

- **Unmodified import.** A reference source enters history first as an unmodified import in its own commit. The commit message records the source repository and the exact pinned revision.
- **Separate adaptation.** Adaptations follow in separate commits, so the diff from the import commit is the complete record of what IllusionRP changed.
- **Notices.** License and attribution notices that an imported source requires are kept as the source provides them: in file headers, or as license files next to the imported code.

## Default strategy

The default strategy is to copy the algorithm slice and adapt its host integration. The port retains file and section organization, function and kernel boundaries, names, constants, profiling names, thread group sizes, keywords, execution order, control flow and applicable upstream comments.

| Class | Allowed scope |
|---|---|
| Copy | Upstream structure and behavior are kept; only necessary names, include paths and resource references change. |
| Adapt | The algorithm structure is kept; host seams are replaced. |
| Rewrite | Only code that cannot be shared: surface data and BRDF, light and probe production, packing, or an integration seam that URP does not have. |

A rewrite stays at the integration boundary, with the surrounding upstream structure mechanically comparable.

## Host seams

Host adaptations cover:

- namespaces, assemblies and include paths;
- HDRP camera and frame settings, mapped to URP camera data, Volume components and renderer feature settings;
- HDRP render graph and frame data resources, mapped to URP public handles or IllusionRP-owned resources;
- per-camera history allocation, validity and reset;
- HDRP macros, mapped to semantically equivalent URP or IllusionRP macros;
- output publication, through IllusionRP's existing producer and consumer seams in [Render Resources](render-resources.md).

## Difference markers

- **Marked changes.** Every modification inside an imported reference file carries an `@IllusionRP` marker: a `// @IllusionRP: reason` comment line before the change, a trailing `// @IllusionRP` on a single changed line, or `// @IllusionRP Start` and `// @IllusionRP End` around a multi-line block. The reason names the upstream behavior and why IllusionRP differs, such as a behavior difference, a non-obvious host adaptation, or an upstream or compiler fix.
- **Mechanical changes.** Include path and namespace changes carry no marker.
- **Own files.** Files written without a reference source do not use the marker; it records differences from a source, not authorship.
- **Upstream comments.** Upstream comments that remain correct are kept.
