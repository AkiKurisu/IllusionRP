# Third-Party Source Imports

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Living |
| Date | 2026-10-18 |
| Related Specs | [Render Resources](render-resources.md), [ASE Shader Workflow](ase-shader-workflow.md) |

Imported source adapts Unity Graphics or third-party renderer and shader code to IllusionRP while keeping a reviewable relationship with its origin. Source history records where code came from and how it differs; specifications describe only the resulting design.

## Pinning

Record the exact source repository and revision and the relevant engine and pipeline versions. Import a complete algorithm slice, including orchestration, shader dependencies, history, resource handling and validity rules; an isolated kernel is not the whole feature.

A component delivered as both a native library and shader source is pinned to one revision for both, including the data encodings they exchange.

## Import

Keep the unmodified import in a separate source-history commit with its revision, followed by adaptation commits.

Imported sources keep their license and attribution notices and remain under their original license. The RTXDI SDK, NRD and SHaRC imports keep their NVIDIA notices and are distributed under the NVIDIA RTX SDKs license.

## Default strategy

Prefer adapting host integration over rewriting the algorithm. Preserve the imported structure sufficiently for mechanical comparison. Necessary rewrites stay at boundaries the host cannot share, such as material response, light production or output representation, and retain the surrounding imported structure.

## Host seams

Map imported camera settings, Volume controls, frame resources and history into existing URP and IllusionRP ownership. Preserve the meaning of inputs and outputs across convention changes. Publication follows [Render Resources](render-resources.md); generated material changes follow [ASE Shader Workflow](ase-shader-workflow.md).

## Difference markers

Mark substantive deviations inside imported files with the established `@IllusionRP` convention and a short reason for the behavioral or host difference. Mechanical path and namespace changes need no marker. Original IllusionRP files do not use markers as authorship labels. Imported comments that remain correct are retained.
