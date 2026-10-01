# Upstream Source Migration

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |
| Related Specs | [Render Resources](render-resources.md), [ASE Shader Workflow](ase-shader-workflow.md), [Validation](validation.md) |

IllusionRP ports algorithms from reference renderer sources into its URP renderer feature and shader library: Unity Graphics (SRP Core, URP and HDRP) and third-party renderers or shader libraries imported as C#, shader or HLSL source. This workflow defines how a reference source is pinned, imported, adapted and reviewed so that every port stays mechanically comparable with its origin.

Out of scope: ASE-generated shaders, covered by [ASE Shader Workflow](ase-shader-workflow.md), and the verification layers, covered by [Validation](validation.md).

## Pinning

- **Exact revision.** Before porting, the exact reference revision is established: for Unity Graphics, the Unity, SRP Core, URP and HDRP versions and the tag or commit; for any other source, the repository and commit. The port reads the source at that revision. Behavior of an older version is confirmed from its tag or commit, never from memory.
- **One revision.** A port never mixes code from different revisions or versions.
- **Algorithm slice.** The unit of migration is a complete algorithm slice, not a single kernel: C# orchestration and render graph scheduling; raster and compute shaders and shared HLSL; constants and generated layouts; resource declarations, history and denoisers; and the sampling, packing and validity logic they depend on directly.
- **No local paths.** Specs, code and commit messages identify a source by repository and revision, never by a path on one machine.

## Import

- **Unmodified import.** A reference source enters history first as an unmodified import in its own commit. The commit message records the source repository and the exact pinned revision.
- **Separate adaptation.** Adaptations follow in separate commits, so the diff from the import commit is the complete record of what IllusionRP changed.
- **Notices.** License and attribution notices that an imported source requires are kept as the source provides them: in file headers, or as license files next to the imported code.

## Default strategy

When the reference source is available, the slice is copied and then adapted; it is not read and re-implemented as an approximation. The port keeps file and section organization, function and kernel boundaries, names, constants, profiling names, thread group sizes, keywords, execution order, control flow, and upstream comments that still apply. Algorithm bodies are not simplified, merged or renamed to fit local style.

| Class | Allowed scope |
|---|---|
| Copy | Upstream structure and behavior are kept; only necessary names, include paths and resource references change. |
| Adapt | The algorithm structure is kept; host seams are replaced. |
| Rewrite | Only code that cannot be shared: surface data and BRDF, light and probe production, packing, or an integration seam that URP does not have. |

A rewrite stays at the boundary, and the surrounding upstream structure remains mechanically comparable. A port that needs to change algorithm behavior, data layout or broad control flow stops for a design review first.

## Host seams

An adaptation may change only these seams:

- namespaces, assemblies and include paths;
- HDRP camera and frame settings, mapped to URP camera data, Volume components and renderer feature settings;
- HDRP render graph and frame data resources, mapped to URP public handles or IllusionRP-owned resources;
- per-camera history allocation, validity and reset;
- HDRP macros, mapped to semantically equivalent URP or IllusionRP macros;
- output publication, through IllusionRP's existing producer and consumer seams in [Render Resources](render-resources.md).

A Unity capability that seems missing is first looked for in its declaring assembly and in the package's internal bridges, the assembly references that give IllusionRP access to SRP Core and URP internals. Reflection or per-frame scene scans are used only after the seam is shown to be unavailable.

## Difference markers

- **Marked changes.** Every modification inside an imported reference file carries an `@IllusionRP` marker: a `// @IllusionRP: reason` comment line before the change, a trailing `// @IllusionRP` on a single changed line, or `// @IllusionRP Start` and `// @IllusionRP End` around a multi-line block. The reason names the upstream behavior and why IllusionRP differs, such as a behavior difference, a non-obvious host adaptation, or an upstream or compiler fix.
- **Mechanical changes.** Include path and namespace changes carry no marker.
- **Own files.** Files written without a reference source do not use the marker; it records differences from a source, not authorship.
- **Upstream comments.** Upstream comments that remain correct are kept.

## Review

Before delivery, the port is reviewed as a structural diff against the pinned slice, starting from the import commit:

- every algorithm difference has a marker and a reason;
- every host seam maps to a current IllusionRP owner and resource contract;
- constants, kernels, thread groups, keywords, history, signal ranges and pass handoffs are complete;
- nothing was rewritten from memory or mixed in from another revision;
- validation covers the affected assemblies, shaders, resources, history and final consumers, as [Validation](validation.md) requires.
