# Sun Shafts

| Field | Value |
|---|---|
| Version | 1.3.2 |
| Status | Living |
| Date | 2026-10-04 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Render Resources](render-resources.md), [Transparency](transparency.md) |

Sun shafts are a screen-space radial glow around an anchor point. Pixels at far depth whose color exceeds a threshold are blurred radially toward the anchor and composited onto the camera color. The effect is not volumetric scattering: it ignores the real light direction and shadow maps, contributes only within a viewport radius of the anchor, and contributes nothing while the anchor is behind the camera. Volumetric fog, which follows the main light and its shadows, is complementary, and both can run in the same frame.

## Porting boundary

The effect ports a legacy built-in pipeline image effect, retaining its parameter set, algorithm and numeric behavior with these URP adaptations:

| Deviation | Reason |
|---|---|
| Depth is linearized with URP's `Linear01Depth(d, _ZBufferParams)`. | URP uses reversed Z, and the built-in macro of the same name has different semantics. |
| The sky-color mask alternative is not ported; the mask always uses depth. | It needs an immediate-mode sky clear that RenderGraph has no equivalent for, and IllusionRP does not own the sky pass. |
| The image-effect callback and temporary render textures become a render pass, blits and transient RenderGraph textures. | The built-in image-effect chain does not exist in URP, and temporaries must be declared to RenderGraph. |
| The anchor transform field becomes a registered `SunShaftCaster` component. | A Volume profile is an asset and cannot reference scene transforms. |
| The border drawn around the mask becomes a one-texel border clear inside the mask pass. | Immediate-mode drawing is illegal inside a RenderGraph pass; without it the radial blur drags edge texels into streaks. |
| The Screen and Add composite formulas become hardware blend states. | The camera color is the render attachment during the composite and cannot also be sampled. |
| UV flipping follows the `_BlitScaleBias` convention. | URP and RenderGraph flip differently from the built-in texel-size sign test. |
| The threshold is multiplied by the current exposure. | The camera color at the injection point is pre-exposed. |
| The effect is injected into linear HDR color before tonemapping. | URP's post-processing stack is one block with no HDR hook after tonemapping. This is the only deviation that changes appearance; threshold and intensity values need recalibration for this pipeline. |
| Per-blit parameters are recorded as global shader properties. | See [Parameter recording](#parameter-recording). |

Preserved from the original: the fixed blur reference height of 768, the LDR clamp of the mask, `iterations` limited to 1 to 4, the six-tap average, the resolution divisors, the maximum radius carried in the anchor vector, and the screen-center anchor when none is registered. The effect renders a single view and makes no stereo provision.

## Volume

`Illusion.Rendering.PostProcessing.SunShafts`, menu `Illusion/Sun Shafts`.

| Parameter | Type | Default | Range |
|---|---|---|---|
| `enable` | `BoolParameter` | false | |
| `thresholdColor` | `ColorParameter`, LDR, no alpha | (0.50196, 0.50196, 0.50196) | |
| `shaftsColor` | `ColorParameter`, LDR, no alpha | white | |
| `maxRadius` | `ClampedFloatParameter` | 0.75 | 0.1 to 1 |
| `blurRadius` | `ClampedFloatParameter` | 5 | 1 to 10 |
| `intensity` | `ClampedFloatParameter` | 5 | 0 to 10 |
| `iterations` | `ClampedIntParameter` | 2 | 1 to 4 |
| `resolution` | `SunShaftsResolutionParameter` | High | Low, Normal, High (divisor 4, 2, 1) |
| `blendMode` | `SunShaftsBlendModeParameter` | Add | Screen, Add |

The component is active when `enable` is on, `intensity` is above 0 and `maxRadius` is above 0. No parameter is a world distance, so [World Scale](world-scale.md) does not apply.

## Enablement

Sun shafts run for a camera only when all of these hold:

- the renderer feature's `sunShafts` setting is on;
- the runtime switch `r.sunshafts` is on (also exposed in the Rendering Debugger under Illusion Features, Post Processing);
- post-processing is enabled for the pipeline and for the camera;
- the camera is not a Preview camera;
- the Volume component is active.

When any condition fails the pass is not recorded, and the frame is identical to one with the effect fully disabled.

## Anchor

- **Registration.** `SunShaftCaster` (`Illusion/Sun Shaft Caster`) registers its transform while it is enabled, in edit mode as well as in play mode. The most recently enabled caster that is still active and enabled is the anchor for every camera.
- **Position.** The anchor is the caster position converted to the camera's viewport. Without a caster it is (0.5, 0.5, 0).
- **Behind the camera.** When the anchor's viewport depth is negative, the shaft color is zero.
- **Shader input.** `_SunShaftsSunPosition` holds the viewport x, y and depth of the anchor and `maxRadius` in w.

## Passes

The pass runs after transparent rendering and before volumetric fog and automatic exposure, where the camera color is linear scene color, HDR when the camera renders HDR, multiplied by the current exposure. [Rendering Pipeline](rendering-pipeline.md) owns the frame order. The shader `Hidden/SunShafts` has four named passes, `SunShaftsDepthMask`, `SunShaftsRadialBlur`, `SunShaftsScreen` and `SunShaftsAdd`, and declares no keywords.

- **Depth mask.** At the camera target size divided by the resolution divisor, in an R8G8B8A8 UNorm target, each pixel whose linear 0-to-1 depth exceeds 0.99 stores `sum(max(color - threshold * exposure, 0)) * saturate(maxRadius - length(anchor.xy - uv))`, summed over RGB; all other pixels and a border one mask texel wide store zero. Stored values clamp to the range 0 to 1.
- **Radial blur.** `iterations` rounds of two blits ping-pong between the mask and a second texture of the same size. Each blit averages six taps that step from the pixel toward the anchor by `(anchor.xy - uv) * offset`. The offset of the first blit is `blurRadius / 768`, and the offset of blit k after it is `blurRadius * 6k / 768`.
- **Composite.** At full resolution into the camera color, the result `saturate(mask * shaftsColor * intensity)` is blended with Screen (`Blend One OneMinusSrcColor`) or Add (`Blend One One`).

## Inputs and resources

- **Depth.** The mask reads the camera depth texture at the pass position. When transparent depth post passes run, that depth includes the transparent surfaces drawn by them, so those surfaces block shafts; other transparent surfaces do not. See [Transparency](transparency.md).
- **Resources.** All three textures are transient RenderGraph textures sized from the camera target; the effect keeps no per-camera history. The shader is included in builds through the renderer resources' always-included shader list, as described in [Render Resources](render-resources.md).

## Parameter recording

The radial blur changes its step between blits that share one material inside a single RenderGraph pass. Material properties resolve when the command buffer executes, so every blit would see the last value; global shader properties are recorded in order. All sun shaft shader inputs (`_SunShaftsSunPosition`, `_SunShaftsThreshold`, `_SunShaftsMaskTexelSize`, `_SunShaftsBlurRadius4`, `_SunShaftsColor`) are therefore set as globals, and each pass allows global state modification.
