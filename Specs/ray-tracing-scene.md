# Ray Tracing Scene

| Field | Value |
|---|---|
| Version | 1.4.3 |
| Status | Living |
| Date | 2026-10-18 |
| Related Specs | [Path Tracing](path-tracing.md), [Ray Traced Global Illumination](ray-traced-global-illumination.md), [Area Lights](area-lights.md), [Render Resources](render-resources.md), [Shader Variant Stripping](shader-variant-stripping.md) |

The ray tracing scene is the acceleration structure scene that [path tracing](path-tracing.md) and [ray traced global illumination](ray-traced-global-illumination.md) trace. Both consumers see the same instances, coverage, materials, lights and lighting environment, so RTGI traces the scene that path tracing renders.

## Ownership

One scene service per renderer feature owns the acceleration structure, the instance and material tables, emissive geometry, the collected lights with their rectangle light proxies, and the environment importance data. It is a renderer-level service that either consumer can update; neither consumer depends on the other existing.

Consumer-specific state stays with its consumer. Accumulation, stable planes, Ray Reconstruction and the adaptive light sampling feedback that path tracing derives from its own camera outputs belong to path tracing; reservoirs, radiance caches and denoiser history belong to RTGI.

## Participation

The Path Tracing Volume's participation layers and directional light angular size define one participation configuration for both consumers. A renderer participates when it is an enabled mesh or skinned mesh renderer, active in its hierarchy, on a participation layer, with a ray tracing mode other than Off, and either saved with its scene or created by the pipeline itself, as rectangle light proxies are.

## Update

- **Once per frame.** Each participation configuration is built at most once per frame, by whichever consumer requests it first. Its LOD selection follows that first camera.
- **Before raster culling.** A raster camera that consumes the scene requests it before URP culls the camera, because adding skinned renderers to the acceleration structure after the camera's shadow casters are culled invalidates that culling. Path tracing cameras cull no raster shadow casters and request it while the render graph records.
- **GPU build.** The scene's GPU build is declared as the first work of the first consumer in the frame, so every later consumer reads a complete scene.

## Change tracking

A configuration finds its participating renderers with one scene scan, then follows engine change notifications for renderer participation, hierarchy, materials, property overrides and mesh data. Only affected renderers are read again, so per-frame cost follows the changes rather than the scene size. The acceleration structure is still culled every frame for transforms and LOD.

Scene loads and unloads arrive as renderer creation and destruction. Changes are retained however long a configuration goes without an update, so it never needs another full scan.

The scene state that path tracing accumulation and RTGI history validation compare advances when the tables change or when a referenced material's content changes. Rewriting a material or property override with identical values does not advance it. Mesh data written in place, such as simulated cloth, advances it only when the tables change.

## Lifetime and availability

The service exists when either path tracing or RTGI is enabled in the renderer settings and the device supports ray tracing. A configuration that no consumer has used for a short grace period is released; disposing the feature releases the service.

Build reachability of the scene support shaders follows [Shader Variant Stripping](shader-variant-stripping.md#resource-stripping).
