# Experimental DLSS Neural Rendering

Extension based on [Kuan-Mi/UnityDLSSNR](https://github.com/Kuan-Mi/UnityDLSSNR). Runs after post-processing on the final Game Camera at native resolution.

## Setup

1. Enable **DLSS Neural Rendering** on **Illusion Graphics**.
2. Place `nvngx_dlssnr.dll` in `Packages/com.kurisu.unityrhi/Plugins/x86_64`.
3. Configure the plugin for Windows x64 Editor and Player.
4. Restart Unity.
5. Add **Illusion/DLSS Neural Rendering** and turn **State** on.

> [!NOTE]
> Windows x64, Direct3D 12 and a compatible NVIDIA GPU are required. `nvngx_dlssnr.dll` is not included.

## Volume

| Property | Description |
|----------|-------------|
| **Preset** | **Default**, **Preset 1**, **Preset 2**, **Preset 3**. |
| **Style** | **Default**, **Natural**, **Cinematic**. |
| **Intensity** | Strength, 0 to 2. |
| **Local Tone** / **Local Structure** | Local tone and structure. |
| **Skin Structure** | `-1` keeps the default. |
| **Auto Mask** | Builds the mask in the effect. |
| **UI Correction** | Corrects UI drawn into the color target. |

## Debugging

`r.dlssnr` toggles the effect. `r.debug.dlssnr` views color, motion vectors, motion magnitude, device depth or linear eye depth. `r.debug.dlssnr.motionrange` and `r.debug.dlssnr.depthrange` scale the motion and depth views.
