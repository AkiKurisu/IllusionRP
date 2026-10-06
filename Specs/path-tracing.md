# Path Tracing

| Field | Value |
|---|---|
| Version | 1.3.3 |
| Status | Draft |
| Date | 2026-10-06 |

## Rendering Modes

Path tracing is a camera mode sharing scene geometry and material assets with raster rendering. Reference progressively accumulates a static image. Realtime supports changing scenes with a finite frame budget and optional Ray Reconstruction (RR). Both use the same material and lighting semantics.

## Responsibilities and Data Boundaries

| Stage | Responsibility |
|---|---|
| Scene input | Geometry, instances, materials, property overrides and motion, preserving object/asset identity. |
| Material evaluation | Shader-authored inputs at the ray hit, independent of final raster color. |
| Material model | Consistent scattering evaluation, sampling and probabilities. |
| Path integration | Lighting, visibility, continuation and termination using the model response. |
| Reconstruction/output | Matching image and surface guides, then delivery to camera processing. |

## Pipeline Architecture and Design Rationale

Unity owns material compilation/binding and hit geometry access; IllusionRP owns scattering models; RTXPT owns path transport and Realtime path organization. Fixed property-name conversion cannot replace arbitrary shader-authored input logic.

Material evaluation produces inputs, not a lit pixel. The model produces scattering; the integrator combines it with lighting and path history. Adapters reconcile conventions without duplicating the scattering model. Reference and Realtime share this boundary.

## Hit-to-Radiance Flow

1. Intersect a camera or continuing path with participating geometry; a miss resolves the applicable environment/background.
2. Evaluate authored material inputs and coverage at the accepted hit.
3. Prepare the scattering interaction, including any subsurface propagation to an exit surface.
4. Evaluate lighting and sample continuation using the same model and probability conventions.
5. Accumulate radiance or continue the path, then provide matching surface information to the output mode.

Direction, normal, probability and energy conventions are converted exactly once. Direct-light sampling and material sampling describe the same distribution; their weighting must neither double-count light nor apply continuation probability compensation twice. Discrete reflection/transmission events remain distinct from continuous scattering.

Realtime stable surfaces organize transport and reconstruction without introducing a different material model. RR consumes the resulting image/guides and never participates in scattering evaluation.

## Scene and Material Inputs

Use existing renderers, meshes, materials and property overrides with their original object/slot scope and precedence. Authored UVs, normal mapping and procedural inputs remain part of hit evaluation. Geometric orientation and ray placement are distinct from shading normals.

| Model | Contract |
|---|---|
| Lit | Surface reflection, coat, transmission and absorption where authored. |
| Fabric | Specular/cloth response with tangent-based Silk anisotropy and authored sheen. |
| Skin | Dual-lobe reflection and closed-volume subsurface scattering. |
| Hair | Card geometry with fiber-direction scattering and card-volume color semantics. |
| Unlit | Presentation color and emission, without a lit scattering model. |

[PRT baking](precomputed-radiance-transfer.md) shades its first hits with the same material evaluation, binding only the hit tables, not camera or frame state.

Coverage decides whether a hit exists; physical transmission decides scattering after acceptance. Main, shadow and subsurface rays preserve the material's coverage contract. Shadow rays continue straight through uncovered/transmitting portions with colored transmission.

Hair cards represent a collapsed hair volume: authored base color describes its overall albedo rather than a single strand's repeated-bounce mapping. Covered card transmission returns to the incident side; only uncovered coverage passes through the card. Raster roughness/specular and authored hair direction retain their meaning. Fabric uses the authored normal/tangent and specular/sheen relationship. These material-specific semantics are intentional, not automatic copies of an upstream renderer.

[Water](water.md) uses a refractive Lit interface and absorbing medium. A path escaping inside an unbounded absorbing medium contributes no light. Unsupported material entry points produce a diagnostic surface rather than silently substituting another model.

## Lighting and Path Integration

Transport includes environment, directional, point, spot and rectangle lights, emissive geometry and indirect lighting. Directional angular size changes shadow softness without changing unoccluded irradiance. Physical attenuation applies; unsupported range-cutoff and cookie behavior is diagnosed.

Rendering layers determine illumination and shadow participation. Custom shadow layers remain distinct from light layers and scene participation filters. The selected per-object directional source also follows its raster caster-layer relationship. Environment and emission affect all participating renderers. Shadow casting modes preserve visibility versus occlusion; light shadow toggles do not disable physical occlusion, and lights do not occlude one another.

Color, energy and world units are converted once at their input boundaries. Subsurface exit lighting and continuation use the actual exit position; numerical ray offsets cannot change physical surface/depth output. Sampling budgets and termination follow the selected mode across all models. Firefly controls are estimate controls, separate from material authoring and display processing.

## Camera Chains and Presentation

Camera chains continue through perfectly specular reflection/transmission until non-delta scattering. They carry presentation; scattered paths carry illumination.

- Camera misses see the configured camera background, while scattered misses see the lighting environment.
- Unlit camera hits show display color; other paths see only baked emission.
- Multiply overlays modify camera-chain presentation without scattering, occlusion, emission or artificial volume thickness; other paths pass through.
- Camera-only backdrops remain invisible to lighting paths.

## Camera Rendering Flow

Validate camera/capabilities, update consistent scene inputs, resolve history validity, execute the selected mode, reconstruct when requested, publish matching color/depth/motion, then retain valid history and restore temporary state.

Base Game and Scene View cameras participate. Overlay and depth-only cameras do not. Scene View uses Reference independently of Game-camera Realtime settings.

Reference accumulation resets when camera, scene, material, lighting or estimator settings change; display-only changes do not contaminate linear accumulation. Realtime represents ordinary motion through motion data and resets incompatible history on cuts, missing frames, size or configuration changes.

## Subsurface Scattering Contract

Raster and path tracing share diffusion-profile identity and unit semantics. Unassigned/unmatched profiles use the first valid Volume profile; neutral behavior applies only without a valid profile. Every camera publishes its own complete state within supported capacity.

Random Walk remains inside its assigned scattering group. A renderer is its own group by default; explicit grouping joins meshes that bound one volume without joining touching unrelated objects. Exit surfaces still apply coverage. Sampling, throughput and probabilities use the same volume model.

## Reconstruction and Output Contract

RR reconstructs Realtime output; disabling it exposes raw Realtime color. Its image, depth, motion, normal and material guides must describe the same frame/view and corresponding samples, with consistent dimensions, jitter and motion units. Hits, misses and unavailable history remain distinguishable.

Output enters normal exposure, tonemapping and post-processing after linear transport. Depth/motion remain consistent with the rendered result. RR and DLSS Neural Rendering have separate roles and resource lifetimes.

## Raster Feature Interoperability

Screen-space wet surfaces are not physical path-tracing inputs. Path-traced cameras reset wet publication; raster cameras retain normal wet rendering when path tracing does not run.

## Lifecycle and Failure Behavior

Camera accumulation, motion and reconstruction state remain isolated. Mode changes discard incompatible history. Path tracing replaces raster work only while applicable; disabled/unavailable cameras retain raster rendering. Requested but unavailable RR reports the reason and retains raster rather than silently changing the request.

Temporary camera state is restored without rewriting user assets. Size changes, camera removal and renderer disposal release obsolete resources. Device loss stops subsequent GPU work and preserves diagnostic evidence. Capability limitations and execution failures are reported distinctly.
