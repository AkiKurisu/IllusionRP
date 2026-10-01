# Validation

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-01 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md), [ASE Shader Workflow](ase-shader-workflow.md), [Shader Variant Stripping](shader-variant-stripping.md) |

Validation is how an IllusionRP change is shown to be correct before it is committed. It runs in four layers (static, import and compile, editor rendering, player), and each type of change has a minimum layer it must reach. This spec also defines the evidence required for rendering comparisons, temporal effects and performance measurements. A change counts as verified only up to the highest layer that actually ran.

Out of scope: application benchmark scenes and quality presets, and the tools that automate editor, capture or profiling steps.

## Evidence order

1. The design is closed: owner, data layout, pass order, fallback and disabled path are specified.
2. Static review covers producers, consumers, shader passes, resource declarations and the disabled path.
3. Unity compiles scripts, reloads the domain, imports assets and compiles shaders.
4. The change is rendered in the editor and, where required, in a player.

Specs and source code are the first evidence of correctness. Screenshots reveal symptoms but do not replace source or frame-structure evidence. GPU captures prove final passes, attachments, values and handoffs; they are not used to guess at gaps while design and code are incomplete.

## Layers

| Layer | Exit condition |
|---|---|
| Static | Spec, code, templates and generated shaders agree. Spec links, terms, versions and stated facts match the source. Committed files do not mix line endings, and shader sources (`.shader`, `.hlsl`, `.compute`, `.cginc`) use CRLF. |
| Import and compile | Unity is idle: `EditorApplication.isCompiling`, `EditorApplication.isUpdating` and `ShaderUtil.anythingCompiling` are all false after the last domain reload. The console shows no new C#, shader, compute, keyword, `UnityPerMaterial` or render graph errors. |
| Editor rendering | Scene and Game views render the change with the expected pass structure, resource values and image, under the conditions in [Rendering comparisons](#rendering-comparisons). |
| Player | A player of the target platform and graphics API is built and run. The record states build target, graphics API, URP asset, renderer data, scene, and the player log. |

- **Readiness.** A dropped tool connection or a single returned call is not proof that compilation finished.
- **Existing errors.** Warnings and errors that existed before the change are recorded separately from new ones. "The console is not empty" or "no dialog appeared" is not a result.
- **Reporting.** A report states which layers ran. Editor results are reported as editor results; a change that requires the player layer is not verified until a player has run.
- **No one-off code.** Tests, validators or runtime code whose only purpose is to prove one result are not added. Temporary scripts, helpers, export lists, captures and orphaned `.meta` files are removed before the change is committed.

## Required verification

| Change | Must verify |
|---|---|
| Specs only | Links, terms, version, status and agreement with the source. |
| Runtime C# | The affected assemblies compile; pass enqueueing, render graph dependencies, resource lifetime and disposal. |
| Editor and build C# | The editor assembly compiles; build target data, fail-open behavior, derived asset state, and a player build. |
| Raster shader or HLSL | Import on the target graphics APIs; pass names and LightMode tags, `UnityPerMaterial`, keywords, render state and representative materials. |
| Compute shader | Kernel and keyword import; thread groups, resource bindings, dispatch extent, signal range and consumers. |
| ASE template or graph | Per-graph export with convergence, and the template and generated diffs, as [ASE Shader Workflow](ase-shader-workflow.md) requires. |
| Frame resources or history | Render graph handles, attachment access, global publication, history validity and per-camera isolation, as [Render Resources](render-resources.md) requires. |
| Shader stripping | Players with stripping on and off, missing variants, and reachability of dedicated passes, as [Shader Variant Stripping](shader-variant-stripping.md) requires. |

A player build is required when a change touches shader keywords, pass stripping or build-time prefiltering; renderer resource references or player-only loading; graphics API, format capability or compute fallback; SRP Batcher, GPU Resident Drawer or DOTS instancing reachability; or camera, Volume or asset paths that differ between editor and player. Editor rendering never stands in for player variant and resource loading checks.

## Rendering comparisons

- **Stable conditions.** Comparisons use a fixed scene, a stable camera, a fixed resolution and completed temporal warmup, captured from the Game view or a player.
- **Same round.** When comparing paths, switches or versions, every side is captured again in the same round; earlier screenshots or captures are not reused.
- **CPU-side structure.** The Frame Debugger and the Render Graph Viewer establish pass order and declared reads and writes.
- **GPU structure.** A GPU frame capture establishes the draws and dispatches that ran and their order; whether a renderer list contains the intended shader pass and coverage; attachments, load and store actions, formats and access; resource values after the producer and before the consumer; keywords, pipeline state, blending, depth, stencil and the selected shader.
- **Citation.** An analysis names the capture it relies on, with its camera, scene, graphics API, the compared configuration, and when it was taken.
- **Both proofs.** A similar image does not prove a resource contract, and a present pass does not prove a correct image.

### Forward baseline

Forward and Forward+ are the production paths ([Rendering Pipeline](rendering-pipeline.md)). A change that touches pass scheduling, shader topology, depth, water, transparency or OIT, screen-space lighting or global shader state is compared on them before and after, with the same scene, camera, materials, Volume and quality settings, on both the final image and the passes, renderer lists, attachments and resource values. Work on another rendering path never changes Forward behavior to obtain a visible result; a path that cannot be completed stays disabled.

### Temporal effects

- **Warmup.** TAA, SSR accumulation, SSGI, screen-space shadow temporal filtering, color pyramid history and automatic exposure each need warmup before a capture.
- **Readiness status.** `IllusionRendererData.Active.TryGetTemporalCaptureStatus(camera, out status)` reports per camera whether history is established: `status.IsReady`, `status.Blockers` (no camera state, warming up, post-processing or TAA history reset, invalid SSGI, SSR or screen-space shadow history) and `status.RecommendedWarmupFrames`. Captures wait on this status, not on a fixed delay. It does not cover color pyramid history or exposure adaptation, which need their own warmup evidence.
- **Invalidation.** Camera cuts, descriptor or size changes, algorithm changes, history reallocation and post-processing resets are each verified to invalidate history.
- **Artifacts.** A temporal artifact is analyzed through the current input, the previous history, validity and rejection, and the final accumulation.

## Performance measurement

A performance conclusion distinguishes three things: static capability (renderer feature settings), runtime enabled state (runtime switches and Volume overrides), and the CPU and GPU work that actually runs. A setting being on is not evidence of cost. Evidence is classified as fact (provable from source, assets or build settings), inference (derived from static structure, not yet measured), hypothesis (awaiting a single-variable experiment), measurement (with its full context) or decision (an accepted trade-off with its reason and scope). Specs keep only facts, decisions and contracts; measurements belong in the change's task or pull request record.

### Evidence

Each measurement records:

- Unity, URP and IllusionRP versions, the code revision and whether the tree was modified;
- build target, graphics API, development or release build and scripting backend;
- CPU, GPU, driver, operating system, power mode and the GPU adapter actually used;
- scene and content mix: counts of characters, objects and lights, and the transparent, hair, skin and water content present;
- camera, resolution, render scale, HDR, VSync, target frame rate and quality level;
- URP asset, renderer data, renderer feature settings, runtime switch state and the effective Volume profile;
- warmup frames, sample interval, statistics and outlier handling;
- before and after screenshots, one structural capture and multi-frame metrics.

A capture without this context is used only for tool compatibility or diagnosis, never as a baseline.

### Control and isolation

- **Player timing.** Timing conclusions come from a player of the target platform, not from the editor.
- **Fixed workload.** Shaders, asset loading, temporal history and animation are warmed up; the camera transform or path, input, content state, lights and UI are fixed; a stable interval is sampled, and one structural capture is taken from the same interval.
- **Control ladder.** Measurements step through: IllusionRP features off or a minimal capability set; capabilities kept but the target effect turned off by its runtime switch (`r.*`, also in the Rendering Debugger's Illusion Features panel) or its Volume override; the shipping configuration; then one effect, resource or parameter changed at a time, with no other renderer, Volume or quality change.
- **Per step.** Each step confirms whether the pass enters the render graph; whether textures are created or imported; which draws, dispatches, copies, clears or queue syncs are submitted; whether extra depth, normal, motion vector, history or pyramid work is triggered; and whether multiple cameras repeat the cost.
- **Shared dependencies.** SSR, SSGI, ambient occlusion, water, OIT, subsurface scattering, shadows, bloom and fog can share or trigger each other's inputs. Each is measured alone first, then as an increment on the combination; a combined cost is not assumed to be the sum.

### Metrics

| Domain | Metrics |
|---|---|
| CPU | Main and render thread frame time (median, P95, P99); render graph record and execute, culling and BatchRendererGroup time; batches, SetPass calls, draw calls, triangles and vertices; GC allocation per frame, managed heap and significant native allocations. |
| GPU | GPU frame time (median, P95, P99); GPU time of the target passes; draw, dispatch, copy and clear counts; texture resolutions, formats, MSAA, load, store and resolve, and peak coexisting bytes; barriers, fences, queue transitions and unnecessary syncs. |
| Visual | Fixed-camera screenshots after the same warmup; local comparisons of transparent edges, hair, skin, shadows, reflections, water and temporal regions. An unexplained difference is a regression; an accepted difference states its reason, the affected content and the quality level it applies to. |

Multi-frame CPU timing comes from the Unity Profiler; pass order and declared resources from the Frame Debugger and Render Graph Viewer; draw-level structure, state and values from a GPU frame capture; multi-frame GPU timing and queue behavior from a GPU timing capture; memory from the Memory Profiler and the build report. A single-frame capture never stands in for multi-frame timing.

### Acceptance

- Control and candidate use the same build conditions, scene and camera.
- Warmup is complete and the sample interval is recorded.
- CPU, GPU, resource and visual evidence are all present.
- The comparison changes a single variable.
- First frame, fast motion, disocclusion, camera switches, dynamic resolution and multiple cameras show no regression.
- The conclusion names the platform and graphics API it applies to and is not extrapolated to others.

## Completion

- Specs, code, templates, generated shaders and the actual frame order agree.
- Every resource that crosses passes has a real render graph dependency, an owner, a format, a lifetime and a disabled behavior.
- No new C#, shader, compute, keyword, `UnityPerMaterial`, SRP Batcher or render graph errors.
- The Forward and Forward+ baseline shows no unintended change.
- Changes that require player or GPU capture evidence have it, rather than relying on static inference.
