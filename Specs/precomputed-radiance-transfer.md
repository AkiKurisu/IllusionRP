# Precomputed Radiance Transfer

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-06 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Materials and Shaders](materials-and-shaders.md) |

PRT provides diffuse indirect lighting for fixed baked geometry and material inputs with runtime directional, point and spot lights and environment lighting. Runtime relighting, visibility and sampling use raster and compute; they do not require ray tracing. The representation is SH9, with patch averaging and probe interpolation as explicit approximations.

## Execution order and scheduling

The reference is [The Division, GDC 2016](https://mrakobes.com/Nikolay.Stefanov.GDC.2016.pdf): sectors and transport on slides 22–26, brick/probe evaluation on 38, visibility and light queries on 41–42, closest-probe feedback on 46, and sector updates/volume publication on 54–55. The following budgets are project defaults, not parameters or performance claims from the presentation.

| Step | Work | Scheduling |
|---|---|---|
| 1 | Snapshot static geometry, authored material inputs and probe placement. | Full offline bake; no runtime scene-object observation. |
| 2 | Partition probe columns in XZ; merge surfels/bricks and build angular factors and independent sky samples within each sector. | Full offline bake; transport ownership never crosses a sector. A sector's surfels may be far outside its probe bounds. |
| 3 | Associate every merged surfel with the globally closest baked-valid probe. | Offline, using nominal grid positions, ties by lower global ID. No candidate means zero feedback. |
| 4 | Load the CPU asset and initialize global probe SH, metadata and ready state. | Once per asset binding. Sector transport is not uploaded here. |
| 5 | Upload sector transport and saved visibility caches. | Byte-budgeted over frames; incomplete sectors cannot relight. |
| 6 | Select the camera sector and a round-robin background sector. | Default two distinct sectors per frame. With budget one, alternate near and background. An uploading background task retains its cursor until updated. |
| 7 | Query lights against each selected sector's surfel AABB and relight its bricks. | All bricks of selected sectors only. A brick is never split across frames. |
| 8 | Integrate those sectors' probes using their new brick radiance and sky samples. | Complete selected sectors; do not wait for other sectors. |
| 9 | Commit updated global probe IDs, then publish dirty camera-volume slots. | Same frame, without a readback gate. All selected sectors read the same frame-start SH before any commit. |
| 10 | Interpolate the published volume in material shading. | Normal pixel shading; separate from offline capture. |

Defaults: 4 by 4 probe columns per sector, full Y extent; two sectors per frame; 256 MiB sector GPU allocation budget; 4 MiB upload budget per frame. A 9 by 4 by 5 grid has six sectors. Oversized sectors require a smaller bake partition, not a full-scene dispatch fallback. CPU retains all baked transport. GPU baking, disk streaming and multiple cascades are outside this implementation; logical grid coordinates, global probe IDs and sector-local GPU indices remain distinct.

## Offline transport and baking

The root asset owns global probe metadata and a sector directory. Each sector owns its probe global IDs, local probe ranges, surfels, bricks, angular factors, sky samples and surfel AABB. Ranges and brick IDs are sector-local; feedback IDs are global. The transport asset records an axis-aligned world grid, participating geometry bounds, geometry/material/transport-settings and complete authoring-input signatures, the deterministic sample count and seed, capture offsets and geometry reference time. Origin, integer grid dimensions and spacing must match the volume; a volume with world rotation or scale cannot reinterpret this world-space bake.

| Data | Fields | GPU stride |
|---|---|---:|
| Surfel | World position, normal, diffuse reflectance; flags, rendering layers, object layers and material key; global nearest valid probe ID. | 56 B |
| Brick range | Start and count of geometry surfels. | 8 B |
| Brick factor | Brick index and nine signed angular transfer coefficients. | 40 B |
| Global probe metadata | Capture offset and integer intensity/validity; CPU root data. | 16 B |
| Sector probe | Factor and sky start/count; capture offset; integer intensity/validity. | 32 B |
| Sky sample | World direction and integral weight. | 16 B |

Ranges use start and count. Geometry arrays contain no sky misses. Empty geometry and factor arrays are valid for an all-sky asset. Intensity and validity use 24 and 8 bits of an integer; they never travel through a float bit pattern.

Baked probe metadata stores geometry validity with unit intensity. Runtime adjustment volumes apply intensity and invalidation separately, so removing an authoring mask can restore a geometrically valid probe. The complete authoring signature rejects changes during a bake; dynamic metadata changes do not invalidate unchanged transport.

The baker creates one deterministic direction/integral-weight table in C#. Compute capture and CPU projection use that same table. A uniform sphere entry has weight `4*pi/N`; a probe-to-brick weight is the sum of `Y_k(direction)*weight` over original samples assigned to the brick. There is no cosine-based renormalization or average-direction reprojection. Every relationship is evaluated, including ranges longer than a thread group.

The patch partition retains principal normal direction, material identity and rendering/object layer boundaries. Four-unit patches and 0.1-unit position merging are representation approximations, not recovered high-frequency lighting. Sky visibility retains its own direction/weight list and is combined with environment radiance independently.

The raster baker snapshots enabled `ContributeGI` LOD0 meshes, transforms, materials, property blocks and a fixed shader time. Skinned geometry is baked to a temporary mesh. Empty particle geometry is reported and included in the geometry signature; nonempty camera-facing particles require a supported snapshot and fail this backend. Unsupported geometry/material inputs are diagnosed rather than silently omitted or replaced with white.

Raster capture uses the material's existing `ForwardGBuffer` pass with the bake-only `_PRT_CAPTURE` variant; it does not add a material pass. The variant shares authored vertex, main Forward normal, albedo and binary coverage inputs and orients two-sided shading normals to the visible face. Screen-space GBuffer normal override ports are not the bake normal source. Global capture mode selects world position, normalized normal, diffuse reflectance or exact numeric metadata. Capture channels use FP32. `_PRTMetadata` carries rendering-layer low/high 16-bit halves, material key and object-layer index; keys remain below `2^24`. Clear normal W=0 denotes a miss. Fractional transparent continuation, emission transport and full BSDF transport are outside this diffuse first-hit contract.

Capture disables alpha-to-coverage and MSAA: output alpha carries semantic data, including an object-layer index that may be zero. Binary material coverage is applied by the authored alpha discard and must agree across capture modes.

Player and AssetBundle shader builds always strip variants with `_PRT_CAPTURE` enabled, including when Strip Unused Variants is disabled. Editor compilation retains them for baking.

ASE capture code belongs to the existing ForwardGBuffer template and linked graph inputs, not manual generated-shader edits. The participating shader is regenerated and checked for export convergence while preserving authored CustomOptions. Lit, Fabric, Skin and Hair capture through their existing pass. The bake keyword and integer mode are restored on scope exit. ForwardGBuffer uses overwrite blending, disabled alpha-to-coverage and On/LEqual depth, matching the runtime prepass overrides.

Virtual offset uses a private triangle BVH from the same mesh snapshot; it does not rely on user colliders or change global physics settings. Thin alpha-tested, transparent and double-sided surfaces do not define occupancy. Twenty-six directions determine whether the point is inside geometry. Bounded exit attempts are verified; failure invalidates the probe. Adjustment volumes use stable identity order, and offsets are persisted.

Capture renders probes sequentially in batches of 32 and reads each batch back asynchronously. An owned Editor update subscription polls requests and is released when they complete, including background Edit Mode. Cancellation completes outstanding readback before releasing its buffers. An incomplete or changed-input bake does not clear or replace the old asset. Reflection normalization captures HDR radiance and uses deterministic directions.

## Lighting and visibility snapshots

Relight input is a world-space set of active directional, point and spot lights intersecting the selected sector's surfel AABB. It does not use the current camera's visible-light list or screen-space Forward+ clusters. Color space, intensity, bounce intensity, range/cone attenuation and object/rendering layers follow the raster lighting conventions; the path tracer's separate physical-color conversion is not applied.

Lighting and visibility changes are tracked separately. Color/intensity changes are acquired on the next sector update without discarding unchanged visibility. Light identity, type, transform, range/cone, shadow settings, layers and quality inputs affect visibility. Visibility epochs are allocated from one monotonic source and identify the light, so resized or reordered light tables cannot reuse another light's cached entries.

Each shadowed light has a 16-byte entry per surfel: raw visibility, epoch, scene tick and valid state. An update first samples the existing URP shadow map when the light state and geometric coverage agree, then writes that sample to the cache. Outside coverage, an entry from the same visibility epoch supplies the last sampled value regardless of age. Age is diagnostic only. A surfel without a valid sample uses unoccluded direct lighting for this evaluation and remains explicitly unknown; that approximation is never written as a valid cache sample.

PRT does not create shadow textures or render shadow caster passes. Continuous sector updates acquire newly covered shadow samples. Geometry and surface materials are fixed by the bake; changes require rebaking. Runtime PRT does not observe scene renderer, mesh, bone, terrain or surface-material changes and does not invalidate cached visibility for them. Runtime input observation covers lights, environment, probe settings and existing shadow-map coverage.

World light records occupy 96 bytes and shadow face records 128 bytes. The cache stores raw visibility; shadow strength is applied once during evaluation. Brick-average fallback promotion is not used. Diagnostic preview explicitly identifies the displayed light and reports cache, map and unknown states.

Cookies are diagnosed and not evaluated by this diffuse baseline. Unknown shadow coverage does not block a sector from publishing; it remains a visible diagnostic approximation.

## GPU residency, feedback and publication

The renderer pass owns global FP32 SH, committed metadata and per-probe ready state. Sector residents own local transport, temporary probe SH, brick radiance, world-light records and per-surfel visibility caches. CPU keeps the full baked asset and visibility snapshots of evicted sectors. Probe SH survives eviction of its transport.

Allocation, upload-in-progress, in-use and pending-eviction allocations all count toward the sector budget. Global probe buffers, the environment cube and camera windows are counted separately. Counts and strides describe resource payload, not driver heap allocation. The current camera sector and background task have priority; other residents are evicted least-recently-used. Before releasing a resident, record asynchronous visibility readback, store cache entries with light identity/epoch on CPU, and release only after completion. A failed readback retains the resident and reports the failure. Reload restores matching identities and epochs; stale light input never restores trustworthy visibility. Insufficient budget preserves already published GI and reports residency pressure. There is no full-world-upload fallback.

Surfel feedback evaluates the baked nearest global probe at the surfel normal, applying one normalized cosine convolution and intensity/validity. Unready or invalid probes contribute zero. The final material sampler still interpolates the camera volume. These are deliberately different operations.

Every selected sector reads frame-start global SH. Integration writes sector scratch buffers; commits run only after all selected integrations, scattering just the updated IDs into the global SH and ready state. No whole-grid SH copy, generation swap, convergence stop or whole-grid readback lies on the publication path. Residuals are diagnostics only; continuous TOD does not cancel background progress.

The primary Game camera drives the scheduler. A SceneView may drive it when no Game camera renders. Other cameras publish the shared results without additional relight. The rendering context supplies a monotonic shared tick; per-camera counters and Edit Mode Time.frameCount are not visibility clocks.

Each camera publishes only sector changes since its last publication. A moving window rebuilds its small layout completely; moving or teleporting does not cause full-world relight. Publication bookkeeping advances in the executing RenderGraph pass. Layout indices/dimensions are typed integers. Ready state is distinct from baked validity. Asset replacement releases residents, global buffers and publications, including pending-readback ownership.

Sampling is inside the first-to-last probe domain. At its upper boundary the final cell uses interpolation rate 1. A singleton axis covers half a spacing on either side and samples its only probe. Window misses, invalid slots, outside-domain positions and zero effective weight return the supplied fallback; multi-bounce feedback supplies zero. Reflection normalization returns neutral factor 1 without valid PRT coverage.

The asset and solver store raw signed FP32 SH9 radiance. Publication stores nine FP32 coefficient pages: RGB is preweighted by intensity and validity, and alpha stores validity. Hardware trilinear filtering supplies the numerator and denominator with the same spatial weights. Sampling divides by filtered validity, evaluates the SH basis and normalized cosine convolution once, then clamps the final irradiance. Nearest-probe feedback uses the same final-clamp and cosine convention without spatial interpolation. Per-corner clamping is not equivalent and can bias the interpolated field bright.

The sampling basis is evaluated once per direction, with fixed coefficient loops expanded for streaming accumulation. Integrated outputs are checked for non-finite values before sector commit; pixel sampling does not repeat finite checks on every coefficient. Where diffuse and reflection normalization need the same neighborhood, both directions share the nine filtered coefficient queries. The single-mip coefficient volume uses Core's shared `sampler_LinearClamp` at LOD zero, matching its bilinear/clamp texture settings without allocating a separate sampler. Packed metadata remains R32_UInt for diagnostics; the pixel sampler uses the FP32 validity alpha.

Hardware filter weights have finite precision independent of FP32 coefficient storage. This is a runtime spatial-interpolation approximation, not bitwise equivalence with manual FP32/FP64 weights. Constant-field preservation and shader arithmetic are verified separately from spatial-filter error; producer/integration tolerances do not imply those spatial errors meet the same limit. Quality acceptance and performance budgets require matched scene/state measurements.

Every eligible camera publishes committed or neutral resources, including idle frames. Reflection/Preview or a runtime switch cannot leave a later Game camera with inherited empty globals. Replaced transport data, asset binding, layout and settings changes reload sources atomically. Disable/destruction releases owners; re-enable can recover without a cached renderer-capability flag blocking it.

## Resource declarations and observation

RenderGraph declares the actual source, lighting, shadow, metadata, SH, residual, readback and publication accesses. Buffer/global publication does not substitute for those dependencies. Persistent RTHandles and buffers are released by their allocating owner; callbacks do not access disposed solve state.

Live camera constants, probe metadata and reflection normalization values are uploaded by recorded GPU commands with buffer write dependencies. Initial allocation may upload directly; per-frame publication does not synchronously call GraphicsBuffer.SetData. Unchanged camera layout/content does not upload its constants again.

Status reports selected/resident/uploading/evicting sectors, budget pressure, uploaded bytes, resident/peak GPU bytes, global fixed bytes, sector update ages, residual and non-finite diagnostics. Diagnostics never gate normal publication.

Irradiance debug spheres evaluate each probe's committed SH directly with normalized per-fragment world normals and one cosine/PI convolution. They do not use the spatially filtered publication texture. The sphere center marks the grid location; saved capture offsets identify where its transport was sampled.

Validation uses fixed geometry/lighting/camera/seed, CPU projection of the same captured data, linear image/SH exports, genuine sector-update GPU captures, and resource-lifecycle checks. Reversing the order of sectors selected in one frame must produce the same results. Budget changes intentionally change response latency; static-input comparisons use stabilized results. Task measurements and captures live outside this living contract.
