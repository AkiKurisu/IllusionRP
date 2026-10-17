# ASE Shader Workflow

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Living |
| Date | 2026-10-18 |
| Related Specs | [Materials and Shaders](materials-and-shaders.md), [Shader Variant Stripping](shader-variant-stripping.md) |

ASE authoring separates reusable rendering contracts from material inputs. Changes must reach both their source assets and generated shader output without replacing authored graph intent.

## Sources

| Source | Responsibility |
|---|---|
| Template | Pass topology, rendering state, material layout and authoring ports/options. |
| Graph | Material inputs, connections and chosen options. |
| ASE function | Reusable input subgraph shared by referencing graphs. |
| Shared shader include | Common surface, lighting, coverage and packing behavior. |
| Generated shader | Exported result of those sources, not an independent implementation. |

Direct edits to generated code are overwritten. Implement changes in the owning source and regenerate affected output.

## Templates and functions

The package supplies Lit, Unlit, Skin, Hair, Fabric and Water templates. Templates are also application authoring interfaces: their ports, options, properties and passes must remain meaningful for external graphs. Shared functions preserve asset identity so referencing graphs resolve the intended source.

## Impact

Template and function changes affect every referencing graph. A graph-only edit affects that material graph. Shared includes reach their consumers on import; exporting is necessary when embedded template/function output changes.

Identify affected package graphs and regenerate them in the same change. Material-property changes also update affected package materials. External graph consumers need re-export when their generated template text becomes stale.

## Export and convergence

Load and export graphs independently to avoid shared Editor state contaminating another graph. Preserve both authored pass selection and option choices; changed connections must be reflected in connection-driven options.

After a topology or connection change, reload and export until another export produces no semantic change. Review pass identity, selected options and connections against the intended change; a successful export alone does not prove preservation. Generated metadata must agree with the exporting ASE version.

## Generated shader contract

Generated output satisfies [Materials and Shaders](materials-and-shaders.md), which owns the pass contract, cross-pass coverage and the [shader data layout and sampler rules](materials-and-shaders.md#shader-data-layout). Verification after export covers the combined feature configurations that consume the generated shader; a successful export or compile does not prove those rules.
