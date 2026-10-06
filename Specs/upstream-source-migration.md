# Upstream Source Migration

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Render Resources](render-resources.md), [ASE Shader Workflow](ase-shader-workflow.md) |

Ports adapt a reference renderer to IllusionRP while preserving a reviewable relationship with the source. This applies to Unity Graphics and third-party renderer or shader code.

## Pinning

Record the exact source repository/revision and relevant engine/pipeline versions. Import a complete algorithm slice, including orchestration, shader dependencies, history, resource handling and validity rules; an isolated kernel is not the whole feature.

## Import

Keep the unmodified import in a separate source-history commit with its revision, followed by adaptation commits. Preserve required license and attribution notices. Source history carries implementation differences; specifications describe the resulting design contract.

## Default strategy

Prefer adapting host integration over rewriting the algorithm. Preserve upstream structure sufficiently for mechanical comparison. Necessary rewrites stay at boundaries the host cannot share, such as material response, light production or output representation, and retain the surrounding reference structure.

## Host seams

Map upstream camera settings, Volume controls, frame resources and history into existing URP and IllusionRP ownership. Preserve the meaning of inputs and outputs across convention changes. Publication follows [Render Resources](render-resources.md); generated material changes follow [ASE Shader Workflow](ase-shader-workflow.md).

## Difference markers

Mark substantive deviations inside imported files with the established `@IllusionRP` convention and a short reason for the behavioral or host difference. Mechanical path/namespace changes need no marker. Original IllusionRP files do not use markers as authorship labels. Retain upstream comments that remain correct.
