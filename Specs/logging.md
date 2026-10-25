# Logging

| Field | Value |
|---|---|
| Version | 1.4.4 |
| Status | Draft |
| Date | 2026-10-25 |
| Related Specs | [Rendering Pipeline](rendering-pipeline.md), [Inline Ray Tracing](inline-ray-tracing.md) |

IllusionRP, the UnityRHI package and the native plugins they ship write to the Unity log. The log is for the people who use them: a message either asks someone to act or records the result of an operation they started. Routine lifecycle and progress stay out of it unless verbose logging is on.

## Sources

Every message starts with the bracketed name of the component that wrote it, so a reader can tell which product to look at.

- **IllusionRP** marks everything the render pipeline package writes, at runtime and in the Editor. A message about one feature names that feature after the prefix.
- **UnityRHI** marks the UnityRHI package and its rendering hardware interface plugin.
- **IllusionRHI** marks the graphics plugin that IllusionRP's inline ray tracing preloads.

A message never carries the prefix of a component it does not come from, including a third-party library compiled into one of these plugins. Libraries that a plugin links log through it or not at all.

## Levels

- **Error.** A feature failed and its output is missing or wrong, such as a compile, initialization or dispatch failure. The message says what stopped working.
- **Warning.** A feature fell back or degraded in a way the reader can fix, such as a missing capability, a missing evaluator or an Editor restart that is needed. The message says what to do.
- **Info.** The result of a long operation the user started, such as generation, baking or a plugin update. Each operation writes one summary.
- **Verbose.** Lifecycle, timing, capability probing and per-instance detail that only helps while investigating. It is written only while verbose logging is on.

Errors and warnings cannot be turned off. A condition that persists, such as a material that keeps being drawn without an evaluator, is reported once per cause rather than every frame. A message about an object references it so the Editor can select it.

## Verbose logging

Verbose logging is off by default. In the Editor it is an IllusionRP project setting. In a player it is a runtime console variable when the Ceres integration is installed. Both control the same switch, so Play Mode follows the project setting until the console variable changes it.

Native plugins have no verbose switch. They write only warnings and errors.
