# Experimental DLSS Neural Rendering

Experimental DLSS Neural Rendering is an extension based on [Kuan-Mi/UnityDLSSNR](https://github.com/Kuan-Mi/UnityDLSSNR). It runs after post-processing on the final Game Camera at native resolution.

## Setup

1. Place `nvngx_dlssnr.dll` in `Packages/top.kuanmi.unityrhi.native/Plugins/x86_64`.
2. Configure the plugin for Windows x64 Editor and Player.
3. Restart Unity.
4. Add `Illusion > DLSS Neural Rendering` to a Volume.

> [!NOTE]
> Windows x64, Direct3D 12 and a compatible NVIDIA GPU are required. `nvngx_dlssnr.dll` is not included.

## Debugging

Use `r.dlssnr` to enable the effect and `r.debug.dlssnr` to inspect its inputs. Runtime status is available in the Illusion Rendering Debugger.
