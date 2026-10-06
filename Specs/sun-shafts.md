# Sun Shafts

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Transparency](transparency.md) |

Sun shafts are a screen-space radial glow around an anchor. Far-depth bright regions are blurred toward that anchor and composited onto camera color. They are not volumetric scattering: they do not follow real light direction or shadow maps and may run alongside volumetric fog.

## Porting boundary

The legacy effect is adapted to URP depth, exposure and RenderGraph ownership. It uses a depth mask rather than a sky-clear alternative and runs in linear HDR before tonemapping, so legacy intensity/threshold values require recalibration. It currently renders one view without stereo support.

## Volume

The Volume controls brightness threshold, color, viewport extent, blur quality and composition. Parameters are screen-space or dimensionless and do not follow [World Scale](world-scale.md). An inactive Volume contributes no effect.

## Enablement

Renderer capability, runtime permission, camera/pipeline post-processing and an active Volume are all required. Preview cameras are excluded. A disabled effect leaves camera color unchanged and records no work.

## Anchor

The most recently enabled active scene caster supplies the shared anchor. Without one, the viewport center is used. Each camera projects the anchor independently; an anchor behind that camera contributes nothing. A scene registration supplies this relationship because a Volume asset cannot own a scene transform reference.

## Passes

After transparents and before volumetric fog and automatic exposure, the effect builds a thresholded far-depth mask, blurs it radially and composites it with Screen or Add blending. Thresholding accounts for the exposure already applied to camera color. The mask border is cleared so blur cannot drag screen-edge texels into streaks.

## Inputs and resources

The mask reads depth at its injection point: transparent surfaces with post-depth can block shafts; other transparency cannot. All intermediates are frame-local, sized from the camera target, and explicitly declared to RenderGraph. The effect has no temporal history; required shaders are included through pipeline resources.

## Parameter recording

Each recorded blur operation must retain its own parameters until GPU execution. Shared mutable material state cannot make earlier operations observe a later operation's settings. Reads, writes and publication follow [Render Resources](render-resources.md).
